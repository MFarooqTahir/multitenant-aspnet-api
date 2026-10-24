using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Multitenant.Infrastructure.Tenancy;

namespace Multitenant.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> at design time. Keeping it here means migrations can
/// be generated without booting the API (and without running startup seeding).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=multitenantDb;Username=multitenant-DbUser;Password=multitenant-DbPassword123!";

    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = FallbackConnectionString;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory");
            })
            .Options;

        return new AppDbContext(options, new TenantContext());
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .AddEnvironmentVariables();

        var apiDirectory = FindApiDirectory();
        if (apiDirectory is not null)
        {
            builder.SetBasePath(apiDirectory);
            builder.AddJsonFile("appsettings.json", optional: true);
            builder.AddJsonFile("appsettings.Development.json", optional: true);
        }

        return builder.Build();
    }

    private static string? FindApiDirectory()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        };

        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                var api = Path.Combine(directory.FullName, "src", "Multitenant.Api");
                if (Directory.Exists(api))
                {
                    return api;
                }

                if (File.Exists(Path.Combine(directory.FullName, "Multitenant.Api.csproj"))
                    && File.Exists(Path.Combine(directory.FullName, "appsettings.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
