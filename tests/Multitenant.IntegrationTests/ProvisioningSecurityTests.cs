using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Abstractions.Identity;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Errors.Exceptions;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class ProvisioningSecurityTests : IntegrationTestBase
{
    public ProvisioningSecurityTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task UserProvisioningService_RejectsWeakPasswordWithoutCreatingUser()
    {
        using var client = CreateClient();
        var tenant = await Scenarios.OnboardAsync(
            client,
            "provisioning-weak-password",
            "Provisioning Weak Password",
            "provisioning-owner@acme.test");

        using var scope = Harness.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenant.Id, tenant.Slug);
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var countBefore = await dbContext.Users.CountAsync();
        var service = scope.ServiceProvider.GetRequiredService<IUserProvisioningService>();

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(
            tenant.Id,
            tenant.Slug,
            "provisioning-weak@acme.test",
            "Weak Password User",
            "weak",
            "Member",
            CancellationToken.None));

        Assert.Equal("invalid_password", exception.Code);
        Assert.Equal(countBefore, await dbContext.Users.CountAsync());
    }

    [Fact]
    public void DemoSeeding_RejectsWeakPasswordAtStartup()
    {
        using var factory = Harness.CreateFactory(configuration =>
        {
            configuration["Database:SeedDemoData"] = "true";
            configuration["Demo:Password"] = "weak";
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Demo:Password does not satisfy the password policy", exception.Message, StringComparison.Ordinal);
        Assert.Contains("12-128", exception.Message, StringComparison.Ordinal);
    }
}
