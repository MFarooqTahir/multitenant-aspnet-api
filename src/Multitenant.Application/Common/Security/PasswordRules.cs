using System.Text.RegularExpressions;

namespace Multitenant.Application.Common.Security;

/// <summary>
/// One password policy shared by registration, onboarding and admin user creation.
/// </summary>
public static partial class PasswordRules
{
    public const int MinimumLength = 12;

    public const bool RequireUppercase = true;

    public const bool RequireLowercase = true;

    public const bool RequireDigit = true;

    public const bool RequireNonAlphanumeric = true;

    public const string Description =
        "Password must be at least 12 characters and contain an uppercase letter, a lowercase letter, a digit and a symbol.";

    public static bool IsValid(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= MinimumLength
        && (!RequireUppercase || password.Any(char.IsUpper))
        && (!RequireLowercase || password.Any(char.IsLower))
        && (!RequireDigit || password.Any(char.IsDigit))
        && (!RequireNonAlphanumeric || password.Any(c => !char.IsLetterOrDigit(c)))
        && !WhitespacePattern().IsMatch(password);

    [GeneratedRegex(@"\s", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
