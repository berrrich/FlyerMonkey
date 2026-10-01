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
    "barcode": "string",
    "price": "string",
    "regularPrice": "explicit previous/regular price only, otherwise empty string",
    "saving": "string",
    "unitPrice": "string",
"offerQuantity": null,
    "promotion": "string"
  }
]

Rules:
- Do not invent values.
- Use an empty string if a field is not visible.
- Keep productName focused on the core product name, not the full advertisement sentence.
- Prefer the printed flyer product description for productName when one is visible; use wording on the product packaging to supplement brand and variant.
- regularPrice must contain only an advertised previous/regular product price.
- Never put a unit price such as "per kg", "per litre", "per 100g" or similar into regularPrice; put that value in unitPrice instead.
- NEVER calculate or infer regularPrice from price and saving.
- A SAVE amount does not mean that a regularPrice was advertised.
- If the advertised offer requires purchasing a specific quantity, put that quantity in offerQuantity.
- Example: "ANY 2 FOR $10" means offerQuantity = 2 and price = "$10".
- If no purchase quantity is required, set offerQuantity to null.
- Put promotional wording such as "ANY 2 FOR $10", "2 FOR $8", or similar in promotion.
- Do not divide a multi-buy price into a single-item price unless a single-item price is explicitly advertised.
- If no previous/regular product price is explicitly advertised, leave regularPrice empty.
- Put flavour/type/style information in variant where possible.
- When a product description lists multiple types or flavours followed by
  "variety" or "varieties", put the listed types or flavours in variant.
- Do not use "variety" or "varieties" alone as the variant when the actual
  varieties are visible.
- Put visible size or quantity information in packSizeText, for example "200g", "2 Litre", "30 Pack".
- Use a simple grocery category such as Biscuits, Soft Drinks, Laundry, Produce, Meat, Dairy, Frozen, Snacks, Baby, Pantry, or similar.
- Only include barcode if it is actually visible.
When reference product packaging is visible:
- Preserve the approximate dominant packaging colours and overall product type
  so the generated image remains recognisable to a shopper.
- Do not attempt to make an exact replica of the original packaging.
- Keep the generated packaging visually clean and simplified.
- Do not invent packaging colours that substantially change the product's
  shelf appearance when the original colours are clearly visible.
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

        var result =
    products ?? new List<ExtractedProduct>();

        // 🐒 Strawberry operates here
        string databasePath =
            @"C:\Users\richa\source\repos\FlyerMonkey\DATA\FlyerMonkey.db";

        var duplicateService =
            new ProductDuplicateService(databasePath);

        await duplicateService.CheckAsync(result);

        return result;
    }
}