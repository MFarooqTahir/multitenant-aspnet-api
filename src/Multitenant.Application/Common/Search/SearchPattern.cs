namespace Multitenant.Application.Common.Search;

/// <summary>
/// Builds a literal contains-pattern for PostgreSQL <c>ILIKE</c>. User-supplied
/// <c>%</c>, <c>_</c> and backslashes are escaped so search cannot turn into a
/// wildcard scan accidentally.
/// </summary>
public static class SearchPattern
{
    public const string EscapeCharacter = "\\";

    public static string Contains(string value)
    {
        var escaped = value
            .Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }
}
