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

#if ANDROID
            var apiBaseUrl = DeviceInfo.DeviceType == DeviceType.Virtual
                ? "http://10.0.2.2:5053/"
                : "http://192.168.4.95:5053/";
#else
var apiBaseUrl = "https://localhost:7094/";
#endif

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