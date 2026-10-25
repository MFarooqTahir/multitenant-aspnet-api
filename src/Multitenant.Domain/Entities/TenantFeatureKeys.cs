namespace Multitenant.Domain.Entities;

/// <summary>
/// The feature keys a tenant may switch. This is a fixed whitelist: a typo cannot
/// create a flag that nothing reads, and unknown keys are rejected at the boundary.
/// </summary>
public static class TenantFeatureKeys
{
    /// <summary>
    /// Assigning tasks to a user. Enabled unless a tenant explicitly turns it off,
    /// so the default preserves the behaviour that existed before flags.
    /// </summary>
    public const string TaskAssignment = "task_assignment";

    public static IReadOnlyList<string> All { get; } = [TaskAssignment];

    public static string Normalize(string? key) => (key ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValid(string? key) =>
        All.Contains(Normalize(key), StringComparer.Ordinal);
}
