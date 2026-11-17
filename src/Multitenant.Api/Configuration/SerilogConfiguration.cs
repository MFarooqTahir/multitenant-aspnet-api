using System.Globalization;
using Microsoft.ApplicationInsights.Extensibility;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;

namespace Multitenant.Api.Configuration;

/// <summary>
/// Wires Serilog from configuration plus the hosting environment: readable console
/// text in Development, compact JSON everywhere else, and an optional Application
/// Insights sink when a connection string is configured.
/// </summary>
public static class SerilogConfiguration
{
    public static void ConfigureApiSerilog(this WebApplicationBuilder builder)
    {
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
    }
}
