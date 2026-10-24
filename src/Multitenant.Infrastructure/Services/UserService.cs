using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Common;
using Multitenant.Application.Contracts;
using Multitenant.Application.Errors;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Services;

public sealed class UserService(
    AppDbContext dbContext,
    UserManager<AppUser> userManager,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAccountEmailService accountEmailService,
    TimeProvider timeProvider) : IUserService
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "email",
        "displayname",
        "createdat",
    };

    public async Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken cancellationToken)
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

            users = users.Where(x => dbContext.UserRoles.Any(ur => ur.UserId == x.Id && ur.RoleId == roleId));
        }

        var totalCount = await users.CountAsync(cancellationToken);
        var (field, descending) = Sorting.Parse(query.Sort, SortFields, "createdat");

        users = field switch
        {
            "email" => descending ? users.OrderByDescending(x => x.Email) : users.OrderBy(x => x.Email),
            "displayname" => descending ? users.OrderByDescending(x => x.DisplayName) : users.OrderBy(x => x.DisplayName),
            _ => descending ? users.OrderByDescending(x => x.CreatedAtUtc) : users.OrderBy(x => x.CreatedAtUtc),
        };

        var items = await users
            .Skip(Pagination.Skip(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var roles = await RolesForAsync(items.Select(x => x.Id).ToList(), cancellationToken);

        var responses = items
            .Select(x => ToResponse(x, roles.GetValueOrDefault(x.Id, TenantRoles.Member)))
            .ToList();

        return new PagedResult<UserResponse>(responses, page, pageSize, totalCount);
    }

    public async Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("User not found.");

        var role = await GetRoleAsync(user);
        return ToResponse(user, role);
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var requestedRole = TenantRoles.Normalize(request.Role);
        var actorRole = TenantRoles.Normalize(currentUser.Role);
        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedException();

        if (requestedRole == TenantRoles.Owner && actorRole != TenantRoles.Owner)
        {
            throw new ForbiddenException("Only an Owner can create another Owner.", "role_not_allowed");
        }

        if (actorRole == TenantRoles.Member)
        {
            throw new ForbiddenException("Members cannot create users.", "role_not_allowed");
        }

        var email = request.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();

        if (await dbContext.Users.AnyAsync(
                x => x.TenantId == tenantId && x.NormalizedEmail == normalizedEmail,
                cancellationToken))
        {
            throw new ConflictException("A user with that email already exists in this tenant.", "email_taken");
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserName = UserNames.For(tenantContext.Slug ?? string.Empty, email),
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            EmailConfirmed = false,
            IsActive = true,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        };

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            throw new ConflictException(
                string.Join(" ", created.Errors.Select(x => x.Description)),
                "user_creation_failed");
        }

        var roleResult = await userManager.AddToRoleAsync(user, requestedRole);
        if (!roleResult.Succeeded)
        {
            throw new ConflictException(
                string.Join(" ", roleResult.Errors.Select(x => x.Description)),
                "role_assignment_failed");
        }

        await accountEmailService.SendConfirmationEmailAsync(user, cancellationToken);
        return ToResponse(user, requestedRole);
    }

    public async Task<UserResponse> UpdateRoleAsync(Guid id, string role, CancellationToken cancellationToken)
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
            return ToResponse(target, requestedRole);
        }

        if (currentRole == TenantRoles.Owner)
        {
            var owners = await CountOwnersAsync(cancellationToken);
            if (owners <= 1)
            {
                throw new ConflictException("The last Owner cannot be demoted.", "last_owner");
            }
        }

        if (targetRoles.Count > 0)
        {
            var removed = await userManager.RemoveFromRolesAsync(target, targetRoles);
            if (!removed.Succeeded)
            {
                throw new ConflictException(
                    string.Join(" ", removed.Errors.Select(x => x.Description)),
                    "role_update_failed");
            }
        }

        var added = await userManager.AddToRoleAsync(target, requestedRole);
        if (!added.Succeeded)
        {
            throw new ConflictException(
                string.Join(" ", added.Errors.Select(x => x.Description)),
                "role_update_failed");
        }

        // Invalidate every JWT issued with the previous role. Refresh tokens remain valid,
        // so the next /auth/refresh call issues a token with the new role.
        await userManager.UpdateSecurityStampAsync(target);

        return ToResponse(target, requestedRole);
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

        if (targetRole == TenantRoles.Owner)
        {
            var owners = await CountOwnersAsync(cancellationToken);
            if (owners <= 1)
            {
                throw new ConflictException("The last Owner cannot be deleted.", "last_owner");
            }
        }

        var deleted = await userManager.DeleteAsync(target);
        if (!deleted.Succeeded)
        {
            throw new ConflictException(
                string.Join(" ", deleted.Errors.Select(x => x.Description)),
                "user_delete_failed");
        }
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

    private Task<int> CountOwnersAsync(CancellationToken cancellationToken)
    {
        var ownerRole = TenantRoles.Owner.ToUpperInvariant();
        return (
                from userRole in dbContext.UserRoles
                join role in dbContext.Roles on userRole.RoleId equals role.Id
                where role.NormalizedName == ownerRole
                select userRole.UserId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    private static UserResponse ToResponse(AppUser user, string role) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            role,
            user.IsActive,
            user.CreatedAtUtc);
}
