using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Contracts.Users;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class UserQueryTests : IntegrationTestBase
{
    public UserQueryTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task UserList_FiltersSearchRoleAndActiveState()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(
            client,
            "user-query-filters",
            "User Query Filters",
            "user-query-owner@acme.test");
        var owner = await Scenarios.LoginAsync(client, tenant.Slug, "user-query-owner@acme.test");
        var alpha = await Scenarios.CreateUserAsync(
            client,
            owner.AccessToken,
            "user-query-alpha@acme.test",
            TenantRoles.Member,
            "Alpha Person");
        var beta = await Scenarios.CreateUserAsync(
            client,
            owner.AccessToken,
            "user-query-beta@acme.test",
            TenantRoles.Admin,
            "Beta Person");
        var gamma = await Scenarios.CreateUserAsync(
            client,
            owner.AccessToken,
            "user-query-gamma@acme.test",
            TenantRoles.Member,
            "Gamma Person");

        using (var scope = Harness.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlAsync(
                $"UPDATE \"AspNetUsers\" SET \"IsActive\" = FALSE WHERE \"Id\" = {gamma.Id}");
        }

        var search = await ReadPageAsync(client, owner.AccessToken, "/api/v1/users?search=ALPHA");
        Assert.Single(search.Items);
        Assert.Equal(alpha.Id, search.Items[0].Id);

        var admins = await ReadPageAsync(client, owner.AccessToken, "/api/v1/users?role=Admin");
        Assert.Single(admins.Items);
        Assert.Equal(beta.Id, admins.Items[0].Id);

        var active = await ReadPageAsync(client, owner.AccessToken, "/api/v1/users?isActive=true");
        Assert.Equal(3, active.TotalCount);
        Assert.DoesNotContain(active.Items, user => user.Id == gamma.Id);

        var inactive = await ReadPageAsync(client, owner.AccessToken, "/api/v1/users?isActive=false");
        Assert.Single(inactive.Items);
        Assert.Equal(gamma.Id, inactive.Items[0].Id);
        Assert.False(inactive.Items[0].IsActive);
    }

    [Fact]
    public async Task UserList_SortsAndPagesWithExactMetadata()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(
            client,
            "user-query-paging",
            "User Query Paging",
            "user-page-owner@acme.test");
        var owner = await Scenarios.LoginAsync(client, tenant.Slug, "user-page-owner@acme.test");
        await Scenarios.CreateUserAsync(client, owner.AccessToken, "user-page-alpha@acme.test");
        await Scenarios.CreateUserAsync(client, owner.AccessToken, "user-page-beta@acme.test", TenantRoles.Admin);
        await Scenarios.CreateUserAsync(client, owner.AccessToken, "user-page-gamma@acme.test");

        var all = await ReadPageAsync(client, owner.AccessToken, "/api/v1/users?sort=email&pageSize=100");
        var expectedEmails = all.Items.Select(user => user.Email).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedEmails, all.Items.Select(user => user.Email));
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(1, all.Page);
        Assert.Equal(100, all.PageSize);
        Assert.Equal(1, all.TotalPages);

        var descending = await ReadPageAsync(client, owner.AccessToken, "/api/v1/users?sort=-email&pageSize=100");
        Assert.Equal(expectedEmails.Reverse(), descending.Items.Select(user => user.Email));

        var firstPage = await ReadPageAsync(client, owner.AccessToken, "/api/v1/users?sort=email&page=1&pageSize=2");
        var secondPage = await ReadPageAsync(client, owner.AccessToken, "/api/v1/users?sort=email&page=2&pageSize=2");
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(expectedEmails.Take(2), firstPage.Items.Select(user => user.Email));
        Assert.Equal(expectedEmails.Skip(2), secondPage.Items.Select(user => user.Email));
    }

    private static async Task<PagedResult<UserResponse>> ReadPageAsync(
        HttpClient client,
        string accessToken,
        string path)
    {
        var response = await client.GetApi(path, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResult<UserResponse>>();
    }
}
