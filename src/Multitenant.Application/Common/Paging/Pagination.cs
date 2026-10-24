namespace Multitenant.Application.Common;

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

    public static int Skip(int page, int pageSize) => (page - 1) * pageSize;
}
