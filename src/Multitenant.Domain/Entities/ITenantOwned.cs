namespace Multitenant.Domain.Entities;

public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
