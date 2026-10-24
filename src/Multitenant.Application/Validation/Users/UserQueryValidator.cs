using FluentValidation;
using Multitenant.Application.Contracts;
using Multitenant.Domain.Entities;

namespace Multitenant.Application.Validation;

public sealed class UserQueryValidator : AbstractValidator<UserQuery>
{
    public UserQueryValidator()
    {
        RuleFor(x => x.Role)
            .Must(role => string.IsNullOrWhiteSpace(role) || TenantRoles.IsValid(role))
            .WithMessage($"Role must be one of: {string.Join(", ", TenantRoles.All)}.");
    }
}
