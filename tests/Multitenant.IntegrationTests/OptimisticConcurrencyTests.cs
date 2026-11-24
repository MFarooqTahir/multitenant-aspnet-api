using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts.Projects;
using Multitenant.Application.Contracts.Tasks;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;
using Multitenant.Infrastructure.Persistence;
using Multitenant.Infrastructure.Tenancy;
using Npgsql;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class OptimisticConcurrencyTests : IntegrationTestBase
{
    public OptimisticConcurrencyTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task ProjectUpdate_StaleContextThrowsConcurrencyException()
    {
        await using var factory = Harness.CreateFactory();
        using var client = factory.CreateClient();
        var (tenant, tokens, project) = await CreateProjectAsync(client, "concurrency-project-update");
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var first = CreateContext(firstScope, tenant);
        var second = CreateContext(secondScope, tenant);
        var firstProject = await first.Projects.SingleAsync(item => item.Id == project.Id);
        var secondProject = await second.Projects.SingleAsync(item => item.Id == project.Id);

        firstProject.Name = "First writer";
        await first.SaveChangesAsync();

        secondProject.Name = "Stale writer";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        var persisted = await (await client.GetApi($"/api/v1/projects/{project.Id}", tokens.AccessToken)).ReadAsync<ProjectResponse>();
        Assert.Equal("First writer", persisted.Name);
    }

    [Fact]
    public async Task ProjectDelete_StaleContextThrowsConcurrencyException()
    {
        await using var factory = Harness.CreateFactory();
        using var client = factory.CreateClient();
        var (tenant, _, project) = await CreateProjectAsync(client, "concurrency-project-delete");
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var first = CreateContext(firstScope, tenant);
        var second = CreateContext(secondScope, tenant);
        var firstProject = await first.Projects.SingleAsync(item => item.Id == project.Id);
        var secondProject = await second.Projects.SingleAsync(item => item.Id == project.Id);

        first.Remove(firstProject);
        await first.SaveChangesAsync();

        second.Remove(secondProject);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task TaskUpdate_StaleContextThrowsConcurrencyException()
    {
        await using var factory = Harness.CreateFactory();
        using var client = factory.CreateClient();
        var (tenant, tokens, project) = await CreateProjectAsync(client, "concurrency-task-update");
        var task = await CreateTaskAsync(client, tokens.AccessToken, project.Id);
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var first = CreateContext(firstScope, tenant);
        var second = CreateContext(secondScope, tenant);
        var firstTask = await first.WorkItems.SingleAsync(item => item.Id == task.Id);
        var secondTask = await second.WorkItems.SingleAsync(item => item.Id == task.Id);

        firstTask.Title = "First task writer";
        await first.SaveChangesAsync();

        secondTask.Title = "Stale task writer";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        var persisted = await (await client.GetApi($"/api/v1/tasks/{task.Id}", tokens.AccessToken)).ReadAsync<TaskResponse>();
        Assert.Equal("First task writer", persisted.Title);
    }

    [Fact]
    public async Task TaskDelete_StaleContextThrowsConcurrencyException()
    {
        await using var factory = Harness.CreateFactory();
        using var client = factory.CreateClient();
        var (tenant, tokens, project) = await CreateProjectAsync(client, "concurrency-task-delete");
        var task = await CreateTaskAsync(client, tokens.AccessToken, project.Id);
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var first = CreateContext(firstScope, tenant);
        var second = CreateContext(secondScope, tenant);
        var firstTask = await first.WorkItems.SingleAsync(item => item.Id == task.Id);
        var secondTask = await second.WorkItems.SingleAsync(item => item.Id == task.Id);

        first.Remove(firstTask);
        await first.SaveChangesAsync();

        second.Remove(secondTask);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task FreshDatabase_AllMigrationsApplyWithoutPendingChanges()
    {
        var databaseName = $"migration_test_{Guid.NewGuid():N}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(Harness.ConnectionString) { Database = "postgres" };
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(Harness.ConnectionString) { Database = databaseName };
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ConnectionString).Options;
            await using var context = new AppDbContext(options, new TenantContext());

            await context.Database.MigrateAsync();

            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.True(await context.Database.CanConnectAsync());
            var project = context.Model.FindEntityType(typeof(Project))!;
            var task = context.Model.FindEntityType(typeof(WorkItem))!;
            Assert.True(project.FindProperty("xmin")!.IsConcurrencyToken);
            Assert.True(task.FindProperty("xmin")!.IsConcurrencyToken);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<(TenantResponse Tenant, Multitenant.Application.Contracts.Auth.AuthTokens Tokens, ProjectResponse Project)> CreateProjectAsync(
        HttpClient client,
        string slug)
    {
        var tenant = await Scenarios.OnboardAsync(client, slug, slug, $"{slug}@acme.test");
        var tokens = await Scenarios.LoginAsync(client, tenant.Slug, $"{slug}@acme.test");
        var project = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, $"{slug} project");
        return (tenant, tokens, project);
    }

    private static async Task<TaskResponse> CreateTaskAsync(HttpClient client, string accessToken, Guid projectId)
    {
        var response = await client.PostApi(
            $"/api/v1/projects/{projectId}/tasks",
            new CreateTaskRequest("Concurrency task", null),
            accessToken);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<TaskResponse>();
    }

    private static AppDbContext CreateContext(IServiceScope scope, TenantResponse tenant)
    {
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenant.Id, tenant.Slug);
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }
}
