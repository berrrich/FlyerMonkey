using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using OffersComponent = FlyerMonkey.Web.Components.Pages.WebOffers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.DataProtection;

var fixture = new Fixture();
if (args.Contains("--serve-api-fixture"))
{
    fixture.Offers = System.Text.Json.JsonSerializer.Deserialize<List<OfferSummary>>(
        await File.ReadAllTextAsync(".artifacts/mission010a-visual/api-offers.json"),
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    var builder = WebApplication.CreateBuilder(Array.Empty<string>());
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
    builder.WebHost.UseUrls("http://127.0.0.1:8842");
    builder.WebHost.UseStaticWebAssets();
    builder.Services.AddSingleton<IOfferService>(fixture);
    builder.Services.AddRazorComponents().AddInteractiveServerComponents();
    var app = builder.Build();
    app.UseAntiforgery();
    app.MapStaticAssets();
    app.MapGet("/_content/FlyerMonkey.Shared/images/product-placeholder.png", () => Results.File(
        Path.Combine(Directory.GetCurrentDirectory(), "FlyerMonkey/FlyerMonkey.Shared/wwwroot/images/product-placeholder.png"), "image/png"));
    app.MapRazorComponents<WebOffers.Checks.VisualHost>().AddInteractiveServerRenderMode();
    await app.RunAsync();
    return;
}
using var services = new ServiceCollection().AddLogging().AddSingleton<IOfferService>(fixture).BuildServiceProvider();
await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
async Task<string> Render() => await renderer.Dispatcher.InvokeAsync(async () =>
    (await renderer.RenderComponentAsync<OffersComponent>(ParameterView.Empty)).ToHtmlString());
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
fixture.Offers = [
    new() { OfferID = 3, ProductID = 33, ProductName = "Coles apples", RetailerName = "Coles", AdvertisedPrice = 3.50m, PromoText = "Half price", UnitPriceText = "$1.75 per kg" },
    new() { OfferID = 1, ProductID = 11, ProductName = "Woolies bananas", RetailerName = "Woolworths" },
    new() { OfferID = 2, ProductName = "Woolies pears", RetailerName = "Woolworths" },
    new() { OfferID = 4, ProductName = "Other fruit", RetailerName = "Independent" }
];
var html = await Render();
if (args.Contains("--visual-fixture") || args.Contains("--api-fixture"))
{
    fixture.Offers = Enumerable.Range(0, 9).Select(i => new OfferSummary
    {
        OfferID = i + 1, ProductID = i % 2 == 0 ? 33 : 0,
        ProductName = i % 3 == 0 ? "Extra long product name with a family size multipack and a delicious seasonal flavour that wraps onto several lines" : "Fresh seasonal favourites",
        RetailerName = i < 5 ? "Woolworths" : i < 8 ? "Coles" : "Independent", PackSizeText = "500g", Category = "Groceries",
        AdvertisedPrice = i % 2 == 0 ? 12.50m : 3.50m,
        PromoText = new[] { "ea", "Half price", "40% off", "" }[i % 4],
        UnitPriceText = i % 3 == 0 ? "$2.50 per 100g with a longer unit description to exercise wrapping" : null
    }).ToList();
    if (args.Contains("--api-fixture"))
        fixture.Offers = System.Text.Json.JsonSerializer.Deserialize<List<OfferSummary>>(
            await File.ReadAllTextAsync(".artifacts/mission010a-visual/api-offers.json"),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    html = await Render();
    var rootPath = Directory.GetCurrentDirectory();
    var css = string.Join("\n", new[] {
        "FlyerMonkey/FlyerMonkey.Shared/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css",
        "FlyerMonkey/FlyerMonkey.Shared/wwwroot/app.css",
        "FlyerMonkey/FlyerMonkey.Web/wwwroot/css/offers.css",
        "FlyerMonkey/FlyerMonkey.Web/obj/Debug/net10.0/scopedcss/Components/Pages/WebOffers.razor.rz.scp.css"
    }.Select(path => File.ReadAllText(Path.Combine(rootPath, path))));
    Directory.CreateDirectory(".artifacts/mission010a-visual");
    await File.WriteAllTextAsync(".artifacts/mission010a-visual/expected.json",
        System.Text.Json.JsonSerializer.Serialize(fixture.Offers.Select(o => o.OfferID)));
    var renderedCount = System.Text.RegularExpressions.Regex.Matches(html, "class=\"deal\"").Count;
    Check(renderedCount == fixture.Offers.Count && fixture.Offers.All(o => html.Contains($"href=\"/offers/{o.OfferID}\"")), "Every returned offer renders with its detail link");
    Console.WriteLine($"Returned: {fixture.Offers.Count}; Woolworths: {fixture.Offers.Count(o => o.RetailerName.Contains("Woolworths", StringComparison.OrdinalIgnoreCase))}; Coles: {fixture.Offers.Count(o => o.RetailerName.Contains("Coles", StringComparison.OrdinalIgnoreCase))}; Rendered: {renderedCount}");
    await File.WriteAllTextAsync(".artifacts/mission010a-visual/offers.html",
        "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><style>" + css +
        "</style></head><body><main style='padding:32px'><nav style='margin-bottom:24px'>FlyerMonkey Â· Offers</nav>" + html + "</main></body></html>");
    Console.WriteLine("Exported actual WebOffers component with production CSS for browser verification.");
    return;
}
Check(html.IndexOf("Woolies bananas") < html.IndexOf("Woolies pears") && html.IndexOf("Woolies pears") < html.IndexOf("Coles apples"), "Retailer grouping preserves API order within each retailer");
Check(html.Contains("$3.50") && html.Contains("Half price"), "API price and promotion render");
Check(html.Contains("/offers/3") && html.Contains(PublicApiClientRegistration.ProductImageUrl(33)), "Existing detail links and product image API retained");
Check(html.Contains("product-placeholder.png") && html.Contains("Other fruit"), "Missing images have fallback and other retailers remain visible");
Check(System.Text.RegularExpressions.Regex.IsMatch(html,
    "class=\"price-row\"[^>]*>\\s*<div class=\"price\"[^>]*>\\$3.50</div>\\s*<p class=\"promotion\"[^>]*>Half price</p>\\s*</div>"),
    "Promotion follows price inside the same row");
Check(html.Contains("$1.75 per kg") && System.Text.RegularExpressions.Regex.Matches(html, "class=\"unit-price\"").Count == 4,
    "Every card reserves a unit-price row, including missing values");
fixture.Offers[1].ProductName = "A long product name with multiple words and an extra long variant description that must wrap without truncation";
fixture.Offers.Add(new() { OfferID = 5, ProductName = "Third Woolworths offer", RetailerName = "Woolworths", PromoText = "A longer promotion badge that can wrap" });
html = await Render();
Check(html.Contains($"title=\"{fixture.Offers[1].ProductName}\"") && html.Contains($"aria-label=\"{fixture.Offers[1].ProductName}\""),
    "Full product names remain available in title and accessible text");
Check(!html.Contains("retailer-name") && System.Text.RegularExpressions.Regex.Matches(html, "class=\"pack-category\"").Count == 5,
    "Retailer text removed and all pack/category slots retained");
Check(html.Contains("Choose retailer") && html.Contains("<option value=\"Coles\""), "Phone selector includes both catalogs");
foreach (var (woolworths, coles, other) in new[] { (5, 1, 2), (1, 5, 0), (0, 3, 1), (3, 0, 0) })
{
    fixture.Offers = Enumerable.Range(0, woolworths + coles + other).Select(i => new OfferSummary
    { OfferID = 100 + i, ProductName = $"Offer {i}", RetailerName = i < woolworths ? "Woolworths" : i < woolworths + coles ? "Coles" : "Independent" }).ToList();
    html = await Render();
    Check(System.Text.RegularExpressions.Regex.Matches(html, "class=\"deal\"").Count == fixture.Offers.Count &&
        fixture.Offers.All(o => html.Contains($"href=\"/offers/{o.OfferID}\"")),
        $"All offers accessible with retailer counts {woolworths}/{coles}/{other}");
}
fixture.Offers = [];
Check((await Render()).Contains("No offers found."), "Empty state");
fixture.Fail = true;
html = await Render();
Check(html.Contains("role=\"alert\"") && html.Contains("Retry"), "API failure offers retry");
fixture.Fail = false;
fixture.Pending = new();
await renderer.Dispatcher.InvokeAsync(async () =>
{
    var root = renderer.BeginRenderingComponent<OffersComponent>();
    Check(root.ToHtmlString().Contains("Loading offers..."), "Loading state");
    fixture.Pending.SetResult([]);
    await root.QuiescenceTask;
});
Console.WriteLine("All web Offers checks passed.");

sealed class Fixture : IOfferService
{
    public List<OfferSummary> Offers = [];
    public bool Fail;
    public TaskCompletionSource<List<OfferSummary>>? Pending;
    public Task<List<OfferSummary>> GetCurrentOffersAsync(CancellationToken cancellationToken = default) =>
        Fail ? Task.FromException<List<OfferSummary>>(new Exception("Offline fixture")) : Pending?.Task ?? Task.FromResult(Offers);
    public Task<List<OfferSummary>> GetOffersAsync(CancellationToken cancellationToken = default) => GetCurrentOffersAsync(cancellationToken);
    public Task<List<OfferSummary>> GetPreviousOffersAsync(CancellationToken cancellationToken = default) => GetCurrentOffersAsync(cancellationToken);
}

