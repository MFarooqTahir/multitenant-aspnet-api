namespace Multitenant.Application.Abstractions;

/// <summary>
/// The authenticated caller, read from the validated JWT.
/// </summary>
/// <remarks>
/// The tenant is deliberately not exposed here: <see cref="ITenantContext"/> is the
/// single source of truth for the current tenant, so services cannot accidentally
/// read a tenant from a different place.
/// </remarks>
public interface ICurrentUser
{
    Guid UserId { get; }

    string Role { get; }
}
