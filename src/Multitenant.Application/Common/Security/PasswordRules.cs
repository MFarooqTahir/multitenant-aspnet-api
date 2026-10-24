using System.Text.RegularExpressions;

namespace Multitenant.Application.Common.Security;

/// <summary>
/// One password policy shared by registration, onboarding and admin user creation.
/// </summary>
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
        && !WhitespacePattern().IsMatch(password);

    [GeneratedRegex(@"\s", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
