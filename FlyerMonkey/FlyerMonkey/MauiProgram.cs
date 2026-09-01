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

            // Add device-specific services used by the FlyerMonkey.Shared project
            builder.Services.AddSingleton<IFormFactor, FormFactor>();

           builder.Services.AddSingleton<MonkeyService>();
            builder.Services.AddSingleton<PricelineService>();
            builder.Services.AddSingleton<HttpClient>();
            builder.Services.AddSyncfusionBlazor();
            // inside CreateMauiApp builder setup
            builder.Services.AddHttpClient<AppProductService, ProductApiService>(client =>
            {
#if ANDROID
                client.BaseAddress = new Uri("http://10.0.2.2:5053/");
#else
    client.BaseAddress = new Uri("https://localhost:7094/");
#endif
            });
            //builder.Services.AddSingleton<IMongoClient, MongoClient>(sp =>
            //new MongoClient("mongodb+srv://richardberriman:T7dFV6OotJ0L1hUv@shopamon.itfqzhv.mongodb.net/?appName=Shopamon"));

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
