using System.Text.RegularExpressions;

namespace Multitenant.Domain.Entities;

public static partial class TenantSlug
{
    public const int MinLength = 3;
    public const int MaxLength = 40;

    public static string Normalize(string? slug) => slug?.Trim().ToLowerInvariant() ?? string.Empty;

    public static bool IsValid(string? slug)
    {
        var normalized = Normalize(slug);
        return !string.IsNullOrWhiteSpace(normalized)
            && normalized.Length is >= MinLength and <= MaxLength
            && SlugPattern().IsMatch(normalized)
            && !normalized.Contains("--", StringComparison.Ordinal);
    }

    public static void EnsureValid(string slug)
    {
        if (!IsValid(slug))
        {
            throw new DomainException(
                $"Tenant slug must be {MinLength}-{MaxLength} characters, contain only lowercase letters, digits and single hyphens, and start and end with a letter or digit.");
        }
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
