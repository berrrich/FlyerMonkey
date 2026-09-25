using OpenAI.Images;

namespace FlyerMonkey.Api.Services;

public class ImageGenerationService
{
    private readonly string _apiKey;
    private readonly ImageClient _imageClient;
    public ImageGenerationService(IConfiguration configuration)
    {
        _apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "OpenAI API key is not configured.");
        _imageClient = new ImageClient(
    model: "gpt-image-1",
    apiKey: _apiKey);
    }

    public async Task<BinaryData> GenerateAsync(string prompt)
    {
        var result =
            await _imageClient.GenerateImageAsync(prompt);

        return result.Value.ImageBytes;
    }
}