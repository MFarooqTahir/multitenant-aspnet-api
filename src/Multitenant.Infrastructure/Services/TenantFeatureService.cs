using Microsoft.EntityFrameworkCore;
using Multitenant.Application.Abstractions.Identity;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Errors.Exceptions;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Services;

/// <summary>
/// Tenant feature switches. Reads rely on the tenant query filter, so a tenant can
/// never observe another tenant's overrides. Writes take the tenant row lock and
/// re-check the Owner role in the service as well as at the controller, matching
/// the pattern used for user administration.
/// </summary>
public sealed class TenantFeatureService(
    AppDbContext dbContext,
    TenantRowLock tenantRowLock,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : ITenantFeatureService
{
    public async Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken)
    {
        _ = tenantContext.TenantId ?? throw new UnauthorizedException();
        var normalizedKey = NormalizeAndValidate(key);

        var stored = await dbContext.TenantFeatures
            .AsNoTracking()
            .Where(x => x.Key == normalizedKey)
            .Select(x => (bool?)x.IsEnabled)
            .FirstOrDefaultAsync(cancellationToken);

        return stored ?? true;
    }

    public async Task<IReadOnlyList<TenantFeatureResponse>> ListAsync(CancellationToken cancellationToken)
    {
        _ = tenantContext.TenantId ?? throw new UnauthorizedException();

        var stored = await dbContext.TenantFeatures
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.IsEnabled, StringComparer.Ordinal, cancellationToken);

        return TenantFeatureKeys.All
            .Select(key => new TenantFeatureResponse(key, stored.GetValueOrDefault(key, true)))
            .ToList();
    }

    public async Task<TenantFeatureResponse> SetAsync(
        string key,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedException();
        var normalizedKey = NormalizeAndValidate(key);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Serialize updates for this tenant, including the first insert, and
        // re-read the actor role under the lock instead of trusting a stale claim.
        await tenantRowLock.AcquireAsync(cancellationToken);

        var actorRole = await (
                from userRole in dbContext.UserRoles
                join role in dbContext.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == currentUser.UserId
                select role.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.Equals(actorRole, TenantRoles.Owner, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only an Owner can change tenant features.", "role_not_allowed");
        }

        var feature = await dbContext.TenantFeatures
            .FirstOrDefaultAsync(x => x.Key == normalizedKey, cancellationToken);

        if (feature is null)
        {
            feature = TenantFeature.Create(tenantId, normalizedKey, isEnabled, timeProvider.GetUtcNow());
            dbContext.TenantFeatures.Add(feature);
        }
        else
        {
            feature.Set(isEnabled, timeProvider.GetUtcNow());
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new TenantFeatureResponse(feature.Key, feature.IsEnabled);
    }

    private static string NormalizeAndValidate(string key)
    {
        var normalizedKey = TenantFeatureKeys.Normalize(key);

        if (!TenantFeatureKeys.IsValid(normalizedKey))
        {
            throw new NotFoundException(
                $"Unknown feature. Valid keys: {string.Join(", ", TenantFeatureKeys.All)}.",
                "unknown_feature");
        }

        return normalizedKey;
    }
}
