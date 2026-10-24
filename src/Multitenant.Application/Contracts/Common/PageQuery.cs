namespace Multitenant.Application.Contracts;

/// <summary>Common paging and sort parameters for list endpoints.</summary>
public abstract class PageQuery
{
    /// <summary>1-based page number. Values below 1 are clamped to 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Items per page, clamped to 100.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary><c>field</c> for ascending, <c>-field</c> for descending.</summary>
    public string? Sort { get; set; }
}
