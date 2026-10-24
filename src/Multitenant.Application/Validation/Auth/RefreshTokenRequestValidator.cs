using FluentValidation;
using Multitenant.Application.Contracts;

namespace Multitenant.Application.Validation;

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
    }
}
