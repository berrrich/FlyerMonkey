namespace FlyerMonkey.Shared.Model;

// Public status contains no customer or external identity identifiers.
public sealed record CustomerCraterAttempt(string State, bool CanGenerate, bool HasImage);
