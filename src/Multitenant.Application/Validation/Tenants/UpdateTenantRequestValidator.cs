using FluentValidation;
using Multitenant.Application.Contracts.Tenants;

namespace Multitenant.Application.Validation.Tenants;

public sealed class UpdateTenantRequestValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(120);
    }
}
