using FluentValidation;
using Multitenant.Application.Contracts.Tenants;

namespace Multitenant.Application.Validation.Tenants;

public sealed class UpdateTenantRequestValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => name?.Trim().Length is >= 2 and <= 120)
            .WithMessage("Name must be between 2 and 120 characters after trimming.");
    }
}
