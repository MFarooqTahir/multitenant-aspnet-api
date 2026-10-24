using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts;

public abstract class PageQuery
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public string? Sort { get; set; }
}

public sealed class UserQuery : PageQuery
{
    public string? Search { get; set; }

    public string? Role { get; set; }

    public bool? IsActive { get; set; }
}

public sealed class ProjectQuery : PageQuery
{
    public string? Search { get; set; }

    public ProjectStatus? Status { get; set; }
}

public sealed class TaskQuery : PageQuery
{
    public string? Search { get; set; }

    public WorkItemStatus? Status { get; set; }

    public WorkItemPriority? Priority { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? AssigneeUserId { get; set; }

    public DateOnly? DueBefore { get; set; }

    public DateOnly? DueAfter { get; set; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record OnboardTenantRequest(
    string Slug,
    string Name,
    string OwnerEmail,
    string OwnerDisplayName,
    string OwnerPassword);

public sealed record TenantResponse(
    Guid Id,
    string Slug,
    string Name,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

public sealed record LoginRequest(string TenantSlug, string Email, string Password);

public sealed record RegisterRequest(string TenantSlug, string Email, string DisplayName, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ResendConfirmationRequest(string TenantSlug, string Email);

public sealed record TwoFactorVerifyRequest(string TwoFactorToken, string? Code, string? RecoveryCode);

public sealed record TwoFactorEnableRequest(string Code);

public sealed record TwoFactorDisableRequest(string Code);

public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);

public sealed record TwoFactorEnabledResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record AuthTokens(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType = "Bearer");

public abstract record LoginResult;

public sealed record LoginSuccess(AuthTokens Tokens) : LoginResult;

public sealed record LoginTwoFactorRequired(string TwoFactorToken, int ExpiresIn) : LoginResult;

public sealed record CreateUserRequest(string Email, string DisplayName, string Password, string Role);

public sealed record UpdateUserRoleRequest(string Role);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateProjectRequest(string Name, string? Description, ProjectStatus Status = ProjectStatus.Active);

public sealed record UpdateProjectRequest(string Name, string? Description, ProjectStatus Status);

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string? Description,
    ProjectStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    WorkItemStatus Status = WorkItemStatus.Todo,
    WorkItemPriority Priority = WorkItemPriority.Medium,
    Guid? AssigneeUserId = null,
    DateOnly? DueDate = null);

public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    WorkItemStatus Status,
    WorkItemPriority Priority,
    Guid? AssigneeUserId,
    DateOnly? DueDate);

public sealed record TaskResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    WorkItemStatus Status,
    WorkItemPriority Priority,
    Guid? AssigneeUserId,
    DateOnly? DueDate,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
