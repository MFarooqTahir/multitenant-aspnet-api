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
            .Must(name => name?.Trim().Length is >= 2 and <= 120)
            .WithMessage("Name must be between 2 and 120 characters after trimming.");

        RuleFor(x => x.OwnerEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.OwnerDisplayName)
            .Must(name => name?.Trim().Length is >= 2 and <= 80)
            .WithMessage("Owner display name must be between 2 and 80 characters after trimming.");

        RuleFor(x => x.OwnerPassword)
            .NotEmpty()
            .MaximumLength(PasswordRules.MaximumLength)
            .Must(PasswordRules.IsValid)
            .WithMessage(PasswordRules.Description);
    }
}
