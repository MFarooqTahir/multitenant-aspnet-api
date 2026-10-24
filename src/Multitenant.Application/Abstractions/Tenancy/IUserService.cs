using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Users;

namespace Multitenant.Application.Abstractions.Tenancy;

/// <summary>
/// Tenant-scoped user administration (list, read, create, change role, delete).
/// </summary>
public interface IUserService
{
    Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken cancellationToken);

    Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<UserResponse> UpdateRoleAsync(Guid id, string role, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
