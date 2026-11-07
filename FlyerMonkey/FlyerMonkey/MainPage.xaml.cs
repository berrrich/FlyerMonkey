namespace FlyerMonkey
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
#if ANDROID
            Services.AndroidAuthDiagnostics.Write("blazor-view.created");
            blazorWebView.BlazorWebViewInitialized += (_, _) =>
                Services.AndroidAuthDiagnostics.Write("blazor-view.initialized");
#endif
        }
    }
}
