namespace Multitenant.Application.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }

    Guid TenantId { get; }

    string Role { get; }
}
