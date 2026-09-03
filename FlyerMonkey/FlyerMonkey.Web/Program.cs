//using FlyerMonkey.Services;
using FlyerMonkey.Shared.Services;
using FlyerMonkey.Web.Components;
using FlyerMonkey.Web.Services;
//using FlyerMonkey.Server.Data;
using SQLServerConnection.Data;
using Syncfusion.Blazor;
using AppProductService =
    FlyerMonkey.Shared.Services.IProductService;

using WebProductApiService =
    FlyerMonkey.Shared.Services.ProductApiService;

var builder = WebApplication.CreateBuilder(args);
// Add database contact

var connectionString =
    builder.Configuration.GetConnectionString(
        "AZURE_SQL_CONNECTIONSTRING")
    ?? throw new InvalidOperationException(
        "Connection string 'AZURE_SQL_CONNECTIONSTRING' was not found.");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add device-specific services used by the FlyerMonkey.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddScoped<MonkeyService>();
builder.Services.AddScoped<PricelineService>();
builder.Services.AddScoped<IProductRepository>(_ =>
    new ProductRepository(connectionString));
builder.Services.AddSyncfusionBlazor();

builder.Services.AddHttpClient<
    AppProductService,
    WebProductApiService>(client =>
    {
        client.BaseAddress = new Uri("https://localhost:7094/");
    });

builder.Services.AddScoped<IProductRepository>(_ =>
    new ProductRepository(connectionString));

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

app.MapGet("/api/products", async (
    IProductRepository repository,
    CancellationToken cancellationToken) =>
{
    var products = await repository.GetProductsAsync(cancellationToken);
    return Results.Ok(products);
});

app.Run();
