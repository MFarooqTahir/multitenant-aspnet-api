using System.Globalization;
using Asp.Versioning;
using FluentValidation;
using Microsoft.ApplicationInsights.Extensibility;
using Serilog.Events;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Multitenant.Api.Authorization;
using Multitenant.Api.Filters;
using Multitenant.Api.Middleware;
using Multitenant.Api.RateLimiting;
using Multitenant.Application.Common;
using Multitenant.Application.Configuration;
using Multitenant.Application.Validation;
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
                LogEventLevel.Information,
                null);
        }
    }
});

builder.Services.AddInfrastructure(builder.Configuration);

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
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    })
    .AddOpenApi();


builder.Services.AddAuthorization(Policies.Configure);

builder.Services.AddApiRateLimiting(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), ["live"])
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

var cors = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
if (cors.AllowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(cors.AllowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();

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

/// <summary>Exposed so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
