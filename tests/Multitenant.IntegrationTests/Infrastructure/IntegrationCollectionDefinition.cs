namespace Multitenant.IntegrationTests.Infrastructure;

/// <summary>Binds <see cref="IntegrationHarness"/> to the xUnit collection.</summary>
[CollectionDefinition(IntegrationHarness.CollectionName)]
public sealed class IntegrationCollectionDefinition : ICollectionFixture<IntegrationHarness>;
