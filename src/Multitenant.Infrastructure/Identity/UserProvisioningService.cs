using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common;
using Multitenant.Application.Contracts;
using Multitenant.Application.Contracts.Users;
using Multitenant.Application.Errors;
using Multitenant.Application.Errors.Exceptions;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Identity;

/// <summary>
/// The single place a user is created: uniqueness check, Identity creation, role
/// assignment and response mapping. Callers only decide which role to grant and
/// whether the caller is allowed to do so. Email delivery is the caller's job,
/// because onboarding must send it only after its transaction commits.
/// </summary>
public sealed class UserProvisioningService(
    AppDbContext dbContext,
    UserManager<AppUser> userManager,
    TimeProvider timeProvider) : IUserProvisioningService
{
    public async Task<AppUser> CreateAsync(
        Guid tenantId,
        string tenantSlug,
        string email,
        string displayName,
        string password,
        string role,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();

        var exists = await dbContext.Users
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.TenantId == tenantId && x.NormalizedEmail == normalizedEmail,
                cancellationToken);

        if (exists)
        {
            throw new ConflictException(
                "A user with that email already exists in this tenant.",
                "email_taken");
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserName = UserNames.For(tenantSlug, email),
            Email = email.Trim(),
            DisplayName = displayName.Trim(),
            EmailConfirmed = false,
            IsActive = true,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        };

        EnsureSucceeded(await userManager.CreateAsync(user, password), "user_creation_failed");
        EnsureSucceeded(await userManager.AddToRoleAsync(user, role), "role_assignment_failed");

        return user;
    }

    public UserResponse ToResponse(AppUser user, string role) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            role,
            user.IsActive,
            user.CreatedAtUtc);

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
