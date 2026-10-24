using System.ComponentModel.DataAnnotations;

namespace Multitenant.Application.Configuration;

public sealed class RateLimitPolicyOptions
{
    [Range(1, 100_000)]
    public int PermitLimit { get; set; } = 100;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}
