using System.ComponentModel.DataAnnotations;

namespace Multitenant.Application.Configuration.Auth;

/// <summary>
/// Controls the in-process refresh-token retention job. Old rows are kept for a
/// grace period so an operator can still investigate a revoked/expired family.
/// </summary>
public sealed class RefreshTokenCleanupOptions
{
    public const string SectionName = "Auth:RefreshTokenCleanup";

    public bool Enabled { get; set; } = true;

    [Range(1, 168)]
    public int IntervalHours { get; set; } = 24;

    [Range(1, 365)]
    public int RetentionDays { get; set; } = 30;

    [Range(100, 10_000)]
    public int BatchSize { get; set; } = 1_000;

    [Range(1, 100)]
    public int MaxBatchesPerPass { get; set; } = 100;
}
