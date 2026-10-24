using System.Globalization;
using Asp.Versioning;
using FluentValidation;
using Microsoft.ApplicationInsights.Extensibility;
using Serilog.Events;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Multitenant.Api.Authorization;
using Multitenant.Api.Configuration;
using Multitenant.Api.Endpoints;
using Multitenant.Api.Filters;
using Multitenant.Api.Middleware;
using Multitenant.Api.OpenApi;
using Multitenant.Api.RateLimiting;
using Multitenant.Application.Common;
using Multitenant.Application.Configuration.Web;
using Multitenant.Application.Validation.Tenants;
using Multitenant.Infrastructure;
using Multitenant.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Multitenant.Api");

    if (context.HostingEnvironment.IsDevelopment())
    {
        loggerConfiguration.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
            formatProvider: CultureInfo.InvariantCulture);
    }
    else
    {
        // Structured JSON lines are what Azure Log Analytics / Application Insights
        // (and AWS CloudWatch when shipped through a log agent) consume natively.
        loggerConfiguration.WriteTo.Console(new CompactJsonFormatter());

        var connectionString = context.Configuration["Serilog:AzureMonitor:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var telemetryConfiguration = new TelemetryConfiguration { ConnectionString = connectionString };
            loggerConfiguration.WriteTo.ApplicationInsights(
                telemetryConfiguration,
                new EventTelemetryConverter(),
                LogEventLevel.Information);
        }
    }
});

builder.Services.AddInfrastructure(builder.Configuration);

var forwardedHeaders = builder.Configuration
    .GetSection(ForwardedHeadersSettings.SectionName)
    .Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();

if (forwardedHeaders.Enabled)
{
    // Validate the proxy configuration at startup instead of at the first request.
    forwardedHeaders.Apply(new ForwardedHeadersOptions());
    builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeaders.Apply);
}

builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>());

builder.Services.AddScoped<ValidationFilter>();
builder.Services.AddValidatorsFromAssemblyContaining<OnboardTenantRequestValidator>();

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var httpContext = context.HttpContext;
        context.ProblemDetails.Instance ??= httpContext.Request.Path;
        context.ProblemDetails.Extensions.TryAdd("traceId", httpContext.TraceIdentifier);
        context.ProblemDetails.Extensions.TryAdd(
            "correlationId",
            httpContext.Items[CorrelationKeys.ItemKey]);
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services
    .AddApiVersioning(options =>
    {
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    })
    .AddOpenApi(options => options.Document.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

builder.Services.AddAuthorization(Policies.Configure);

builder.Services.AddApiRateLimiting();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), ["live"])
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

var cors = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
if (cors.AllowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(cors.AllowedOrigins)
        .WithHeaders("Content-Type", "Authorization", "X-Correlation-Id", "X-Api-Version")
        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
        .DisallowCredentials()));
}

var app = builder.Build();

// Fail fast if a non-development deployment leaves the wildcard host filter in place.
var allowedHosts = app.Configuration["AllowedHosts"];
if (!app.Environment.IsDevelopment()
    && (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Trim() == "*"))
{
    throw new InvalidOperationException(
        "AllowedHosts must list explicit host names outside Development. "
        + "Set it via configuration (AllowedHosts) or the AllowedHosts environment variable.");
}

if (forwardedHeaders.Enabled)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseProblemDetailsStatusCodePages();
app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (cors.AllowedOrigins.Length > 0)
{
    app.UseCors();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapConfirmEmailPage();

// Ensure the ValidationFilter cannot silently fail open: every complex action
// parameter must have a registered FluentValidation validator.
ValidationFilterAssertion.AssertAllActionParametersHaveValidators(app.Services);

app.MapHealthChecks("/health").DisableRateLimiting();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
}).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).DisableRateLimiting();

app.MapOpenApi().WithDocumentPerVersion();
app.MapScalarApiReference(options => options
    .WithTitle("Multitenant API v1")
    .WithTheme(ScalarTheme.Purple));

await using (var scope = app.Services.CreateAsyncScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

await app.RunAsync();
