using FlyerMonkey.Shared.Services;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Microsoft.Extensions.DependencyInjection;

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

            
            builder.Services.AddSyncfusionBlazor();

#if ANDROID
            // Both Android configurations browse the published read-only HTTPS API.
            builder.Services.AddMauiPublicApiClients(android: true);
#else
            builder.Services.AddMauiPublicApiClients(android: false);
#endif

#if ANDROID && DEBUG
            builder.Services.AddMauiCraterClients(androidDebug: true);
#else
            builder.Services.AddMauiCraterClients(androidDebug: false);
#endif
            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
