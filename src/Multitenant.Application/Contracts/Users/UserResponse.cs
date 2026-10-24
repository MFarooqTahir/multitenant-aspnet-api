namespace Multitenant.Application.Contracts;

public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);
