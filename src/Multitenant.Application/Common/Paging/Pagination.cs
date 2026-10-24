namespace Multitenant.Application.Common.Paging;

/// <summary>Shared paging rules so every list endpoint clamps input the same way.</summary>
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

    /// <summary>
    /// Converts a page number to an SQL offset. Computed in 64-bit and capped at
    /// <see cref="int.MaxValue"/> so an absurd page number cannot overflow into a
    /// negative offset and turn into a 500.
    /// </summary>
    public static int Skip(int page, int pageSize)
    {
        var skip = (long)(page - 1) * pageSize;
        return skip >= int.MaxValue ? int.MaxValue : (int)skip;
    }
}
