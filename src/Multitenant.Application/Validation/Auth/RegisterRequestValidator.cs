using FluentValidation;
using Multitenant.Application.Common;
using Multitenant.Application.Contracts;
using Multitenant.Domain.Entities;

namespace Multitenant.Application.Validation;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.TenantSlug)
            .NotEmpty()
            .Must(TenantSlug.IsValid)
            .WithMessage(TenantSlug.Description);

        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().Length(2, 80);

        RuleFor(x => x.Password)
            .NotEmpty()
            .Must(PasswordRules.IsValid)
            .WithMessage(PasswordRules.Description);
    }
}
