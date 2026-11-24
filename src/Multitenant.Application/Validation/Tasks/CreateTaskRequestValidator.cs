using FluentValidation;
using Multitenant.Application.Contracts.Tasks;

namespace Multitenant.Application.Validation.Tasks;

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .Must(title => title?.Trim().Length is >= 2 and <= 200)
            .WithMessage("Title must be between 2 and 200 characters after trimming.");
        RuleFor(x => x.Description)
            .Must(description => description is null || description.Trim().Length <= 4000)
            .WithMessage("Description cannot exceed 4000 characters after trimming.");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.AssigneeUserId).NotEqual(Guid.Empty);
    }
}
