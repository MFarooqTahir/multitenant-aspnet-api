using Microsoft.EntityFrameworkCore;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Errors.Exceptions;

namespace Multitenant.Infrastructure.Persistence;

/// <summary>
/// Serializes privileged tenant mutations on the tenant row. Callers must begin a
/// transaction before acquiring the lock and authorize against live data afterwards.
/// </summary>
public sealed class TenantRowLock(AppDbContext dbContext, ITenantContext tenantContext)
{
    public Task AcquireAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "TenantRowLock must be acquired inside an explicit database transaction.");
        }

        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedException();

        return dbContext.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM \"Tenants\" WHERE \"Id\" = {tenantId} FOR UPDATE",
            cancellationToken);
    }
}
