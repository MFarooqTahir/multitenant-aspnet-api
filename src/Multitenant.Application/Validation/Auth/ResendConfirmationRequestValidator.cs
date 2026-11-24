using FluentValidation;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Domain.Entities;

namespace Multitenant.Application.Validation.Auth;

public sealed class ResendConfirmationRequestValidator : AbstractValidator<ResendConfirmationRequest>
{
    public ResendConfirmationRequestValidator()
    {
        RuleFor(x => x.TenantSlug).NotEmpty().MaximumLength(TenantSlug.MaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}
