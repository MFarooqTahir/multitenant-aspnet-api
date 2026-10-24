using Multitenant.Application.Contracts;
using Multitenant.Domain.Entities;

namespace Multitenant.Application.Abstractions;

/// <summary>
/// Creates the Identity user and assigns its first role. Both the administrative
/// endpoint and self-service registration call this, so the mechanics of user
/// creation exist in exactly one place.
/// </summary>
public interface IUserProvisioningService
{
    Task<AppUser> CreateAsync(
        Guid tenantId,
        string tenantSlug,
        string email,
        string displayName,
        string password,
        string role,
        CancellationToken cancellationToken);

    UserResponse ToResponse(AppUser user, string role);
}
