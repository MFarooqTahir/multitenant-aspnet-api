namespace Multitenant.Domain.Entities;

public interface IHasTimestamps
{
    DateTimeOffset CreatedAtUtc { get; set; }

    DateTimeOffset UpdatedAtUtc { get; set; }
}
