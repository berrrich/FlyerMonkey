using FlyerMonkey.Services;
using FlyerMonkey.Shared.Services;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Microsoft.Extensions.DependencyInjection;
using AppProductService = FlyerMonkey.Shared.Services.IProductService;

namespace FlyerMonkey
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddSingleton<IFormFactor, FormFactor>();
            builder.Services.AddSingleton<MonkeyService>();
            builder.Services.AddSingleton<PricelineService>();
            builder.Services.AddSyncfusionBlazor();

            var apiBaseUrl =
                "https://flyermonkeyapi-g7htasdacxfzgbcd.australiaeast-01.azurewebsites.net/";

            builder.Services.AddHttpClient<AppProductService, ProductApiService>(client =>
            {
                client.BaseAddress = new Uri(apiBaseUrl);
            });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}