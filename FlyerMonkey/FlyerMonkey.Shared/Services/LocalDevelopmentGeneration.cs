using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

public interface ILocalDevelopmentGeneration
{
    bool Enabled { get; }
    Task<string> GenerateAsync(Guid requestId, Product product, string creativePrompt);
}

public sealed class LocalDevelopmentGenerationException(string message) : Exception(message);

/// <summary>One explicit attempt per entry. An uncertain outcome must never silently charge again.</summary>
public sealed class LocalDevelopmentGenerationState
{
    public string Status { get; private set; } = "Ready";
    public string? Image { get; private set; }
    public string? Error { get; private set; }
    public bool Attempted { get; private set; }
    public async Task GenerateAsync(ILocalDevelopmentGeneration service, Product product, string prompt)
    {
        if (Attempted || !service.Enabled) return;
        Attempted = true;
        Status = "Generating";
        try
        {
            Image = await service.GenerateAsync(Guid.NewGuid(), product, prompt);
            Status = "Success";
        }
        catch (Exception ex)
        {
            Status = "Failure";
            Error = "Generation or preview failed. Check the local API logs and your private artwork before starting another attempt; an image may already have been generated. Check OpenAI configuration, operator sign-in and local generation settings.";
            if (ex is LocalDevelopmentGenerationException) Error = ex.Message + " " + Error;
        }
    }
}
