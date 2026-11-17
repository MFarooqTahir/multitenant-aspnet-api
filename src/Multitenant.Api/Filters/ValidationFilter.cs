using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Multitenant.Api.ErrorHandling;

namespace Multitenant.Api.Filters;

/// <summary>
/// Runs the FluentValidation validator registered for each action argument and
/// short-circuits with an RFC 7807 validation problem.
/// </summary>
/// <remarks>
/// A missing validator is a fail-open: the action runs with unvalidated input.
/// <see cref="ValidationFilterAssertion"/> enforces at startup that every action
/// parameter type has a corresponding validator registered.
/// </remarks>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            foreach (var failure in result.Errors)
            {
                var key = string.IsNullOrWhiteSpace(failure.PropertyName)
                    ? "request"
                    : ApiProblemDetails.ToCamelCasePath(failure.PropertyName);

                errors[key] = errors.TryGetValue(key, out var existing)
                    ? [.. existing, failure.ErrorMessage]
                    : [failure.ErrorMessage];
            }
        }

        if (errors.Count > 0)
        {
            context.Result = new BadRequestObjectResult(
                ApiProblemDetails.CreateValidation(context.HttpContext, errors));
            return;
        }

        await next();
    }
}
