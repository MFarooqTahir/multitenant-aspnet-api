namespace Multitenant.IntegrationTests.Infrastructure;

/// <summary>A freshly onboarded tenant plus the owner email used to create it.</summary>
public sealed record OnboardedTenant(string Slug, string OwnerEmail);
