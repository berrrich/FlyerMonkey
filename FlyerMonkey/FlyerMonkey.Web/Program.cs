using FlyerMonkey.Shared.Services;
using FlyerMonkey.Web.Components;
using Syncfusion.Blazor;
using FlyerMonkey.Web.Services;
using AppProductService =
    FlyerMonkey.Shared.Services.IProductService;

using WebProductApiService =
    FlyerMonkey.Shared.Services.ProductApiService;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IWebsiteCraterSession, WebsiteCraterSession>();
builder.Services.AddScoped<ILocalDevelopmentGeneration, LocalDevelopmentGeneration>();
var operatorAuthentication = FlyerMonkey.Web.Services.OperatorAuthentication.AddOperatorAuthentication(
    builder.Services, builder.Configuration, builder.Environment);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSyncfusionBlazor();
builder.Services.AddApprovedArtworkClient(builder.Configuration.GetValue<bool>("ApprovedArtwork:Enabled"),
    builder.Configuration["ApprovedArtwork:ApiBaseUrl"]);

builder.Services.AddWebCraterClients(builder.Environment.IsDevelopment(),
    builder.Configuration.GetValue<bool>("CraterIt:Enabled"), builder.Configuration["CraterIt:ApiBaseUrl"]);

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

if (operatorAuthentication)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapOperatorAuthentication();
}
app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(FlyerMonkey.Shared._Imports).Assembly);

app.Run();
