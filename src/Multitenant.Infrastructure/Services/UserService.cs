using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Multitenant.Application.Abstractions.Identity;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common.Paging;
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
            var search = $"%{query.Search.Trim()}%";
            users = users.Where(x =>
                EF.Functions.ILike(x.Email ?? string.Empty, search)
                || EF.Functions.ILike(x.DisplayName, search));
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
            "email" => descending ? users.OrderByDescending(x => x.Email) : users.OrderBy(x => x.Email),
            "displayname" => descending
                ? users.OrderByDescending(x => x.DisplayName)
                : users.OrderBy(x => x.DisplayName),
            _ => descending
                ? users.OrderByDescending(x => x.CreatedAtUtc)
                : users.OrderBy(x => x.CreatedAtUtc),
        };

        var items = await users
            .Skip(Pagination.Skip(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var roles = await RolesForAsync([.. items.Select(x => x.Id)], cancellationToken);

        var responses = items
            .Select(x => userProvisioning.ToResponse(x, roles.GetValueOrDefault(x.Id, TenantRoles.Member)))
            .ToList();

        return new PagedResult<UserResponse>(responses, page, pageSize, totalCount);
    }

    public async Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        return userProvisioning.ToResponse(user, await GetRoleAsync(user));
    }

    public async Task<UserResponse> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var requestedRole = TenantRoles.Normalize(request.Role);
        var actorRole = TenantRoles.Normalize(currentUser.Role);
        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedException();

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
            tenantContext.Slug ?? string.Empty,
            request.Email,
            request.DisplayName,
            request.Password,
            requestedRole,
            cancellationToken);

        await accountEmailService.SendConfirmationEmailAsync(user, cancellationToken);

        return userProvisioning.ToResponse(user, requestedRole);
    }

    public async Task<UserResponse> UpdateRoleAsync(
        Guid id,
        string role,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (target.Id == currentUser.UserId)
        {
            throw new ForbiddenException("You cannot change your own role.", "self_role_change");
        }

        var actorRole = TenantRoles.Normalize(currentUser.Role);
        var requestedRole = TenantRoles.Normalize(role);
        var targetRoles = await userManager.GetRolesAsync(target);
        var currentRole = targetRoles.FirstOrDefault() ?? TenantRoles.Member;

        if (actorRole == TenantRoles.Member)
        {
            throw new ForbiddenException("Members cannot change roles.", "role_not_allowed");
        }

        if ((currentRole == TenantRoles.Owner || requestedRole == TenantRoles.Owner)
            && actorRole != TenantRoles.Owner)
        {
            throw new ForbiddenException("Only an Owner can manage the Owner role.", "role_not_allowed");
        }

        if (currentRole == requestedRole)
        {
            return userProvisioning.ToResponse(target, requestedRole);
        }

        // Both role writes must succeed together; otherwise the user would be left
        // with no role at all. The tenant row lock serializes concurrent Owner
        // changes so the last-Owner guard cannot be defeated by a race.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockTenantAsync(cancellationToken);

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
        await userManager.UpdateSecurityStampAsync(target);
        await transaction.CommitAsync(cancellationToken);

        return userProvisioning.ToResponse(target, requestedRole);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var target = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        if (target.Id == currentUser.UserId)
        {
            throw new ForbiddenException("You cannot delete your own account.", "self_delete");
        }

        var actorRole = TenantRoles.Normalize(currentUser.Role);
        var targetRoles = await userManager.GetRolesAsync(target);
        var targetRole = targetRoles.FirstOrDefault() ?? TenantRoles.Member;

        if (actorRole == TenantRoles.Member)
        {
            throw new ForbiddenException("Members cannot delete users.", "role_not_allowed");
        }

        if (targetRole == TenantRoles.Owner && actorRole != TenantRoles.Owner)
        {
            throw new ForbiddenException("Only an Owner can delete an Owner.", "role_not_allowed");
        }

        // Same guard and the same serialization as UpdateRoleAsync: deleting and
        // demoting are two ways of removing the last Owner.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockTenantAsync(cancellationToken);

        if (targetRole == TenantRoles.Owner && await CountOwnersAsync(cancellationToken) <= 1)
        {
            throw new ConflictException("The last Owner cannot be deleted.", "last_owner");
        }

        EnsureSucceeded(await userManager.DeleteAsync(target), "user_delete_failed");
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> RolesForAsync(
        List<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var rows = await (
                from userRole in dbContext.UserRoles
                join role in dbContext.Roles on userRole.RoleId equals role.Id
                where userIds.Contains(userRole.UserId)
                select new { userRole.UserId, Role = role.Name ?? TenantRoles.Member })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.First().Role);
    }

    private async Task<string> GetRoleAsync(AppUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return roles.FirstOrDefault() ?? TenantRoles.Member;
    }

    /// <summary>
    /// Takes a row lock on the current tenant, held until the surrounding
    /// transaction ends. Two concurrent Owner demotions would otherwise both read
    /// "two Owners" and both commit, leaving the tenant without one.
    /// </summary>
    private async Task LockTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedException();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Tenants\" WHERE \"Id\" = {tenantId} FOR UPDATE",
            cancellationToken);
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
