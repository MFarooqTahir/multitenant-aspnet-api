using FluentValidation;
using Multitenant.Application.Contracts.Tasks;

namespace Multitenant.Application.Validation.Tasks;

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().Length(2, 200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.AssigneeUserId).NotEqual(Guid.Empty);
    }
}
