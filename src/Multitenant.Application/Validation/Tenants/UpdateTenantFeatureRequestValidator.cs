using FluentValidation;
using Multitenant.Application.Contracts.Tenants;

namespace Multitenant.Application.Validation.Tenants;

/// <summary>
/// The boolean is nullable so a missing JSON member is rejected instead of
/// silently disabling the feature.
/// </summary>
public sealed class UpdateTenantFeatureRequestValidator : AbstractValidator<UpdateTenantFeatureRequest>
{
    public UpdateTenantFeatureRequestValidator()
    {
        RuleFor(x => x.IsEnabled).NotNull();
    }
}
