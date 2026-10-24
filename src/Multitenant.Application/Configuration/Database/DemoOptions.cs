namespace Multitenant.Application.Configuration.Database;

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>Password assigned to every seeded demo account.</summary>
    public string Password { get; set; } = string.Empty;
}
