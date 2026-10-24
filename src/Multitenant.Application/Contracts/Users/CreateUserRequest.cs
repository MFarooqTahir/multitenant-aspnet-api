namespace Multitenant.Application.Contracts.Users;

public sealed record CreateUserRequest(string Email, string DisplayName, string Password, string Role);
