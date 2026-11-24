using FluentValidation;
using Multitenant.Application.Contracts.Projects;

namespace Multitenant.Application.Validation.Projects;

public sealed class ProjectQueryValidator : AbstractValidator<ProjectQuery>
{
    public ProjectQueryValidator()
    {
        // Page and PageSize are intentionally not range-validated here: the service
        // clamps oversized pages rather than rejecting them.
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Status)
            .Must(status => status is null || Enum.IsDefined(status.Value))
            .WithMessage("Status is not a valid project status.");
    }
}
