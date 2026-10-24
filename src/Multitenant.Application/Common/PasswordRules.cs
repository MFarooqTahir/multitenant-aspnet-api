using System.Text.RegularExpressions;

namespace Multitenant.Application.Common;

public static partial class PasswordRules
{
    public const int MinimumLength = 12;

    public const string Description =
        "Password must be at least 12 characters and contain an uppercase letter, a lowercase letter, a digit and a symbol.";

    public static bool IsValid(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= MinimumLength
        && password.Any(char.IsUpper)
        && password.Any(char.IsLower)
        && password.Any(char.IsDigit)
        && password.Any(c => !char.IsLetterOrDigit(c))
        && WhitespacePattern().IsMatch(password) is false;

    [GeneratedRegex(@"\s", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}

public static class TenantSlugRules
{
    public const string Description =
        "Slug must be 3-40 characters, use lowercase letters, digits and single hyphens, and start and end with a letter or digit.";

    public static bool IsValid(string? slug) => Domain.Entities.TenantSlug.IsValid(Domain.Entities.TenantSlug.Normalize(slug));
}
