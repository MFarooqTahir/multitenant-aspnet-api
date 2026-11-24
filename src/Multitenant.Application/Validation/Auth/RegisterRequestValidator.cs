using FluentValidation;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Domain.Entities;
using PasswordRules = Multitenant.Application.Common.Security.PasswordRules;

namespace Multitenant.Application.Validation.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.TenantSlug)
            .NotEmpty()
            .Must(TenantSlug.IsValid)
            .WithMessage(TenantSlug.Description);

        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DisplayName)
            .Must(name => name?.Trim().Length is >= 2 and <= 80)
            .WithMessage("Display name must be between 2 and 80 characters after trimming.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MaximumLength(PasswordRules.MaximumLength)
            .Must(PasswordRules.IsValid)
            .WithMessage(PasswordRules.Description);
    }
}
