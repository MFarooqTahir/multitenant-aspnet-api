using FluentValidation;
using Multitenant.Application.Contracts.Tenants;

namespace Multitenant.Application.Validation.Tenants;

/// <summary>
/// The request has a single boolean, so there is nothing to reject. The validator
/// exists because startup fails closed when an endpoint accepts a complex parameter
/// without one, which keeps validation from silently disappearing.
/// </summary>
public sealed class UpdateTenantFeatureRequestValidator : AbstractValidator<UpdateTenantFeatureRequest>
{
}
