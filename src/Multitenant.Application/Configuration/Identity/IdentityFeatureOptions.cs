namespace Multitenant.Application.Configuration;

/// <summary>
/// Feature switches for email confirmation and two-factor authentication.
/// Both are implemented but off by default so the demo stays frictionless.
/// </summary>
public sealed class IdentityFeatureOptions
{
    public const string SectionName = "Identity";

    public bool RequireConfirmedEmail { get; set; }

    public bool EnableTwoFactor { get; set; }
}
