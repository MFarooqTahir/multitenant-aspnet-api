using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

public sealed class TotpTests
{
    // RFC 6238's Base32 secret decodes to the ASCII digits "1234567890" twice.
    // Keep the public vector as bytes so secret scanners do not mistake it for a credential.
    private static readonly byte[] RfcSha1Key = "12345678901234567890"u8.ToArray();

    [Theory]
    [InlineData(59, "94287082")]
    [InlineData(1_111_111_109, "07081804")]
    [InlineData(1_234_567_890, "89005924")]
    [InlineData(2_000_000_000, "69279037")]
    [InlineData(20_000_000_000, "65353130")]
    public void Compute_KnownRfc6238Sha1Vectors_Matches(long unixSeconds, string expected)
    {
        var actual = Totp.Compute(RfcSha1Key, DateTimeOffset.FromUnixTimeSeconds(unixSeconds), digits: 8);

        Assert.Equal(expected, actual);
    }
}
