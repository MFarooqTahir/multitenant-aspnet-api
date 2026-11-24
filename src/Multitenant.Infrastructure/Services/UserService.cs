using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Multitenant.Application.Abstractions.Identity;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common.Paging;
using Multitenant.Application.Common.Search;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Users;
using Multitenant.Application.Errors.Exceptions;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Services;

/// <summary>
/// Tenant-scoped user administration. Authorization rules live here rather than in
/// controllers so they apply regardless of how the service is called.
/// </summary>
public sealed class UserService(
    AppDbContext dbContext,
    TenantRowLock tenantRowLock,
    UserManager<AppUser> userManager,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IUserProvisioningService userProvisioning,
    IAccountEmailService accountEmailService) : IUserService
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "email",
        "displayname",
        "createdat",
    };

    public async Task<PagedResult<UserResponse>> ListAsync(
        UserQuery query,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = Pagination.Normalize(query.Page, query.PageSize);
        var users = dbContext.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = SearchPattern.Contains(query.Search);
            users = users.Where(x =>
                EF.Functions.ILike(x.Email ?? string.Empty, search, SearchPattern.EscapeCharacter)
                || EF.Functions.ILike(x.DisplayName, search, SearchPattern.EscapeCharacter));
        }

        if (query.IsActive is not null)
        {
            users = users.Where(x => x.IsActive == query.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var roleName = TenantRoles.Normalize(query.Role).ToUpperInvariant();
            var roleId = await dbContext.Roles
                .Where(x => x.NormalizedName == roleName)
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (roleId == Guid.Empty)
            {
                return new PagedResult<UserResponse>([], page, pageSize, 0);
            }

            users = users.Where(x =>
                dbContext.UserRoles.Any(ur => ur.UserId == x.Id && ur.RoleId == roleId));
        }

        var totalCount = await users.CountAsync(cancellationToken);
        var (field, descending) = Sorting.Parse(query.Sort, SortFields, "createdat");

        users = field switch
        {
            "email" => descending
                ? users.OrderByDescending(x => x.Email).ThenByDescending(x => x.Id)
                : users.OrderBy(x => x.Email).ThenBy(x => x.Id),
            "displayname" => descending
                ? users.OrderByDescending(x => x.DisplayName).ThenByDescending(x => x.Id)
                : users.OrderBy(x => x.DisplayName).ThenBy(x => x.Id),
            _ => descending
                ? users.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
                : users.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
        };

        var items = await users
            .Skip(Pagination.Skip(page, pageSize))
            .Take(pageSize)
            .Select(x => new UserResponse(
                x.Id,
                x.Email ?? string.Empty,
                x.DisplayName,
                (
                    from userRole in dbContext.UserRoles
                    join role in dbContext.Roles on userRole.RoleId equals role.Id
                    where userRole.UserId == x.Id
                    select role.Name
                ).FirstOrDefault() ?? TenantRoles.Member,
                x.IsActive,
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<UserResponse>(items, page, pageSize, totalCount);
    }

    public async Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .Where(x => x.Id == id)
            .Select(x => new UserResponse(
                x.Id,
                x.Email ?? string.Empty,
                x.DisplayName,
                (
                    from userRole in dbContext.UserRoles
                    join role in dbContext.Roles on userRole.RoleId equals role.Id
                    where userRole.UserId == x.Id
                    select role.Name
                ).FirstOrDefault() ?? TenantRoles.Member,
                x.IsActive,
                x.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("User not found.");
    }

    public async Task<UserResponse> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var requestedRole = TenantRoles.Normalize(request.Role);
        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedException();
        var tenantSlug = tenantContext.Slug ?? throw new UnauthorizedException();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await tenantRowLock.AcquireAsync(cancellationToken);

        var actor = await GetActorAsync(cancellationToken);
        var actorRole = TenantRoles.Normalize(await GetRoleAsync(actor));

        if (actorRole == TenantRoles.Member)
        {
            throw new ForbiddenException("Members cannot create users.", "role_not_allowed");
        }

        if (requestedRole == TenantRoles.Owner && actorRole != TenantRoles.Owner)
        {
            throw new ForbiddenException("Only an Owner can create another Owner.", "role_not_allowed");
        }

        var user = await userProvisioning.CreateAsync(
            tenantId,
            tenantSlug,
            request.Email,
            request.DisplayName,
            request.Password,
            requestedRole,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        // Delivery is a side effect and must not hold the tenant lock or roll back
        // an already-committed user.
        await accountEmailService.SendConfirmationEmailAsync(user, cancellationToken);

        return userProvisioning.ToResponse(user, requestedRole);
    }

    public async Task<UserResponse> UpdateRoleAsync(
        Guid id,
        string role,
        CancellationToken cancellationToken)
    {
        var requestedRole = TenantRoles.Normalize(role);

        // Lock first, then re-read actor and target roles. Otherwise an Admin can
        // act on a stale Member role while an Owner promotes that user concurrently.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await tenantRowLock.AcquireAsync(cancellationToken);

        var actor = await GetActorAsync(cancellationToken);
        var actorRole = TenantRoles.Normalize(await GetRoleAsync(actor));

        if (actorRole == TenantRoles.Member)
        {
            throw new ForbiddenException("Members cannot change roles.", "role_not_allowed");
        }

        var target = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (target.Id == currentUser.UserId)
        {
            throw new ForbiddenException("You cannot change your own role.", "self_role_change");
        }

        var targetRoles = await userManager.GetRolesAsync(target);
        var currentRole = targetRoles.FirstOrDefault() ?? TenantRoles.Member;

        if ((currentRole == TenantRoles.Owner || requestedRole == TenantRoles.Owner)
            && actorRole != TenantRoles.Owner)
        {
            throw new ForbiddenException("Only an Owner can manage the Owner role.", "role_not_allowed");
        }

        if (currentRole == requestedRole)
        {
            return userProvisioning.ToResponse(target, requestedRole);
        }

        if (currentRole == TenantRoles.Owner && await CountOwnersAsync(cancellationToken) <= 1)
        {
            throw new ConflictException("The last Owner cannot be demoted.", "last_owner");
        }

        if (targetRoles.Count > 0)
        {
            var removed = await userManager.RemoveFromRolesAsync(target, targetRoles);
            EnsureSucceeded(removed, "role_update_failed");
        }

        var added = await userManager.AddToRoleAsync(target, requestedRole);
        EnsureSucceeded(added, "role_update_failed");

        // Invalidate every access token issued with the previous role. Refresh tokens
        // stay valid, so the next refresh returns a token carrying the new role.
        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(target), "role_update_failed");
        await transaction.CommitAsync(cancellationToken);

        return userProvisioning.ToResponse(target, requestedRole);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await tenantRowLock.AcquireAsync(cancellationToken);

        var actor = await GetActorAsync(cancellationToken);
        var actorRole = TenantRoles.Normalize(await GetRoleAsync(actor));

        if (actorRole == TenantRoles.Member)
        {
            throw new ForbiddenException("Members cannot delete users.", "role_not_allowed");
        }

        var target = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (target.Id == currentUser.UserId)
        {
            throw new ForbiddenException("You cannot delete your own account.", "self_delete");
        }

        var targetRoles = await userManager.GetRolesAsync(target);
        var targetRole = targetRoles.FirstOrDefault() ?? TenantRoles.Member;

        if (targetRole == TenantRoles.Owner && actorRole != TenantRoles.Owner)
        {
            throw new ForbiddenException("Only an Owner can delete an Owner.", "role_not_allowed");
        }

        if (targetRole == TenantRoles.Owner && await CountOwnersAsync(cancellationToken) <= 1)
        {
            throw new ConflictException("The last Owner cannot be deleted.", "last_owner");
        }

        EnsureSucceeded(await userManager.DeleteAsync(target), "user_delete_failed");
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<AppUser> GetActorAsync(CancellationToken cancellationToken)
    {
        var actor = await dbContext.Users
            .FirstOrDefaultAsync(x => x.Id == currentUser.UserId, cancellationToken)
            ?? throw new UnauthorizedException();

        if (!actor.IsActive)
        {
            throw new UnauthorizedException();
        }

        return actor;
    }

    private async Task<string> GetRoleAsync(AppUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return roles.FirstOrDefault() ?? TenantRoles.Member;
    }

    private Task<int> CountOwnersAsync(CancellationToken cancellationToken)
    {
        var ownerRole = TenantRoles.Owner.ToUpperInvariant();

        // Joining AppUsers applies the tenant query filter, so Owners in other
        // tenants are not counted. Without this join the "last Owner" guard would
        // look at the whole database.
        return (
                from userRole in dbContext.UserRoles
                join role in dbContext.Roles on userRole.RoleId equals role.Id
                join user in dbContext.Users on userRole.UserId equals user.Id
                where role.NormalizedName == ownerRole
                select userRole.UserId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    private static void EnsureSucceeded(IdentityResult result, string code)
    {
        if (!result.Succeeded)
        {
            throw new ConflictException(
                string.Join(" ", result.Errors.Select(x => x.Description)),
                code);
        }
    }
}
