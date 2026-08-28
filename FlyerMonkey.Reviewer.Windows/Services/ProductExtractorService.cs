using FlyerMonkey.Reviewer.Windows.Models;
using OpenAI.Chat;
using PDFtoImage;
using System.IO;
using System.Text.Json;

namespace FlyerMonkey.Reviewer.Windows.Services;

public class ProductExtractorService
{
    private readonly ChatClient _client;

    public ProductExtractorService()
    {
        var apiKey =
            Environment.GetEnvironmentVariable(
                "FLYERMONKEY_OPENAI_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key is not configured.");
        }

        _client = new ChatClient(
            model: "gpt-4o-mini",
            apiKey: apiKey);
    }

    public async Task<List<ExtractedProduct>> ExtractProductsAsync(
        string pdfPath)
    {
        using var pdfStream = File.OpenRead(pdfPath);
        using var imageStream = new MemoryStream();

        Conversion.SavePng(
            imageStream,
            pdfStream,
            page: 0);

        var imageBytes = imageStream.ToArray();

        var messages = new List<ChatMessage>
        {
            new UserChatMessage(
                ChatMessageContentPart.CreateTextPart(
                    """
                    Extract every advertised product visible on this supermarket flyer page.

                    Return ONLY valid JSON in this exact shape:

                    [
                      {
                        "productName": "string",
                        "brand": "string",
                        "price": "string",
                        "unitPrice": "string",
                        "promotion": "string"
                      }
                    ]

                    Do not invent values.
                    Use an empty string if a field is not visible.
                    """),

                ChatMessageContentPart.CreateImagePart(
                    BinaryData.FromBytes(imageBytes),
                    "image/png")
            )
        };

        var response =
            await _client.CompleteChatAsync(messages);

        var raw =
            response.Value.Content[0].Text;

        var json =
            JsonResponseCleaner.Clean(raw);

        var products =
            JsonSerializer.Deserialize<List<ExtractedProduct>>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        return products ?? new List<ExtractedProduct>();
    }
}