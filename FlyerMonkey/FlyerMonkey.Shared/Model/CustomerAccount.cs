namespace FlyerMonkey.Shared.Model;

public sealed record CustomerAccount(Guid CustomerId, string Status, bool CraterEnabled);

// Presentation only. Protected operations always revalidate at the API.
public sealed record CustomerSessionState(bool Enabled, bool SignedIn);
