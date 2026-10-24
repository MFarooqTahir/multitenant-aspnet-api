using FluentValidation;
using Multitenant.Application.Contracts;

namespace Multitenant.Application.Validation;

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
    }
}
