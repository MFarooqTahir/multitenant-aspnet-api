using FluentValidation;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Domain.Entities;
using PasswordRules = Multitenant.Application.Common.Security.PasswordRules;

namespace Multitenant.Application.Validation.Tenants;

public sealed class OnboardTenantRequestValidator : AbstractValidator<OnboardTenantRequest>
{
    public OnboardTenantRequestValidator()
    {
        RuleFor(x => x.Slug)
            .NotEmpty()
            .Must(TenantSlug.IsValid)
            .WithMessage(TenantSlug.Description);

        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(2, 120);

        RuleFor(x => x.OwnerEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.OwnerDisplayName)
            .NotEmpty()
            .Length(2, 80);

        RuleFor(x => x.OwnerPassword)
            .NotEmpty()
            .Must(PasswordRules.IsValid)
            .WithMessage(PasswordRules.Description);
    }
}
