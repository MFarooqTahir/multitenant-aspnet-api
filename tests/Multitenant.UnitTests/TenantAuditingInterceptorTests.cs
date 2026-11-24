using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Errors.Exceptions;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;
using Multitenant.Infrastructure.Tenancy;
using Multitenant.UnitTests.Infrastructure;

namespace Multitenant.UnitTests;

public sealed class TenantAuditingInterceptorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SavingChanges_EmptyTenantIdOnInsert_StampsResolvedTenant()
    {
        var tenantContext = ResolvedTenant();
        var interceptor = new TenantAuditingInterceptor(tenantContext, new FakeTimeProvider(Now));
        var (context, probe) = CreateContext(tenantContext, interceptor);
        using (context)
        {
            var project = new Project { Name = "New project" };
            context.Projects.Add(project);

            Save(context, probe);

            Assert.Equal(TenantId, project.TenantId);
            Assert.Equal(Now, project.CreatedAtUtc);
            Assert.Equal(Now, project.UpdatedAtUtc);
        }
    }

    [Theory]
    [InlineData("foreign-add")]
    [InlineData("foreign-update")]
    [InlineData("foreign-delete")]
    public void SavingChanges_ForeignTenantAddUpdateOrDelete_ThrowsTenantMismatch(string operation)
    {
        var tenantContext = ResolvedTenant();
        var interceptor = new TenantAuditingInterceptor(tenantContext, new FakeTimeProvider(Now));
        var (context, probe) = CreateContext(tenantContext, interceptor);
        using (context)
        {
            var project = new Project { Id = Guid.NewGuid(), TenantId = OtherTenantId, Name = "Foreign" };

            switch (operation)
            {
                case "foreign-add":
                    context.Projects.Add(project);
                    break;
                case "foreign-update":
                    context.Attach(project);
                    context.Entry(project).State = EntityState.Modified;
                    break;
                default:
                    context.Attach(project);
                    context.Entry(project).State = EntityState.Deleted;
                    break;
            }

            var exception = Assert.Throws<ForbiddenException>(() => Save(context, probe));

            Assert.Equal("tenant_mismatch", exception.Code);
        }
    }

    [Fact]
    public void SavingChanges_WithoutResolvedTenant_Throws()
    {
        var tenantContext = new TenantContext();
        var interceptor = new TenantAuditingInterceptor(tenantContext, new FakeTimeProvider(Now));
        var (context, probe) = CreateContext(tenantContext, interceptor);
        using (context)
        {
            context.Projects.Add(new Project { Name = "Unresolved" });

            var exception = Assert.Throws<InvalidOperationException>(() => Save(context, probe));

            Assert.Contains("without a resolved tenant context", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SavingChanges_TenantIdChangedAfterInsert_ThrowsTenantMismatch()
    {
        var tenantContext = ResolvedTenant();
        var interceptor = new TenantAuditingInterceptor(tenantContext, new FakeTimeProvider(Now));
        var (context, probe) = CreateContext(tenantContext, interceptor);
        using (context)
        {
            var project = new Project { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Owned" };
            context.Attach(project);
            context.Entry(project).State = EntityState.Modified;
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            project.TenantId = OtherTenantId;

            var exception = Assert.Throws<ForbiddenException>(() => Save(context, probe));

            Assert.Equal("tenant_mismatch", exception.Code);
        }
    }

    [Fact]
    public void SavingChanges_AddAndUpdate_SetsExactTimestamps()
    {
        var tenantContext = ResolvedTenant();
        var timeProvider = new FakeTimeProvider(Now);
        var interceptor = new TenantAuditingInterceptor(tenantContext, timeProvider);
        var (context, probe) = CreateContext(tenantContext, interceptor);
        using (context)
        {
            var added = new Project { Name = "Added" };
            context.Projects.Add(added);

            Save(context, probe);
            context.Entry(added).State = EntityState.Detached;

            var modified = new Project
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Name = "Original",
                CreatedAtUtc = Now.AddDays(-1),
                UpdatedAtUtc = Now.AddDays(-1),
            };
            context.Attach(modified);
            context.Entry(modified).State = EntityState.Modified;
            timeProvider.Advance(TimeSpan.FromMinutes(5));
            modified.Name = "Changed";

            Save(context, probe);

            Assert.Equal(Now, added.CreatedAtUtc);
            Assert.Equal(Now, added.UpdatedAtUtc);
            Assert.Equal(Now.AddDays(-1), modified.CreatedAtUtc);
            Assert.Equal(Now.AddMinutes(5), modified.UpdatedAtUtc);
        }
    }

    private static TenantContext ResolvedTenant()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(TenantId, "tenant");
        return tenantContext;
    }

    private static (AppDbContext Context, SavingChangesProbe Probe) CreateContext(
        ITenantContext tenantContext,
        TenantAuditingInterceptor interceptor)
    {
        var probe = new SavingChangesProbe(interceptor);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=interceptor-tests;Username=test;Password=test")
            .AddInterceptors(probe)
            .Options;

        return (new AppDbContext(options, tenantContext), probe);
    }

    private static void Save(AppDbContext context, SavingChangesProbe probe)
    {
        var stopBeforeDatabase = new InvalidOperationException("Stop before opening a database connection.");
        probe.StopAfterTarget = stopBeforeDatabase;

        try
        {
            context.SaveChanges();
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, stopBeforeDatabase))
        {
        }
    }

    private sealed class SavingChangesProbe(TenantAuditingInterceptor target) : SaveChangesInterceptor
    {
        public Exception? StopAfterTarget { get; set; }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            var intercepted = target.SavingChanges(eventData, result);
            if (StopAfterTarget is not null)
            {
                throw StopAfterTarget;
            }

            return intercepted;
        }
    }
}
