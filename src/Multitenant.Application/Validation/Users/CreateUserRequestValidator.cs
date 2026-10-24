using FluentValidation;
using Multitenant.Application.Common;
using Multitenant.Application.Contracts;
using Multitenant.Domain.Entities;

namespace Multitenant.Application.Validation;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().Length(2, 80);

        RuleFor(x => x.Password)
            .NotEmpty()
            .Must(PasswordRules.IsValid)
            .WithMessage(PasswordRules.Description);

        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(TenantRoles.IsValid)
            .WithMessage($"Role must be one of: {string.Join(", ", TenantRoles.All)}.");
    }
}
