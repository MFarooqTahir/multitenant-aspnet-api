using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Api;
using Testcontainers.PostgreSql;

namespace Multitenant.IntegrationTests.Infrastructure;

/// <summary>
/// Owns the Testcontainers PostgreSQL container and the WebApplicationFactory used
/// by every integration test. Registered as an xUnit collection fixture, so the
/// container starts once per test run and all tests in the collection share it.
/// </summary>
public sealed class IntegrationHarness : IDisposable, IAsyncLifetime
{
    public const string CollectionName = "Integration";

    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public WebApplicationFactory<IApiMarker> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        Factory = CreateFactory();
    }

    public async Task DisposeAsync()
    {
        Factory.Dispose();
        await _container.DisposeAsync();
    }

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public WebApplicationFactory<IApiMarker> CreateFactory(
        Action<Dictionary<string, string?>>? overrideConfiguration = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var configuration = new Dictionary<string, string?>(CreateDefaultConfiguration())
        {
            ["ConnectionStrings:Postgres"] = ConnectionString,
        };

        overrideConfiguration?.Invoke(configuration);

        return new IntegrationWebApplicationFactory(configuration, configureServices);
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

    private sealed class IntegrationWebApplicationFactory : WebApplicationFactory<IApiMarker>
    {
        private readonly Dictionary<string, string?> _configuration;
        private readonly Action<IServiceCollection>? _configureServices;

        public IntegrationWebApplicationFactory(
            Dictionary<string, string?> configuration,
            Action<IServiceCollection>? configureServices)
        {
            _configuration = configuration;
            _configureServices = configureServices;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_configuration));

            if (_configureServices is not null)
            {
                builder.ConfigureTestServices(_configureServices);
            }
        }
    }
}
