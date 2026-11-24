using Multitenant.Application.Common.Search;

namespace Multitenant.UnitTests;

public sealed class SearchPatternTests
{
    [Fact]
    public void Contains_EscapesPercentUnderscoreAndBackslashLiterally()
    {
        Assert.Equal("%100\\%\\_ready%", SearchPattern.Contains("100%_ready"));
        Assert.Equal("%path\\\\name%", SearchPattern.Contains(@"path\name"));
    }

    [Fact]
    public void Contains_TrimsAndWrapsLiteralSearch()
    {
        Assert.Equal("%alpha%", SearchPattern.Contains("  alpha  "));
        Assert.Equal("%%", SearchPattern.Contains("   "));
    }
}
