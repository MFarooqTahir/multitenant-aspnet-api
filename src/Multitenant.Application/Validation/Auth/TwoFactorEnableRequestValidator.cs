using FluentValidation;
using Multitenant.Application.Contracts.Auth;

namespace Multitenant.Application.Validation.Auth;

public sealed class TwoFactorEnableRequestValidator : AbstractValidator<TwoFactorEnableRequest>
{
    public TwoFactorEnableRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches(@"^[0-9]{6}$");
    }
}
