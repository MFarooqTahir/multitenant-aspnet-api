using Multitenant.Application.Contracts;
using Multitenant.Domain.Entities;

namespace Multitenant.Application.Abstractions;

public interface IAuthService
{
    Task<LoginResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task<AuthTokens> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);

    Task LogoutAllAsync(CancellationToken cancellationToken);

    Task ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken);

    Task ResendConfirmationAsync(string tenantSlug, string email, CancellationToken cancellationToken);

    Task<TwoFactorSetupResponse> SetupTwoFactorAsync(CancellationToken cancellationToken);

    Task<TwoFactorEnabledResponse> EnableTwoFactorAsync(string code, CancellationToken cancellationToken);

    Task DisableTwoFactorAsync(string code, CancellationToken cancellationToken);

    Task<AuthTokens> VerifyTwoFactorAsync(TwoFactorVerifyRequest request, string? ipAddress, CancellationToken cancellationToken);
}

public interface ITenantService
{
    Task<TenantResponse> OnboardAsync(OnboardTenantRequest request, CancellationToken cancellationToken);

    Task<TenantResponse> GetCurrentAsync(CancellationToken cancellationToken);
}

public interface IUserService
{
    Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken cancellationToken);

    Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<UserResponse> UpdateRoleAsync(Guid id, string role, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IProjectService
{
    Task<PagedResult<ProjectResponse>> ListAsync(ProjectQuery query, CancellationToken cancellationToken);

    Task<ProjectResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken);

    Task<ProjectResponse> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface ITaskService
{
    Task<PagedResult<TaskResponse>> ListAsync(TaskQuery query, CancellationToken cancellationToken);

    Task<TaskResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<TaskResponse> CreateAsync(Guid projectId, CreateTaskRequest request, CancellationToken cancellationToken);

    Task<TaskResponse> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface ITenantStore
{
    Task<Tenant?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Tenant?> FindBySlugAsync(string slug, CancellationToken cancellationToken);
}
