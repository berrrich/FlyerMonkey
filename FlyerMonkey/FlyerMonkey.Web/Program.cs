using FlyerMonkey.Shared.Services;
using FlyerMonkey.Web.Components;
using Syncfusion.Blazor;
using AppProductService =
    FlyerMonkey.Shared.Services.IProductService;

using WebProductApiService =
    FlyerMonkey.Shared.Services.ProductApiService;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSyncfusionBlazor();

builder.Services.AddHttpClient<ImageGenerationApiService>(client =>
{
    if (builder.Environment.IsDevelopment() &&
        builder.Configuration.GetValue<bool>("CraterIt:Enabled") &&
        Uri.TryCreate(builder.Configuration["CraterIt:ApiBaseUrl"], UriKind.Absolute, out var uri) &&
        uri.IsLoopback && (uri.Scheme == "http" || uri.Scheme == "https"))
        client.BaseAddress = uri;
    client.Timeout = TimeSpan.FromMinutes(3);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddHttpClient<
    AppProductService,
    WebProductApiService>(client =>
    {
        client.BaseAddress = new Uri(
    "https://flyermonkeyapi-g7htasdacxfzgbcd.australiaeast-01.azurewebsites.net/");
    });
builder.Services.AddHttpClient<
    IOfferService,
    OfferApiService>(client =>
    {
        client.BaseAddress = new Uri(
            "https://flyermonkeyapi-g7htasdacxfzgbcd.australiaeast-01.azurewebsites.net/");
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(FlyerMonkey.Shared._Imports).Assembly);

app.Run();
