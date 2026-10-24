namespace Multitenant.Domain.Entities;

public sealed class Tenant
{
    private Tenant()
    {
    }

    private Tenant(Guid id, string slug, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Slug = slug;
        Name = name;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Slug { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public ICollection<AppUser> Users { get; private set; } = [];

    public ICollection<Project> Projects { get; private set; } = [];

    public static Tenant Create(string slug, string name, DateTimeOffset createdAtUtc)
    {
        var normalizedSlug = TenantSlug.Normalize(slug);
        TenantSlug.EnsureValid(normalizedSlug);

        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 2 or > 120)
        {
            throw new DomainException("Tenant name must be between 2 and 120 characters.");
        }

        return new Tenant(Guid.NewGuid(), normalizedSlug, normalizedName, createdAtUtc);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void Rename(string name)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 2 or > 120)
        {
            throw new DomainException("Tenant name must be between 2 and 120 characters.");
        }

        Name = normalizedName;
    }
}
