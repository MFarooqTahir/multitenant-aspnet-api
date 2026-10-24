using FluentValidation;
using Multitenant.Application.Contracts.Auth;

namespace Multitenant.Application.Validation.Auth;

public sealed class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty();
    }
}
