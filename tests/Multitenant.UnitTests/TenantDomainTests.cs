using Multitenant.Application.Common.Security;
using Multitenant.Domain.Entities;

namespace Multitenant.UnitTests;

public sealed class TenantDomainTests
{
    public class TenantSlugTests
    {
        [Theory]
        [InlineData("acme", true)]
        [InlineData("ABC", true)]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
        [InlineData("ACME", true)]
        [InlineData("a", false)]
        [InlineData("ab", false)]
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
        [InlineData("PASSWORD12!AB", false)] // missing lowercase
        [InlineData("password12!ab", false)] // missing uppercase
        [InlineData("Password!abcd", false)] // missing digit
        [InlineData("Password123ab", false)] // no symbol
        [InlineData("Pass word1!a", false)] // whitespace
        [InlineData("Str0ng!aaaa", false)] // one character below minimum
        [InlineData("Password1!aa", true)] // exact minimum
        [InlineData("V3ryStrong!Pass", true)]
        public void ValidatesPasswordStrength(string password, bool expected)
        {
            Assert.Equal(expected, PasswordRules.IsValid(password));
        }

        [Fact]
        public void PasswordRules_ExactMinimumAndMaximumLengths_AreAccepted()
        {
            Assert.True(PasswordRules.IsValid("Password1!aa"));
            Assert.True(PasswordRules.IsValid("Aa1!" + new string('a', 124)));
        }

        [Fact]
        public void PasswordRules_AdjacentLengthsOutsideRange_AreRejected()
        {
            Assert.False(PasswordRules.IsValid("Str0ng!aaaa"));
            Assert.False(PasswordRules.IsValid("Aa1!" + new string('a', 125)));
        }
    }
}
