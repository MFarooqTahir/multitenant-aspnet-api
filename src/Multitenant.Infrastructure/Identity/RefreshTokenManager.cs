using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Multitenant.Application.Configuration.Auth;
using Multitenant.Domain.Entities;

namespace Multitenant.Infrastructure.Identity;

public sealed class RefreshTokenManager(
    Persistence.AppDbContext dbContext,
    IOptions<AuthOptions> authOptions,
    TimeProvider timeProvider)
{
    private readonly AuthOptions _auth = authOptions.Value;

    public static string Hash(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public (string RawToken, RefreshToken Entity) Create(
        AppUser user,
        string? ipAddress,
        Guid? familyId = null,
        Guid? id = null)
    {
        var raw = CreateRawToken();
        var now = timeProvider.GetUtcNow();
        var entity = new RefreshToken
        {
            Id = id ?? Guid.NewGuid(),
            UserId = user.Id,
            TenantId = user.TenantId,
            TokenHash = Hash(raw),
            FamilyId = familyId ?? Guid.NewGuid(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_auth.RefreshTokenDays),
            CreatedByIp = ipAddress,
        };

        dbContext.RefreshTokens.Add(entity);
        return (raw, entity);
    }



    public async Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await dbContext.RefreshTokens
            .Where(x => x.FamilyId == familyId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.RevokedAtUtc, now)
                    .SetProperty(x => x.RevokedReason, reason),
                cancellationToken);
    }

    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await dbContext.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.RevokedAtUtc, now)
                    .SetProperty(x => x.RevokedReason, reason),
                cancellationToken);
    }

    public static string CreateRawToken()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
