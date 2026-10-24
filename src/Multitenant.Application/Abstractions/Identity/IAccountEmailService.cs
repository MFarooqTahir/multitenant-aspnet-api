using Multitenant.Application.Abstractions.Messaging;
using Multitenant.Domain.Entities;

namespace Multitenant.Application.Abstractions.Identity;

/// <summary>
/// Builds account emails (currently the confirmation link). Kept separate from
/// <see cref="IEmailSender"/> so token generation stays in one place and the
/// transport can be swapped independently.
/// </summary>
public interface IAccountEmailService
{
    Task SendConfirmationEmailAsync(AppUser user, CancellationToken cancellationToken);
}
