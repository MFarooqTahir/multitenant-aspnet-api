using Multitenant.Application.Common;

namespace Multitenant.UnitTests;

public sealed class UserNameTests
{
    [Fact]
    public void NamespacesUserNamesByTenantSlug()
    {
        Assert.Equal("acme:owner@acme.com", UserNames.For("Acme", "Owner@Acme.com"));
    }
}
