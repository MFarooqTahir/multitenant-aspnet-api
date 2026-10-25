using Multitenant.Domain.Entities;

namespace Multitenant.UnitTests;

public sealed class TenantFeatureTests
{
    [Fact]
    public void CreatesAWhitelistedFeature()
    {
        var now = DateTimeOffset.UtcNow;
        var feature = TenantFeature.Create(Guid.NewGuid(), " TASK_ASSIGNMENT ", false, now);

        Assert.Equal(TenantFeatureKeys.TaskAssignment, feature.Key);
        Assert.False(feature.IsEnabled);
        Assert.Equal(now, feature.UpdatedAtUtc);
    }

    [Fact]
    public void RejectsUnknownFeatureKeys()
    {
        Assert.Throws<DomainException>(
            () => TenantFeature.Create(Guid.NewGuid(), "not_a_feature", true, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SetUpdatesTheValueAndTimestamp()
    {
        var created = DateTimeOffset.UtcNow.AddMinutes(-1);
        var feature = TenantFeature.Create(Guid.NewGuid(), TenantFeatureKeys.TaskAssignment, true, created);
        var changed = DateTimeOffset.UtcNow;

        feature.Set(false, changed);

        Assert.False(feature.IsEnabled);
        Assert.Equal(changed, feature.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("task_assignment", true)]
    [InlineData("TASK_ASSIGNMENT", true)]
    [InlineData(" task_assignment ", true)]
    [InlineData("task-assignment", false)]
    [InlineData("", false)]
    public void ValidatesKnownKeys(string key, bool expected) =>
        Assert.Equal(expected, TenantFeatureKeys.IsValid(key));
}
