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
    }
}
