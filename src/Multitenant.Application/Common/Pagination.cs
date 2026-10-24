namespace Multitenant.Application.Common;

public static class Pagination
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize switch
        {
            < 1 => 20,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };

        return (normalizedPage, normalizedPageSize);
    }

    public static int Skip(int page, int pageSize) => (page - 1) * pageSize;
}

public static class Sorting
{
    /// <summary>
    /// Accepts "field" for ascending and "-field" for descending, and only allows fields in the whitelist.
    /// </summary>
    public static (string Field, bool Descending) Parse(string? sort, IReadOnlySet<string> allowedFields, string defaultField)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return (defaultField, false);
        }

        var trimmed = sort.Trim();
        var descending = trimmed.StartsWith('-');
        var field = descending ? trimmed[1..] : trimmed;

        if (!allowedFields.Contains(field, StringComparer.OrdinalIgnoreCase))
        {
            return (defaultField, false);
        }

        return (field.ToLowerInvariant(), descending);
    }
}
