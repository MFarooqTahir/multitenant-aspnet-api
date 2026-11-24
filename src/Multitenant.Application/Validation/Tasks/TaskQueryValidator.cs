using FluentValidation;
using Multitenant.Application.Contracts.Tasks;

namespace Multitenant.Application.Validation.Tasks;

public sealed class TaskQueryValidator : AbstractValidator<TaskQuery>
{
    public TaskQueryValidator()
    {
        // Page and PageSize are intentionally not range-validated here: the service
        // clamps oversized pages rather than rejecting them.
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Status)
            .Must(status => status is null || Enum.IsDefined(status.Value))
            .WithMessage("Status is not a valid task status.");
        RuleFor(x => x.Priority)
            .Must(priority => priority is null || Enum.IsDefined(priority.Value))
            .WithMessage("Priority is not a valid task priority.");
        RuleFor(x => x)
            .Must(query => query.DueBefore is null || query.DueAfter is null || query.DueBefore >= query.DueAfter)
            .WithMessage("DueBefore cannot be earlier than DueAfter.");
    }
}
