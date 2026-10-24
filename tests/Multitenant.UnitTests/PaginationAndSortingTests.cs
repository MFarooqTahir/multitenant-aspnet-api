using Multitenant.Application.Common;
using Multitenant.Application.Common.Paging;

namespace Multitenant.UnitTests;

public sealed class PaginationAndSortingTests
{
    [Fact]
    public void NormalizesPageAndSize()
    {
        var (page, size) = Pagination.Normalize(0, 0);
        Assert.Equal(1, page);
        Assert.Equal(20, size);

        var (_, max) = Pagination.Normalize(1, 500);
        Assert.Equal(100, max);
    }

    [Fact]
    public void ComputesSkip()
    {
        Assert.Equal(40, Pagination.Skip(3, 20));
    }

    [Fact]
    public void ParsesAscendingAndDescendingSort()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "createdat" };

        Assert.Equal(("name", false), Sorting.Parse("name", allowed, "createdat"));
        Assert.Equal(("createdat", true), Sorting.Parse("-createdAt", allowed, "createdat"));
    }

    [Fact]
    public void FallsBackToDefaultSortForUnknownFields()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "createdat" };

        Assert.Equal(("createdat", false), Sorting.Parse("evilField", allowed, "createdat"));
    }
}
