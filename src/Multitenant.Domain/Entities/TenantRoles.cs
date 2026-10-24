namespace Multitenant.Domain.Entities;

public static class TenantRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";

    public static readonly IReadOnlyList<string> All = [Owner, Admin, Member];

    public static bool IsValid(string? role) =>
        role is not null && All.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string role)
    {
        var match = All.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new DomainException($"Unknown role '{role}'.");
    }

    public static int Rank(string role) => Normalize(role) switch
    {
        Owner => 3,
        Admin => 2,
        _ => 1,
    };
}
