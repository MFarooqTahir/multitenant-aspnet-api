using Microsoft.EntityFrameworkCore;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.UnitTests;

public sealed class DesignTimeDbContextFactoryTests
{
    [Fact]
    public void CreateDbContext_EnvironmentConnectionStringOverridesJsonConfiguration()
    {
        const string variableName = "ConnectionStrings__Postgres";
        var original = Environment.GetEnvironmentVariable(variableName);
        var expected = "Host=environment.example;Port=5432;Database=environment;Username=env;Password=env-password";

        try
        {
            Environment.SetEnvironmentVariable(variableName, expected);
            using var context = new DesignTimeDbContextFactory().CreateDbContext([]);

            Assert.Equal(expected, context.Database.GetConnectionString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, original);
        }
    }
}
