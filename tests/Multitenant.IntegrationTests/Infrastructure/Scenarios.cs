using Multitenant.Application.Contracts;

namespace Multitenant.IntegrationTests.Infrastructure;

/// <summary>
/// Reusable API scenarios shared by the integration test classes.
/// </summary>
public static class Scenarios
{
    public const string Password = "Password1!aaaa";

    public static async Task<TenantResponse> OnboardAsync(
        HttpClient client,
        string slug,
        string name,
        string ownerEmail,
        string password = Password)
    {
        var response = await client.PostApi(
            "/api/v1/tenants/onboard",
            new OnboardTenantRequest(slug, name, ownerEmail, "Owner", password));

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<TenantResponse>();
    }

    public static async Task<AuthTokens> LoginAsync(
        HttpClient client,
        string tenantSlug,
        string email,
        string password = Password)
    {
        var response = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest(tenantSlug, email, password));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<AuthTokens>();
    }

    public static async Task<AuthTokens> OnboardAndLoginAsync(
        HttpClient client,
        string slug,
        string ownerEmail)
    {
        await OnboardAsync(client, slug, $"Tenant {slug}", ownerEmail, Password);
        return await LoginAsync(client, slug, ownerEmail, Password);
    }

    public static async Task<ProjectResponse> CreateProjectAsync(
        HttpClient client,
        string accessToken,
        string name)
    {
        var response = await client.PostApi(
            "/api/v1/projects",
            new CreateProjectRequest(name, null, Domain.Entities.ProjectStatus.Active),
            accessToken);

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ProjectResponse>();
    }
}
