namespace Multitenant.Application.Configuration.Web;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Explicit origins. An empty list disables CORS entirely.</summary>
    public string[] AllowedOrigins { get; set; } = [];
}
