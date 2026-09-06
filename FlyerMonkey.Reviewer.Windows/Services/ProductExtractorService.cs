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
    "variant": "string",
    "packSizeText": "string",
    "category": "string",
    "barcode": "string"
  }
]

Rules:
- Do not invent values.
- Use an empty string if a field is not visible.
- Keep productName focused on the core product name, not the full advertisement sentence.
- Put flavour/type/style information in variant where possible.
- Put visible size or quantity information in packSizeText, for example "200g", "2 Litre", "30 Pack".
- Use a simple grocery category such as Biscuits, Soft Drinks, Laundry, Produce, Meat, Dairy, Frozen, Snacks, Baby, Pantry, or similar.
- Only include barcode if it is actually visible.
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