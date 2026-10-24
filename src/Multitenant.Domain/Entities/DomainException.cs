namespace Multitenant.Domain.Entities;

public sealed class DomainException(string message) : Exception(message);
