namespace Multitenant.Application.Configuration.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Global budget for authenticated traffic, partitioned per tenant.</summary>
    public RateLimitPolicyOptions Tenant { get; set; } = new();

    /// <summary>Stricter budget for anonymous auth endpoints, partitioned per IP.</summary>
    public RateLimitPolicyOptions Auth { get; set; } = new();
}
