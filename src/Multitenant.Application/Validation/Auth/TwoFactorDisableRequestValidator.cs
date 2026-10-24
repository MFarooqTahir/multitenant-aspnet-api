using FluentValidation;
using Multitenant.Application.Contracts;

namespace Multitenant.Application.Validation;

public sealed class TwoFactorDisableRequestValidator : AbstractValidator<TwoFactorDisableRequest>
{
    public TwoFactorDisableRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
    }
}
