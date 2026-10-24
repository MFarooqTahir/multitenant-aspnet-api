using FluentValidation;
using Multitenant.Application.Contracts.Auth;

namespace Multitenant.Application.Validation.Auth;

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
    }
}
