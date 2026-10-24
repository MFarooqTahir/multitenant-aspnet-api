using FluentValidation;
using Multitenant.Application.Contracts;

namespace Multitenant.Application.Validation;

public sealed class TwoFactorEnableRequestValidator : AbstractValidator<TwoFactorEnableRequest>
{
    public TwoFactorEnableRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
    }
}
