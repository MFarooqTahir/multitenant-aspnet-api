using Multitenant.Application.Common;
using Multitenant.Application.Common.Security;
using Multitenant.Domain.Entities;

namespace Multitenant.UnitTests;

public sealed class TenantDomainTests
{
    public class TenantSlugTests
    {
        [Theory]
        [InlineData("acme", true)]
        [InlineData("ACME", true)]
        [InlineData("a", false)]
        [InlineData("acme inc", false)]
        [InlineData("ac--me", false)]
        [InlineData("-acme", false)]
        [InlineData("acme-", false)]
        public void ValidatesSlugs(string slug, bool expected) =>
            Assert.Equal(expected, Domain.Entities.TenantSlug.IsValid(slug));

        [Fact]
        public void NormalizesToLowercaseWithoutWhitespace()
        {
            Assert.Equal("acme", Domain.Entities.TenantSlug.Normalize("  AcMe "));
        }
    }

    public class TenantRolesTests
    {
        [Fact]
        public void NormalizesKnownRoles()
        {
            Assert.Equal("Admin", TenantRoles.Normalize("ADMIN"));
            Assert.Equal("Member", TenantRoles.Normalize("member"));
        }

        [Fact]
        public void RejectsUnknownRoles()
        {
            Assert.Throws<DomainException>(() => TenantRoles.Normalize("OwnerOfTheWorld"));
        }

        [Fact]
        public void RanksOwnerHighest()
        {
            Assert.True(TenantRoles.Rank(TenantRoles.Owner) > TenantRoles.Rank(TenantRoles.Admin));
            Assert.True(TenantRoles.Rank(TenantRoles.Admin) > TenantRoles.Rank(TenantRoles.Member));
        }
    }

    public class PasswordRulesTests
    {
        [Theory]
        [InlineData("Password1!", false)] // missing lowercase
        [InlineData("password1!", false)] // missing uppercase
        [InlineData("PASSWORD1!", false)] // missing lowercase
        [InlineData("Password!1", false)] // missing digit
        [InlineData("Password123", false)] // no symbol
        [InlineData("P w1!aaaa", false)] // whitespace
        [InlineData("Short1!aaaa", false)]
        [InlineData("V3ryStrong!Pass", true)]
        public void ValidatesPasswordStrength(string password, bool expected)
        {
            Assert.Equal(expected, PasswordRules.IsValid(password));
        }

        [Fact]
        public void RequiresMinimumLength()
        {
            Assert.False(PasswordRules.IsValid("Str0ng!Pass"));
        }
    }
}
