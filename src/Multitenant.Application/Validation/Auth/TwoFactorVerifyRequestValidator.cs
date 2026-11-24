using FluentValidation;
using Multitenant.Application.Contracts.Auth;

namespace Multitenant.Application.Validation.Auth;

public sealed class TwoFactorVerifyRequestValidator : AbstractValidator<TwoFactorVerifyRequest>
{
    public TwoFactorVerifyRequestValidator()
    {
        RuleFor(x => x.TwoFactorToken).NotEmpty().MaximumLength(4096);
        RuleFor(x => x.Code)
            .Matches(@"^[0-9]{6}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));
        RuleFor(x => x.RecoveryCode).MaximumLength(128);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Code) ^ !string.IsNullOrWhiteSpace(x.RecoveryCode))
            .WithMessage("Provide exactly one of an authenticator code or a recovery code.");
    }
}
