namespace Multitenant.Domain.Entities;

public sealed class Project : ITenantOwned, IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<WorkItem> Tasks { get; set; } = [];
}
