using System.Text.Json.Serialization;
using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Multitenant.Api.Authorization;
using Multitenant.Api.Configuration;
using Multitenant.Api.Endpoints;
using Multitenant.Api.Filters;
using Multitenant.Api.Middleware;
using Multitenant.Api.OpenApi;
using Multitenant.Api.RateLimiting;
using Multitenant.Application.Configuration.Auth;
using Multitenant.Application.Configuration.Email;
using Multitenant.Application.Configuration.Identity;
using Multitenant.Application.Configuration.Jwt;
using Multitenant.Application.Configuration.Web;
using Multitenant.Application.Validation.Tenants;
using Multitenant.Infrastructure;
using Multitenant.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// This API accepts only small JSON documents. A global Kestrel cap protects every
// endpoint before model binding, while credential endpoints tighten it to 16 KiB.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);

builder.ConfigureApiSerilog();

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

// Enums cross the wire as readable names ("Active", "High"); numeric values are still
// accepted on input so existing clients keep working.
builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddApiErrorContract();
builder.Services.AddScoped<ValidationFilter>();
builder.Services.AddValidatorsFromAssemblyContaining<OnboardTenantRequestValidator>();

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
var allowedCorsOrigins = cors.AllowedOrigins ?? [];
if (allowedCorsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedCorsOrigins)
        .WithHeaders("Content-Type", "Authorization", "X-Correlation-Id", "X-Api-Version")
        .WithExposedHeaders("Retry-After", "X-Correlation-Id")
        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
        .DisallowCredentials()));
}

var app = builder.Build();

// Fail fast if a non-development deployment leaves the wildcard host filter in place.
// Auth:PublicBaseUrl gets the same treatment through its options validator.
var allowedHosts = app.Configuration["AllowedHosts"];
var allowedHostEntries = (allowedHosts ?? string.Empty)
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (!app.Environment.IsDevelopment()
    && (allowedHostEntries.Length == 0 || allowedHostEntries.Any(host => host == "*")))
{
    throw new InvalidOperationException(
        "AllowedHosts must list explicit host names outside Development. "
        + "Set it via configuration (AllowedHosts) or the AllowedHosts environment variable.");
}

if (!app.Environment.IsDevelopment())
{
    var jwtOptions = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
    var authOptions = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
    var loopbackOnly = Uri.TryCreate(authOptions.PublicBaseUrl, UriKind.Absolute, out var publicUri)
        && publicUri.IsLoopback;

    if (!loopbackOnly
        && (jwtOptions.SigningKey.Contains("dev-only", StringComparison.OrdinalIgnoreCase)
            || jwtOptions.SigningKey.Contains("development-only", StringComparison.OrdinalIgnoreCase)
            || jwtOptions.SigningKey.Contains("local-development-key", StringComparison.OrdinalIgnoreCase)
            || jwtOptions.SigningKey.Contains("replace-with", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Non-development deployments cannot use a development or placeholder JWT signing key.");
    }

    var identityOptions = app.Services.GetRequiredService<IOptions<IdentityFeatureOptions>>().Value;
    var emailOptions = app.Services.GetRequiredService<IOptions<EmailOptions>>().Value;

    if (identityOptions.RequireConfirmedEmail
        && string.Equals(emailOptions.Provider, "Logging", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Identity:RequireConfirmedEmail needs a real Email:Provider outside Development.");
    }

    if (string.Equals(emailOptions.Provider, "Smtp", StringComparison.OrdinalIgnoreCase)
        && !emailOptions.Smtp.UseStartTls
        && !(Uri.TryCreate(
                $"smtp://{emailOptions.Smtp.Host}:{emailOptions.Smtp.Port}",
                UriKind.Absolute,
                out var smtpUri)
            && smtpUri.IsLoopback))
    {
        throw new InvalidOperationException(
            "Non-development SMTP connections must use StartTLS unless the server is on loopback.");
    }
}

if (forwardedHeaders.Enabled)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

// Request logging wraps exception handling so it sees the final ProblemDetails
// status (404, not the intermediate 500) and does not duplicate handled stack traces.
app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, _, _) =>
        httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest
                ? LogEventLevel.Warning
                : LogEventLevel.Information;

    // Search terms and other query values can contain personal data. Keep the
    // route shape in request logs without persisting the raw query string.
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        diagnosticContext.Set("RequestPath", httpContext.Request.Path.Value);
});

app.UseExceptionHandler();
app.UseProblemDetailsStatusCodePages();

app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (allowedCorsOrigins.Length > 0)
{
    app.UseCors();
}

app.UseAuthentication();
app.UseMiddleware<TenantLogContextMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapConfirmEmailPage();

// Ensure the ValidationFilter cannot silently fail open: every complex action
// parameter must have a registered FluentValidation validator.
ValidationFilterAssertion.AssertAllActionParametersHaveValidators(app.Services);

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

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
