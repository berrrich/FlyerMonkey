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

    /*public async Task<BinaryData> GenerateAsync(string prompt)
    {
        var result =
            await _imageClient.GenerateImageAsync(prompt);

        return result.Value.ImageBytes;
    }*/
    public async Task<BinaryData> GenerateAsync(string prompt)
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
                options);

        await File.WriteAllBytesAsync(
            @"C:\Users\richa\source\repos\FlyerMonkey\DATA\ProductImages\flyermonkey-test2Cadbury.jpg",
            result.Value.ImageBytes.ToArray());

        return result.Value.ImageBytes;
    }
}