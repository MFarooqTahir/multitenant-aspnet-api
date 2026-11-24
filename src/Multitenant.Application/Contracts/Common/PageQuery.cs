namespace Multitenant.Application.Contracts.Common;

/// <summary>Common paging and sort parameters for list endpoints.</summary>
public abstract class PageQuery
{
    /// <summary>1-based page number, clamped to 1–10,000.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Items per page; values below 1 use 20 and values above 100 use 100.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary><c>field</c> for ascending, <c>-field</c> for descending.</summary>
    public string? Sort { get; set; }
}
