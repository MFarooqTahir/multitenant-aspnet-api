using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

public sealed class TotpTests
{
    private const string RfcSha1Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"; // Public RFC 6238 vector; Gitleaks exception is fingerprinted in .gitleaksignore.

    [Theory]
    [InlineData(59, "94287082")]
    [InlineData(1_111_111_109, "07081804")]
    [InlineData(1_234_567_890, "89005924")]
    [InlineData(2_000_000_000, "69279037")]
    [InlineData(20_000_000_000, "65353130")]
    public void Compute_KnownRfc6238Sha1Vectors_Matches(long unixSeconds, string expected)
    {
        var actual = Totp.Compute(RfcSha1Secret, DateTimeOffset.FromUnixTimeSeconds(unixSeconds), digits: 8);

        Assert.Equal(expected, actual);
    }
}
