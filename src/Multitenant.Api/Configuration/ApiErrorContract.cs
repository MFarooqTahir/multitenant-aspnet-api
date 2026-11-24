using Microsoft.AspNetCore.Mvc;
using Multitenant.Api.ErrorHandling;
using Multitenant.Api.Middleware;
using Multitenant.Application.Common;

namespace Multitenant.Api.Configuration;

/// <summary>
/// Registers the single RFC 7807 error contract used by the whole API: handled
/// exceptions, framework-generated status codes, FluentValidation failures and
/// model-binding failures all return the same <c>problem+json</c> shape with a
/// stable <c>code</c>, <c>traceId</c> and <c>correlationId</c>.
/// </summary>
public static class ApiErrorContract
{
    public static IServiceCollection AddApiErrorContract(this IServiceCollection services)
    {
        // Model-binding failures (malformed JSON, unparsable query values) happen
        // before ValidationFilter runs. Without this they would return a problem
        // body with no machine-readable code.
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
                new BadRequestObjectResult(
                    ApiProblemDetails.FromModelState(context.HttpContext, context.ModelState));
        });

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var httpContext = context.HttpContext;
                context.ProblemDetails.Instance ??= httpContext.Request.Path;
                context.ProblemDetails.Extensions.TryAdd("traceId", httpContext.TraceIdentifier);
                context.ProblemDetails.Extensions.TryAdd(
                    "correlationId",
                    httpContext.Items[CorrelationKeys.ItemKey]);

                var statusCode = context.ProblemDetails.Status ?? httpContext.Response.StatusCode;
                var code = ApiProblemDetails.CodeForStatus(statusCode);
                if (code is not null)
                {
                    context.ProblemDetails.Type = ApiProblemDetails.TypeFor(statusCode);
                    context.ProblemDetails.Extensions.TryAdd("code", code);
                }
            };
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }
}
