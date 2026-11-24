using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Multitenant.Api.Configuration;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Application.Contracts.Projects;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class PlatformHardeningTests : IntegrationTestBase
{
    public PlatformHardeningTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task UnsupportedMediaType_ReturnsProblemCode()
    {
        using var client = CreateClient();

        using var unsupportedContent = new StringContent("{}", Encoding.UTF8, "text/plain");
        var unsupported = await client.PostAsync("/api/v1/auth/login", unsupportedContent);

        await AssertProblemAsync(unsupported, HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
    }

    [Fact]
    public async Task RequestBodyLimits_AreConfiguredAtGlobalAndOnboardingLevels()
    {
        using var factory = Harness.CreateFactory();
        using var client = factory.CreateClient();

        var kestrelOptions = factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>>().Value;
        Assert.Equal(64 * 1024, kestrelOptions.Limits.MaxRequestBodySize);
        var onboard = typeof(Multitenant.Api.Controllers.TenantsController).GetMethod(nameof(Multitenant.Api.Controllers.TenantsController.Onboard))!;
        var limit = Assert.Single(onboard.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute), false));
        Assert.Equal(16 * 1024, ((Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata)limit).MaxRequestBodySize);
    }

    [Fact]
    public void ProblemDetails_For413And415_HaveStableCodes()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        var tooLarge = Multitenant.Api.ErrorHandling.ApiProblemDetails.CreateForStatus(context, StatusCodes.Status413PayloadTooLarge);
        var unsupported = Multitenant.Api.ErrorHandling.ApiProblemDetails.CreateForStatus(context, StatusCodes.Status415UnsupportedMediaType);

        Assert.Equal("payload_too_large", tooLarge!.Extensions["code"]?.ToString());
        Assert.Equal("unsupported_media_type", unsupported!.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task SecurityHeaders_ArePresentOnApiAndConfirmationPageHasCsp()
    {
        using var client = CreateClient();

        var api = await client.GetApi("/health/live");
        var confirmation = await client.GetApi("/confirm-email");

        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.Equal("nosniff", api.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", api.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", api.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        Assert.Equal("nosniff", confirmation.Headers.GetValues("X-Content-Type-Options").Single());
        var csp = confirmation.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("form-action 'self'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cors_ExposesRetryAfterAndCorrelationId()
    {
        const string origin = "https://client.example";
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Cors:AllowedOrigins:0"] = origin);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-Correlation-Id", "cors-correlation-id");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var exposed = response.Headers.GetValues("Access-Control-Expose-Headers").Single();
        Assert.Contains("Retry-After", exposed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-Correlation-Id", exposed, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Jwt:SigningKey", "short", "SigningKey")]
    [InlineData("Auth:RefreshTokenDays", "366", "RefreshTokenDays")]
    [InlineData("RateLimiting:Tenant:PermitLimit", "100001", "permit limits must be 1–100,000")]
    [InlineData("RateLimiting:Tenant:WindowSeconds", "86401", "permit limits must be 1–100,000")]
    [InlineData("RateLimiting:Auth:PermitLimit", "100001", "permit limits must be 1–100,000")]
    [InlineData("RateLimiting:Auth:WindowSeconds", "86401", "permit limits must be 1–100,000")]
    [InlineData("Email:Smtp:Port", "0", "Email:Smtp:Port")]
    [InlineData("Cors:AllowedOrigins:0", "http://example.com", "Cors:AllowedOrigins")]
    [InlineData("Cors:AllowedOrigins:0", "ftp://localhost", "Cors:AllowedOrigins")]
    [InlineData("Cors:AllowedOrigins:0", "*", "Cors:AllowedOrigins")]
    [InlineData("Cors:AllowedOrigins:0", "https://example.com/path", "Cors:AllowedOrigins")]
    public void StartupOptions_InvalidConfigurationThrowsOptionsValidationException(
        string key,
        string value,
        string expectedMessage)
    {
        using var factory = Harness.CreateFactory(configuration => configuration[key] = value);

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("smtp-user", "")]
    [InlineData("", "smtp-password")]
    public void SmtpCredentials_MustBePairedAtStartup(string username, string password)
    {
        using var factory = Harness.CreateFactory(configuration =>
        {
            configuration["Email:Provider"] = "Smtp";
            configuration["Email:Smtp:Host"] = "smtp.example.com";
            configuration["Email:Smtp:UseStartTls"] = "true";
            configuration["Email:Smtp:Username"] = username;
            configuration["Email:Smtp:Password"] = password;
        });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("configured together", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionSmtp_WithoutStartTlsOutsideLoopbackFailsStartup()
    {
        using var factory = Harness.CreateFactory(configuration =>
        {
            configuration["Email:Provider"] = "Smtp";
            configuration["Email:Smtp:Host"] = "smtp.example.com";
            configuration["Email:Smtp:UseStartTls"] = "false";
        }, environment: "Production");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Equal("Non-development SMTP connections must use StartTLS unless the server is on loopback.", exception.Message);
    }

    [Fact]
    public async Task NonDevelopmentSmtp_LoopbackWithoutStartTlsStarts()
    {
        using var factory = Harness.CreateFactory(configuration =>
        {
            configuration["Email:Provider"] = "Smtp";
            configuration["Email:Smtp:Host"] = "localhost";
            configuration["Email:Smtp:UseStartTls"] = "false";
        }, environment: "Production");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetApi("/health/live")).StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void ForwardedHeaders_AcceptsForwardLimitAtBounds(int forwardLimit)
    {
        var settings = new ForwardedHeadersSettings { ForwardLimit = forwardLimit };
        var options = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions();

        settings.Apply(options);

        Assert.Equal(forwardLimit, options.ForwardLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void ForwardedHeaders_RejectsForwardLimitOutsideOneToTwenty(int forwardLimit)
    {
        var settings = new ForwardedHeadersSettings
        {
            ForwardLimit = forwardLimit,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            settings.Apply(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions()));

        Assert.Equal("ForwardedHeaders:ForwardLimit must be between 1 and 20.", exception.Message);
    }

    [Fact]
    public void AllowedHosts_MixedWildcardIsRejectedOutsideDevelopment()
    {
        using var factory = Harness.CreateFactory(configuration =>
            configuration["AllowedHosts"] = "localhost;*");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("AllowedHosts must list explicit host names outside Development", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentUpdate_ReturnsConflictWithConcurrencyCode()
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "platform-concurrency", "platform-concurrency@acme.test");
        var project = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Concurrency probe");

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync("""
                BEGIN;
                DROP TRIGGER IF EXISTS trg_delete_project_during_update_for_test ON "Projects";
                CREATE OR REPLACE FUNCTION skip_project_update_for_test() RETURNS trigger AS $$
                BEGIN
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_skip_project_update_for_test
                BEFORE UPDATE ON "Projects"
                FOR EACH ROW EXECUTE FUNCTION skip_project_update_for_test();
                COMMIT;
                """);
        }

        try
        {
            var response = await client.PutApi(
                $"/api/v1/projects/{project.Id}",
                new UpdateProjectRequest("Concurrent update", null, ProjectStatus.Active),
                tokens.AccessToken);

            await AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency_conflict");
        }
        finally
        {
            using var scope = Harness.Factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS trg_skip_project_update_for_test ON "Projects";
                DROP FUNCTION IF EXISTS skip_project_update_for_test();
                """);
        }
    }

    [Fact]
    public async Task LoggingEmailSender_RedactsTokenAndDoesNotLogRecipient()
    {
        var sink = new CapturingSerilogSink();
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Serilog:MinimumLevel:Default"] = "Information",
            services => services.AddSingleton<Serilog.Core.ILogEventSink>(sink));
        using var client = factory.CreateClient();
        const string recipient = "private-recipient@acme.test";

        var response = await client.PostApi(
            "/api/v1/tenants/onboard",
            new Multitenant.Application.Contracts.Tenants.OnboardTenantRequest(
                "email-log-redaction",
                "Email Log Redaction",
                recipient,
                "Email Log Owner",
                Scenarios.Password));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var emailLog = Assert.Single(sink.Entries, entry =>
            entry.Message.Contains("Email with subject", StringComparison.Ordinal));
        Assert.Contains("token=[REDACTED]", emailLog.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(recipient, emailLog.Message, StringComparison.Ordinal);
        Assert.All(sink.Entries, entry => Assert.DoesNotContain(recipient, entry.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SerilogRequestLogs_OmitQueryValuesAndRetainRoutePath()
    {
        var sink = new CapturingSerilogSink();
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Serilog:MinimumLevel:Default"] = "Information",
            services => services.AddSingleton<Serilog.Core.ILogEventSink>(sink));
        using var client = factory.CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, "serilog-query-redaction", "serilog-query@acme.test");
        const string secret = "query-secret-value";

        var response = await client.GetApi($"/api/v1/projects?search={secret}", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline && !sink.Entries.Any(entry => entry.Message.Contains("/api/v1/projects", StringComparison.Ordinal)))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        var requestLog = Assert.Single(sink.Entries, entry => entry.Message.Contains("/api/v1/projects", StringComparison.Ordinal));
        Assert.DoesNotContain(secret, requestLog.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("search=", requestLog.Message, StringComparison.Ordinal);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal(code, problem.Extensions["code"]?.ToString());
    }
}
