namespace Multitenant.Application.Configuration.Database;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending migrations at startup. Convenient for local/demo, avoid in production.</summary>
    public bool AutoMigrate { get; set; }

    /// <summary>Seed the three roles and the <c>acme</c>/<c>globex</c> demo tenants.</summary>
    public bool SeedDemoData { get; set; }
}
