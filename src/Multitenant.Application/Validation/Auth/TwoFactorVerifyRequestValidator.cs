using FluentValidation;
using Multitenant.Application.Contracts.Auth;

namespace Multitenant.Application.Validation.Auth;

public sealed class TwoFactorVerifyRequestValidator : AbstractValidator<TwoFactorVerifyRequest>
{
    public TwoFactorVerifyRequestValidator()
    {
        RuleFor(x => x.TwoFactorToken).NotEmpty().MaximumLength(4096);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Code) || !string.IsNullOrWhiteSpace(x.RecoveryCode))
            .WithMessage("An authenticator code or a recovery code is required.");
    }
}
