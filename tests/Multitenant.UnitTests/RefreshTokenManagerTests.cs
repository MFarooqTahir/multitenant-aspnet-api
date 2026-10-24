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
    public void CreatesUrlSafeOpaqueTokens()
    {
        var token = RefreshTokenManager.CreateRawToken();

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(token.Length is > 20 and <= 128);
        Assert.DoesNotContain('=', token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
    }

    [Fact]
    public void GeneratesUniqueTokens()
    {
        var first = RefreshTokenManager.CreateRawToken();
        var second = RefreshTokenManager.CreateRawToken();

        Assert.NotEqual(first, second);
    }
}
