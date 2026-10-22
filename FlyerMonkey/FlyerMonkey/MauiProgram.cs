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
            builder.Services.AddSingleton(new FlyerMonkey.Services.AndroidOperatorOptions());
            builder.Services.AddSingleton<FlyerMonkey.Services.AndroidOperatorAuthentication>();
            builder.Services.AddSingleton<IOperatorTokenProvider>(sp => sp.GetRequiredService<FlyerMonkey.Services.AndroidOperatorAuthentication>());
            builder.Services.AddSingleton<IOperatorSession>(sp => sp.GetRequiredService<FlyerMonkey.Services.AndroidOperatorAuthentication>());
            builder.Services.AddTransient(sp => new OperatorArtworkClient(
                new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }), sp.GetRequiredService<IOperatorTokenProvider>(),
                new Uri(sp.GetRequiredService<FlyerMonkey.Services.AndroidOperatorOptions>().ApiBaseUrl)));
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
