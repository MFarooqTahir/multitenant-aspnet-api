namespace Multitenant.Application.Common.Paging;

/// <summary>
/// Parses the <c>sort</c> query parameter: <c>field</c> ascending, <c>-field</c>
/// descending. Unknown fields fall back to the resource's default ordering, which
/// prevents callers from sorting on arbitrary columns.
/// </summary>
public static class Sorting
{
    public static (string Field, bool Descending) Parse(
        string? sort,
        IReadOnlySet<string> allowedFields,
        string defaultField)
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
