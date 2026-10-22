using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using FlyerMonkey.Api.Controllers;
using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SQLServerConnection.Data;

// All identifiers and signing keys below are disposable OFFLINE fixtures, not Entra accounts/tokens.
const string tenant = "11111111-1111-4111-8111-111111111111";
const string owner = "22222222-2222-4222-8222-222222222222";
const string api = "33333333-3333-4333-8333-333333333333";
const string client = "44444444-4444-4444-8444-444444444444";
const string other = "55555555-5555-4555-8555-555555555555";
const string issuer = $"https://login.microsoftonline.com/{tenant}/v2.0";
using var rsa = RSA.Create(2048);
using var wrongRsa = RSA.Create(2048);
var key = new RsaSecurityKey(rsa) { KeyId = "offline" };
var wrongKey = new RsaSecurityKey(wrongRsa) { KeyId = "offline-wrong" };
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:Enabled"] = "true", ["Authentication:TenantId"] = tenant,
    ["Authentication:ApiClientId"] = api, ["Authentication:OperatorObjectId"] = owner,
    ["Authentication:AllowedClientIds:0"] = client,
    ["CraterIt:PrivateAccessEnabled"] = "true", ["CraterIt:Enabled"] = "true"
});
for (var i = 0; i < CraterPermissions.All.Length; i++)
    builder.Configuration[$"Authentication:OperatorPermissions:{i}"] = CraterPermissions.All[i];
builder.Services.AddFlyerMonkeyAuthentication(builder.Configuration);
// Static discovery/signing data: prevents every network call to identity metadata.
builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    var discovery = new OpenIdConnectConfiguration { Issuer = issuer };
    discovery.SigningKeys.Add(key);
    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(discovery);
    options.Backchannel = new HttpClient(new ForbiddenNetwork());
});
builder.Services.AddControllers().AddApplicationPart(typeof(ImagesController).Assembly);
builder.Services.AddSingleton<IProductRepository, Products>();
builder.Services.AddSingleton<ProductService>();
builder.Services.AddSingleton(new ProductImageService(new ConfigurationBuilder().AddInMemoryCollection(
    new Dictionary<string, string?> { ["ConnectionStrings:BlobStorage"] = "UseDevelopmentStorage=true" }).Build()));
var storage = new ArtworkStore();
builder.Services.AddSingleton<ICraterArtworkStore>(storage);
var providerResolutions = 0;
builder.Services.AddScoped<ImageGenerationService>(_ => { providerResolutions++; throw new Exception("Paid provider must never resolve"); });
await using var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
foreach (var permission in CraterPermissions.All)
    app.MapGet("/test/" + permission, () => Results.Ok()).RequireAuthorization(permission);
app.MapGet("/test/fallback", () => Results.Ok());
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
string Token(string? tokenIssuer = null, string? audience = null, string subject = owner,
    string tokenTenant = tenant, string? scope = "Crater.Access", string actor = client,
    string version = "2.0", bool expired = false, SecurityKey? signingKey = null)
{
    var claims = new List<Claim> { new("tid", tokenTenant), new("oid", subject), new("azp", actor), new("ver", version),
        new("email", "same-display-email@example.invalid") };
    if (scope is not null) claims.Add(new("scp", scope));
    return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(tokenIssuer ?? issuer, audience ?? api, claims,
        DateTime.UtcNow.AddHours(-1), expired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5),
        new SigningCredentials(signingKey ?? key, SecurityAlgorithms.RsaSha256)));
}
try
{
    await app.StartAsync();
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new(app.Urls.Single()) };
    async Task<HttpResponseMessage> Get(string path, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) request.Headers.Authorization = new("Bearer", token);
        return await http.SendAsync(request);
    }
    Check((await Get("api/products")).StatusCode == HttpStatusCode.OK, "Anonymous catalogue with fallback policy");
    Check((await Get("api/status")).StatusCode == HttpStatusCode.OK, "Anonymous status");
    Check((await Get("api/products/1/approved-artwork/image")).StatusCode == HttpStatusCode.NotFound, "Anonymous approved route reaches disabled feature, not auth challenge");
    Check((await Get("api/images/artwork?productId=1")).StatusCode == HttpStatusCode.Unauthorized, "Missing token");
    Check((await Get("test/fallback")).StatusCode == HttpStatusCode.Unauthorized, "Unannotated route defaults denied");
    foreach (var invalid in new[] { "invalid", Token(tokenIssuer: "https://wrong.invalid/v2.0"), Token(audience: other),
        Token(expired: true), Token(signingKey: wrongKey) })
        Check((await Get("api/images/artwork?productId=1", invalid)).StatusCode == HttpStatusCode.Unauthorized, "Invalid JWT rejected");
    foreach (var forbidden in new[] { Token(subject: other), Token(tokenTenant: other), Token(scope: null),
        Token(scope: "Other.Access"), Token(actor: other), Token(version: "1.0") })
        Check((await Get("api/images/artwork?productId=1", forbidden)).StatusCode == HttpStatusCode.Forbidden, "Invalid operator/delegation rejected");
    var valid = Token();
    foreach (var permission in CraterPermissions.All)
    {
        Check((await Get("test/" + permission, valid)).StatusCode == HttpStatusCode.OK, "Explicit grant " + permission);
        for (var i = 0; i < CraterPermissions.All.Length; i++) builder.Configuration[$"Authentication:OperatorPermissions:{i}"] = CraterPermissions.All[i] == permission ? "" : CraterPermissions.All[i];
        Check((await Get("test/" + permission, valid)).StatusCode == HttpStatusCode.Forbidden, "No inherited grant " + permission);
        for (var i = 0; i < CraterPermissions.All.Length; i++) builder.Configuration[$"Authentication:OperatorPermissions:{i}"] = CraterPermissions.All[i];
    }
    var list = await (await Get("api/images/artwork?productId=1", valid)).Content.ReadFromJsonAsync<List<CraterArtwork>>();
    Check(list is { Count: 1 } && list[0].ArtworkId == storage.Owned.ArtworkId, "Only owned metadata listed");
    foreach (var hidden in new[] { storage.Foreign, storage.Legacy })
    {
        var before = storage.Reads;
        Check((await Get($"api/images/artwork/{hidden.ArtworkId:N}/image", valid)).StatusCode == HttpStatusCode.NotFound && storage.Reads == before,
            "Foreign/ownerless bytes never read");
    }
    Check((await Get($"api/images/artwork/{storage.Owned.ArtworkId:N}/image", valid)).StatusCode == HttpStatusCode.OK, "Owned bytes retrievable");
    builder.Configuration["CraterIt:PrivateAccessEnabled"] = "false";
    Check((await Get("api/images/artwork?productId=1", valid)).StatusCode == HttpStatusCode.NotFound, "Private switch defaults closed");
    builder.Configuration["CraterIt:PrivateAccessEnabled"] = "true";
    async Task<HttpResponseMessage> Generate(string? token, Guid requestId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/images/generate")
        { Content = JsonContent.Create(new { RequestId = requestId, ProductID = 1, ProductName = "Test", Prompt = "test", Preset = 0 }) };
        if (token is not null) request.Headers.Authorization = new("Bearer", token);
        request.Headers.Add("X-CraterIt-Local", "1");
        return await http.SendAsync(request);
    }
    Check((await Generate(null, Guid.NewGuid())).StatusCode == HttpStatusCode.Unauthorized, "Local header cannot bypass enabled auth");
    Check((await Generate(valid, Guid.NewGuid())).StatusCode == HttpStatusCode.NotFound && providerResolutions == 0, "Production generation denied even with every grant and enabled flag");
    app.Environment.EnvironmentName = "Development";
    Check((await Generate(valid, storage.Foreign.RequestId)).StatusCode == HttpStatusCode.Conflict && providerResolutions == 0,
        "Cross-owner replay never returns artwork or calls provider");
    using var removedPost = new HttpRequestMessage(HttpMethod.Post, "api/products") { Content = JsonContent.Create(new { Name = "Injected" }) };
    removedPost.Headers.Authorization = new("Bearer", valid);
    Check((await http.SendAsync(removedPost)).StatusCode == HttpStatusCode.MethodNotAllowed, "Product POST remains removed");
    Check(providerResolutions == 0, "No paid provider constructed");
    // Keep all configured grants to prove that the local pilot cap overrides stale entries.
    builder.Configuration["Authentication:ViewOwnOnly"] = "true";
    using var accessResponse = await Get("api/operator/access", valid);
    Check(accessResponse.StatusCode == HttpStatusCode.OK && accessResponse.Headers.CacheControl?.NoStore == true, "Local access evidence is authenticated and no-store");
    using var accessJson = System.Text.Json.JsonDocument.Parse(await accessResponse.Content.ReadAsStringAsync());
    var access = accessJson.RootElement;
    Check(access.GetProperty("authenticated").GetBoolean() && access.GetProperty("tenantMatches").GetBoolean() &&
        access.GetProperty("operatorMatches").GetBoolean(), "Validated identity matches without displaying identifiers");
    foreach (var permission in CraterPermissions.All)
        Check(access.GetProperty("permissions").GetProperty(permission).GetBoolean() == (permission == CraterPermissions.ViewOwn), "ViewOwn-only evidence: " + permission);
    Check((await Get("api/operator/access", Token(subject: other))).StatusCode == HttpStatusCode.Forbidden, "Wrong identity cannot obtain access evidence");
    app.Environment.EnvironmentName = "Production";
    Check((await Get("api/operator/access", valid)).StatusCode == HttpStatusCode.NotFound, "Access evidence absent outside Development");
    app.Environment.EnvironmentName = "Development";

    var handler = new CaptureHandler();
    using var transport = new HttpClient(handler);
    var tokenProvider = new FakeTokens();
    var privateClient = new OperatorArtworkClient(transport, tokenProvider, new Uri("https://offline.invalid/"));
    using var sent = await privateClient.GetAsync("api/images/artwork?productId=1");
    Check(handler.Authorization == "Bearer offline-token" && handler.Target?.Host == "offline.invalid", "Private bearer confined to trusted API");
    foreach (var bad in new[] { "https://evil.invalid/api/images/artwork", "//evil.invalid/api/images/artwork", "api/images/artwork/../../products", "api/images/artwork/%2e%2e/products" })
    {
        try { await privateClient.GetAsync(bad); throw new Exception("Expected destination rejection"); }
        catch (InvalidOperationException) { Check(true, "Untrusted destination denied"); }
    }
    Console.WriteLine($"{checks} checks passed. Locally signed JWTs, loopback HTTP, fake repositories/storage only; no real Entra integration or paid calls.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { await app.StopAsync(); }

sealed class ForbiddenNetwork : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new Exception("Identity network access forbidden in offline tests");
}
sealed class Products : IProductRepository
{
    public Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<Product> { new() { ID = 1, Name = "Offline" } });
    public Task<int> AddProductAsync(Product product, CancellationToken cancellationToken = default) => throw new Exception("SQL writes forbidden");
}
sealed class ArtworkStore : ICraterArtworkStore
{
    public CraterArtwork Owned = Make(Guid.Parse("22222222-2222-4222-8222-222222222222"));
    public CraterArtwork Foreign = Make(Guid.NewGuid());
    public CraterArtwork Legacy = Make(null);
    public int Reads;
    static CraterArtwork Make(Guid? owner) => new(Guid.NewGuid(), Guid.NewGuid(), 1, "Test", CraterStyle.Classic, "Classic", DateTimeOffset.UtcNow,
        "image/jpeg", "artwork.jpg", 1, new string('A', 64))
        { OwnerTenantId = owner is null ? null : Guid.Parse("11111111-1111-4111-8111-111111111111"), OwnerObjectId = owner };
    public Task<CraterArtwork?> GetMetadataAsync(Guid id, CancellationToken token) => Task.FromResult(new[] { Owned, Foreign, Legacy }.SingleOrDefault(a => a.ArtworkId == id));
    public Task<CraterArtwork?> FindRequestAsync(Guid id, CancellationToken token) => Task.FromResult(new[] { Owned, Foreign, Legacy }.SingleOrDefault(a => a.RequestId == id));
    public Task<IReadOnlyList<CraterArtwork>> ListAsync(int productId, CancellationToken token) => Task.FromResult<IReadOnlyList<CraterArtwork>>([Owned, Foreign, Legacy]);
    public Task SaveAsync(CraterArtwork artwork, byte[] jpeg, CancellationToken token) => throw new Exception("No writes expected");
    public Task<byte[]?> ReadAsync(Guid id, CancellationToken token) { Reads++; return Task.FromResult<byte[]?>([255, 216, 255]); }
}
sealed class FakeTokens : IOperatorTokenProvider
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("offline-token");
}
sealed class CaptureHandler : HttpMessageHandler
{
    public string? Authorization;
    public Uri? Target;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Authorization = request.Headers.Authorization?.ToString(); Target = request.RequestUri;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
