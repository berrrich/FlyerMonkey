namespace FlyerMonkey.Shared.Model;

// Resolved from the linked User, never from email or client-supplied role claims.
public sealed record CustomerGenerationAccess(Guid CustomerId, Guid TenantId, Guid ObjectId,
    string UserStatus, IReadOnlyCollection<string> Roles)
{
    public bool Allows(Guid customer, Guid tenant, Guid subject) =>
        CustomerId == customer && TenantId == tenant && ObjectId == subject &&
        UserStatus == "Active" && Roles.Contains("Customer", StringComparer.Ordinal) &&
        Roles.Contains("Crater Tester", StringComparer.Ordinal);
}
