using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

/// <summary>
/// Base class for tests in the integration collection. Each test class takes the
/// collection fixture in its constructor and assigns <see cref="Harness"/>.
/// </summary>
public abstract class IntegrationTestBase
{
    protected IntegrationHarness Harness { get; set; } = null!;

    protected HttpClient CreateClient() => Harness.Factory.CreateClient();
}
