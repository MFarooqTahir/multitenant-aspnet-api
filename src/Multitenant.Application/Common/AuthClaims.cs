namespace Multitenant.Application.Common;

/// <summary>
/// Claim names used by the API. They are deliberately short and stable because
/// they end up inside access tokens.
/// </summary>
public static class AuthClaims
{
    public const string Subject = "sub";
    public const string TenantId = "tenant_id";
    public const string TenantSlug = "tenant_slug";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string SecurityStamp = "sstamp";
    public const string Purpose = "purpose";
    public const string TwoFactorPurpose = "2fa";
}
