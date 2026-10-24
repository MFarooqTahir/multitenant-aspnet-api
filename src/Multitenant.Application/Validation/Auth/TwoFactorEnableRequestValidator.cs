using FluentValidation;
using Multitenant.Application.Contracts.Auth;

namespace Multitenant.Application.Validation.Auth;

public sealed class TwoFactorEnableRequestValidator : AbstractValidator<TwoFactorEnableRequest>
{
    public TwoFactorEnableRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
    }
}
