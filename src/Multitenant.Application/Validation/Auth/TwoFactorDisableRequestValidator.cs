using FluentValidation;
using Multitenant.Application.Contracts.Auth;

namespace Multitenant.Application.Validation.Auth;

public sealed class TwoFactorDisableRequestValidator : AbstractValidator<TwoFactorDisableRequest>
{
    public TwoFactorDisableRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches(@"^[0-9]{6}$");
    }
}
