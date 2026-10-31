using OpenAI.Images;

using OpenAI;
using System.ClientModel;

namespace FlyerMonkey.Api.Services;

public class ImageGenerationService
{
    public const string DefaultModel = "gpt-image-1.5";
    public string Model { get; }
    private readonly ImageClient _imageClient;
    public ImageGenerationService(IConfiguration configuration, OpenAIClientOptions? clientOptions = null)
    {
        Model = configuration["OpenAI:ImageModel"]?.Trim() ?? DefaultModel;
        if (string.IsNullOrWhiteSpace(Model))
            throw new InvalidOperationException("OpenAI:ImageModel must not be blank.");
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API key is not configured.");
        _imageClient = new ImageClient(Model, new ApiKeyCredential(apiKey), clientOptions ?? new OpenAIClientOptions());
    }

    public async Task<BinaryData> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
#pragma warning disable OPENAI001
        var options = new ImageGenerationOptions
        {
            Size = GeneratedImageSize.W1024xH1024,
            OutputFileFormat = GeneratedImageFileFormat.Jpeg,
            OutputCompressionFactor = 80
        };
#pragma warning restore OPENAI001
        var result =
            await _imageClient.GenerateImageAsync(
                prompt,
                options,
                cancellationToken);

        return result.Value.ImageBytes;
    }
}
