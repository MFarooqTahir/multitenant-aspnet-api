using FluentValidation;
using Multitenant.Application.Contracts.Projects;

namespace Multitenant.Application.Validation.Projects;

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => name?.Trim().Length is >= 2 and <= 120)
            .WithMessage("Name must be between 2 and 120 characters after trimming.");
        RuleFor(x => x.Description)
            .Must(description => description is null || description.Trim().Length <= 2000)
            .WithMessage("Description cannot exceed 2000 characters after trimming.");
        RuleFor(x => x.Status).IsInEnum();
    }
}
