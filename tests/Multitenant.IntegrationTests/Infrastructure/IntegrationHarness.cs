using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace Multitenant.IntegrationTests.Infrastructure;

/// <summary>
/// Owns the Testcontainers PostgreSQL container and the <see cref="WebApplicationFactory"/>
/// used by every integration test. Tests run inside the "Integration" xUnit collection so the
/// container is only started once and tests do not run in parallel against it.
/// </summary>
public sealed class IntegrationHarness : IDisposable, IAsyncLifetime
{
    public const string CollectionName = "Integration";

    public static PostgreSqlContainer Container { get; } = new("postgres:16-alpine");

    public static string ConnectionString { get; private set; } = string.Empty;

    private static WebApplicationFactory<Program> _factory = CreateFactory();

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        ConnectionString = Container.GetConnectionString();
        _factory = CreateFactory();
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
       await Container.StopAsync();
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    public static WebApplicationFactory<Program> DefaultFactory => _factory;

    public static WebApplicationFactory<Program> CreateFactory(Action<Dictionary<string, string?>>? overrideConfiguration = null)
    {
        var configuration = new Dictionary<string, string?>(CreateDefaultConfiguration())
        {
            ["ConnectionStrings:Postgres"] = ConnectionString,
        };

        overrideConfiguration?.Invoke(configuration);

        return new IntegrationWebApplicationFactory(configuration);
    }

    private static Dictionary<string, string?> CreateDefaultConfiguration() => new()
    {
        ["Database:AutoMigrate"] = "true",
        ["Database:SeedDemoData"] = "true",
        ["Demo:Password"] = "Demo123!Passw0rd",
        ["Jwt:SigningKey"] = "integration-test-signing-key-0123456789abcdef0123456789",
        ["Jwt:Issuer"] = "multitenant-aspnet-api",
        ["Jwt:Audience"] = "multitenant-aspnet-api",
        ["Auth:RefreshTokenDays"] = "30",
        ["RateLimiting:Tenant:PermitLimit"] = "1000",
        ["RateLimiting:Auth:PermitLimit"] = "1000",
        ["Serilog:MinimumLevel:Default"] = "Warning",
    };

    private sealed class IntegrationWebApplicationFactory(Dictionary<string, string?> configuration)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
        }
    }
}
