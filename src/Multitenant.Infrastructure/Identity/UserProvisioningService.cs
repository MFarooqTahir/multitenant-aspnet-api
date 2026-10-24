using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common;
using Multitenant.Application.Contracts.Users;
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
public sealed partial class UserProvisioningService(
    AppDbContext dbContext,
    UserManager<AppUser> userManager,
    TimeProvider timeProvider,
    ILogger<UserProvisioningService> logger) : IUserProvisioningService
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

        // Wrap creation and role assignment in a transaction so that if role assignment
        // fails, the user creation is rolled back rather than requiring a separate
        // delete that could itself fail and leave an orphaned, roleless account.
        // When a caller (e.g. tenant onboarding) already owns a transaction, join it
        // instead of starting a nested one.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            EnsureSucceeded(await userManager.CreateAsync(user, password), "user_creation_failed");

            var roleAssigned = await userManager.AddToRoleAsync(user, role);
            if (!roleAssigned.Succeeded)
            {
                throw new ConflictException(
                    string.Join(" ", roleAssigned.Errors.Select(x => x.Description)),
                    "role_assignment_failed");
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                LogProvisioningRolledBack(logger, user.Id);
            }

            throw;
        }

        return user;
    }
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Implements IUserProvisioningService")]
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

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Rolled back provisioning for user {UserId} because role assignment or persistence failed.")]
    private static partial void LogProvisioningRolledBack(ILogger logger, Guid userId);
}
