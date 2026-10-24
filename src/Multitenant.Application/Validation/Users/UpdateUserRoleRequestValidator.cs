using FluentValidation;
using Multitenant.Application.Contracts;
using Multitenant.Domain.Entities;

namespace Multitenant.Application.Validation;

public sealed class UpdateUserRoleRequestValidator : AbstractValidator<UpdateUserRoleRequest>
{
    public UpdateUserRoleRequestValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(TenantRoles.IsValid)
            .WithMessage($"Role must be one of: {string.Join(", ", TenantRoles.All)}.");
    }
}
