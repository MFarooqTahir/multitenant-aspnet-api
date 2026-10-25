namespace Multitenant.Domain.Entities;

/// <summary>
/// A per-tenant override for one <see cref="TenantFeatureKeys"/> entry. A missing row
/// means "enabled": flags are kill switches a tenant opts out of, not opt-ins, so a
/// tenant created before a feature existed keeps behaving as it always did.
/// </summary>
public sealed class TenantFeature : ITenantOwned
{
    private TenantFeature()
    {
    }

    private TenantFeature(Guid tenantId, string key, bool isEnabled, DateTimeOffset updatedAtUtc)
    {
        TenantId = tenantId;
        Key = key;
        IsEnabled = isEnabled;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid TenantId { get; set; }

    public string Key { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static TenantFeature Create(
        Guid tenantId,
        string key,
        bool isEnabled,
        DateTimeOffset updatedAtUtc)
    {
        var normalizedKey = TenantFeatureKeys.Normalize(key);
        if (!TenantFeatureKeys.IsValid(normalizedKey))
        {
            throw new DomainException($"Unknown tenant feature '{key}'.");
        }

        return new TenantFeature(tenantId, normalizedKey, isEnabled, updatedAtUtc);
    }

    public void Set(bool isEnabled, DateTimeOffset updatedAtUtc)
    {
        IsEnabled = isEnabled;
        UpdatedAtUtc = updatedAtUtc;
    }
}
