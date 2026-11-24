using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Multitenant.Application.Common;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Contracts.Users;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class JwtLiveValidationTests : IntegrationTestBase
{
    private const string SigningKey = "integration-test-signing-key-0123456789abcdef0123456789"; // Test-only key for invalid-JWT cases; fingerprinted in .gitleaksignore.

    public JwtLiveValidationTests(IntegrationHarness harness) => Harness = harness;

    [Theory]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-signature")]
    [InlineData("wrong-algorithm")]
    [InlineData("expired")]
    [InlineData("missing-exp")]
    [InlineData("missing-sub")]
    [InlineData("missing-tenant-id")]
    [InlineData("missing-tenant-slug")]
    [InlineData("missing-role")]
    [InlineData("missing-security-stamp")]
    [InlineData("empty-security-stamp")]
    [InlineData("tampered-security-stamp")]
    [InlineData("tenant-slug-mismatch")]
    [InlineData("role-mismatch")]
    public async Task AccessToken_WithStructuralDefect_ReturnsUnauthorized(string defect)
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(
            client,
            $"jwt-{defect}",
            $"jwt-{defect}@acme.test");
        var token = CreateDefectiveToken(tokens.AccessToken, defect);

        var response = await client.GetApi("/api/v1/tenants/current", token);

        await AssertUnauthorizedProblemAsync(response);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("user")]
    public async Task AccessToken_WithInactiveTenantOrUser_ReturnsUnauthorized(string inactiveTarget)
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(
            client,
            $"jwt-inactive-{inactiveTarget}",
            $"jwt-inactive-{inactiveTarget}@acme.test");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        var tenantId = Guid.Parse(jwt.Claims.Single(claim => claim.Type == AuthClaims.TenantId).Value);
        var userId = Guid.Parse(jwt.Subject);

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (inactiveTarget == "tenant")
            {
                await dbContext.Database.ExecuteSqlAsync(
                    $"UPDATE \"Tenants\" SET \"IsActive\" = FALSE WHERE \"Id\" = {tenantId}");
            }
            else
            {
                await dbContext.Database.ExecuteSqlAsync(
                    $"UPDATE \"AspNetUsers\" SET \"IsActive\" = FALSE WHERE \"Id\" = {userId}");
            }
        }

        var response = await client.GetApi("/api/v1/tenants/current", tokens.AccessToken);

        await AssertUnauthorizedProblemAsync(response);
    }

    [Fact]
    public async Task AccessToken_WithUnconfirmedEmailUnderFlag_ReturnsUnauthorized()
    {
        using var setupClient = CreateClient();
        var tenant = await Scenarios.OnboardAsync(
            setupClient,
            "jwt-unconfirmed",
            "JWT Unconfirmed",
            "jwt-unconfirmed@acme.test");
        var tokens = await Scenarios.LoginAsync(setupClient, tenant.Slug, "jwt-unconfirmed@acme.test");

        await using var strictFactory = Harness.CreateFactory(
            configuration => configuration["Identity:RequireConfirmedEmail"] = "true",
            environment: "Development");
        using var strictClient = strictFactory.CreateClient();

        var response = await strictClient.GetApi("/api/v1/tenants/current", tokens.AccessToken);

        await AssertUnauthorizedProblemAsync(response);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    public async Task AccessToken_WithNullOrEmptyDatabaseStamp_ReturnsUnauthorized(string stampCase)
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(
            client,
            $"jwt-db-stamp-{stampCase}",
            $"jwt-db-stamp-{stampCase}@acme.test");
        var userId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken).Subject);

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (stampCase == "null")
            {
                await dbContext.Database.ExecuteSqlAsync(
                    $"UPDATE \"AspNetUsers\" SET \"SecurityStamp\" = NULL WHERE \"Id\" = {userId}");
            }
            else
            {
                await dbContext.Database.ExecuteSqlAsync(
                    $"UPDATE \"AspNetUsers\" SET \"SecurityStamp\" = '' WHERE \"Id\" = {userId}");
            }
        }

        var response = await client.GetApi("/api/v1/tenants/current", tokens.AccessToken);

        await AssertUnauthorizedProblemAsync(response);
    }

    [Fact]
    public async Task AuthenticatedRequest_LoadsLiveTenantUserAndRoleInOneProjection()
    {
        var capture = new DbCommandCaptureInterceptor();
        await using var factory = Harness.CreateFactory(configureServices: services =>
        {
            services.AddSingleton(capture);
            services.AddDbContext<AppDbContext>((_, options) => options.AddInterceptors(capture));
        });
        using var client = factory.CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(
            client,
            "jwt-one-projection",
            "jwt-one-projection@acme.test");
        capture.Clear();

        var response = await client.GetApi("/api/v1/tenants/current", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var liveStateQueries = capture.SelectCommands
            .Where(command => command.Contains("\"AspNetUsers\"", StringComparison.Ordinal)
                && command.Contains("\"Tenants\"", StringComparison.Ordinal)
                && command.Contains("\"AspNetUserRoles\"", StringComparison.Ordinal)
                && command.Contains("\"AspNetRoles\"", StringComparison.Ordinal))
            .ToArray();
        var query = Assert.Single(liveStateQueries);
        Assert.Contains("\"SecurityStamp\"", query, StringComparison.Ordinal);
        Assert.Contains("\"EmailConfirmed\"", query, StringComparison.Ordinal);
    }

    private static string CreateDefectiveToken(string accessToken, string defect)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var original = handler.ReadJwtToken(accessToken);
        var claims = original.Claims
            .Where(claim => claim.Type is not (JwtRegisteredClaimNames.Iss
                or JwtRegisteredClaimNames.Aud
                or JwtRegisteredClaimNames.Exp
                or JwtRegisteredClaimNames.Nbf))
            .ToList();
        var issuer = original.Issuer;
        var audience = original.Audiences.Single();
        var notBefore = original.ValidFrom;
        var expires = original.ValidTo;
        var key = SigningKey;
        var algorithm = SecurityAlgorithms.HmacSha256;

        if (defect == "missing-exp")
        {
            var credentialsWithoutLifetime = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                algorithm);
            return handler.WriteToken(new JwtSecurityToken(
                issuer,
                audience,
                claims,
                notBefore: null,
                expires: null,
                signingCredentials: credentialsWithoutLifetime));
        }

        void ReplaceClaim(string type, string value)
        {
            claims.RemoveAll(claim => claim.Type == type);
            claims.Add(new Claim(type, value));
        }

        switch (defect)
        {
            case "wrong-issuer":
                issuer = "wrong-issuer";
                break;
            case "wrong-audience":
                audience = "wrong-audience";
                break;
            case "wrong-signature":
                key = "another-integration-signing-key-0123456789abcdef0123456789";
                break;
            case "wrong-algorithm":
                algorithm = SecurityAlgorithms.HmacSha384;
                break;
            case "expired":
                notBefore = DateTime.UtcNow.AddMinutes(-20);
                expires = DateTime.UtcNow.AddMinutes(-2);
                break;
            case "missing-sub":
                claims.RemoveAll(claim => claim.Type == AuthClaims.Subject);
                break;
            case "missing-tenant-id":
                claims.RemoveAll(claim => claim.Type == AuthClaims.TenantId);
                break;
            case "missing-tenant-slug":
                claims.RemoveAll(claim => claim.Type == AuthClaims.TenantSlug);
                break;
            case "missing-role":
                claims.RemoveAll(claim => claim.Type == AuthClaims.Role);
                break;
            case "missing-security-stamp":
                claims.RemoveAll(claim => claim.Type == AuthClaims.SecurityStamp);
                break;
            case "empty-security-stamp":
                ReplaceClaim(AuthClaims.SecurityStamp, string.Empty);
                break;
            case "tampered-security-stamp":
                ReplaceClaim(AuthClaims.SecurityStamp, "tampered-stamp");
                break;
            case "tenant-slug-mismatch":
                ReplaceClaim(AuthClaims.TenantSlug, "another-tenant");
                break;
            case "role-mismatch":
                ReplaceClaim(AuthClaims.Role, TenantRoles.Admin);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(defect), defect, "Unknown JWT defect.");
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            algorithm);
        var token = new JwtSecurityToken(issuer, audience, claims, notBefore, expires, credentials);
        return handler.WriteToken(token);
    }

    private static async Task AssertUnauthorizedProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal("unauthorized", problem.Extensions["code"]?.ToString());
    }
}
