using FluentValidation;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Domain.Entities;
using PasswordRules = Multitenant.Application.Common.Security.PasswordRules;

namespace Multitenant.Application.Validation.Auth;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.TenantSlug).NotEmpty().MaximumLength(TenantSlug.MaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordRules.MaximumLength);
    }
}
