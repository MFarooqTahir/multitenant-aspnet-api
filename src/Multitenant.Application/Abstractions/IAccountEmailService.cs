using Multitenant.Domain.Entities;

namespace Multitenant.Application.Abstractions;

public interface IAccountEmailService
{
    Task SendConfirmationEmailAsync(AppUser user, CancellationToken cancellationToken);
}
