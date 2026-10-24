using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Multitenant.Api.Filters;

/// <summary>
/// Enforces that every complex action parameter has a registered FluentValidation
/// validator. Without this, <see cref="ValidationFilter"/> silently skips unknown
/// argument types (fail-open) and an endpoint can ship with unvalidated input.
/// </summary>
public static class ValidationFilterAssertion
{
    public static void AssertAllActionParametersHaveValidators(IServiceProvider services)
    {
        var isService = services.GetService<IServiceProviderIsService>();
        var descriptors = services
            .GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors
            .Items
            .OfType<ControllerActionDescriptor>();

        var missing = new List<string>();

        foreach (var descriptor in descriptors)
        {
            foreach (var parameter in descriptor.Parameters)
            {
                if (!RequiresValidation(parameter.ParameterType))
                {
                    continue;
                }

                var validatorType = typeof(IValidator<>).MakeGenericType(parameter.ParameterType);
                if (isService?.IsService(validatorType) != true)
                {
                    missing.Add(
                        $"{descriptor.ControllerName}.{descriptor.ActionName} parameter '{parameter.Name}' ({parameter.ParameterType.Name})");
                }
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "The following action parameters have no registered FluentValidation validator. "
                + "Register an IValidator<T> so the ValidationFilter cannot fail open:\n - "
                + string.Join("\n - ", missing));
        }
    }

    private static bool RequiresValidation(Type type)
    {
        if (type == typeof(CancellationToken)
            || type == typeof(string)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(TimeSpan))
        {
            return false;
        }

        if (type.IsPrimitive || type.IsEnum || Nullable.GetUnderlyingType(type) is not null)
        {
            return false;
        }

        if (typeof(IFormFile).IsAssignableFrom(type)
            || typeof(IFormFileCollection).IsAssignableFrom(type)
            || typeof(HttpContext).IsAssignableFrom(type))
        {
            return false;
        }

        // Only classes/records that can carry a request body or query DTO.
        return type.IsClass && type != typeof(object) && !type.IsArray;
    }
}
