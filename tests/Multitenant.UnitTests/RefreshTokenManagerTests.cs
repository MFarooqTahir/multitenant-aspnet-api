using Multitenant.Infrastructure.Identity;

namespace Multitenant.UnitTests;

public sealed class RefreshTokenManagerTests
{
    [Fact]
    public void HashesTokensDeterministically()
    {
        var hash1 = RefreshTokenManager.Hash("abc");
        var hash2 = RefreshTokenManager.Hash("abc");
        var hash3 = RefreshTokenManager.Hash("abd");

        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hash3);
    }

    [Fact]
    public void Hash_KnownSha256Value_UsesBase64Url()
    {
        Assert.Equal("ungWv48Bz-pBQUDeXa4iI7ADYaOWF3qctBD_YfIAFa0", RefreshTokenManager.Hash("abc"));
    }

    [Fact]
    public void CreatesUrlSafeOpaqueTokens_WithExactLengthAndAlphabet()
    {
        var token = RefreshTokenManager.CreateRawToken();

        Assert.Equal(43, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
        var bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");
        Assert.Equal(32, bytes.Length);
    }

    [Fact]
    public void GeneratesUniqueTokens()
    {
        var first = RefreshTokenManager.CreateRawToken();
        var second = RefreshTokenManager.CreateRawToken();

        Assert.NotEqual(first, second);
    }
}
