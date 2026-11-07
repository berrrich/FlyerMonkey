using Android.App;
using Android.Content.PM;
using Android.OS;

namespace FlyerMonkey
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private static WeakReference<MainActivity>? current;
        internal static bool HasLiveInstance => current?.TryGetTarget(out var activity) == true &&
            !activity.IsFinishing && !activity.IsDestroyed;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            Services.AndroidAuthDiagnostics.Write(savedInstanceState is null ? "main.create" : "main.recreate");
            base.OnCreate(savedInstanceState);
            current = new(this);
        }

        protected override void OnNewIntent(Android.Content.Intent? intent)
        {
            base.OnNewIntent(intent);
            if (intent is not null) Intent = intent;
            Services.AndroidAuthDiagnostics.Write("main.new-intent");
        }

        protected override void OnResume()
        {
            base.OnResume();
            Services.AndroidAuthDiagnostics.Write("main.resume");
        }

        protected override void OnDestroy()
        {
            if (current?.TryGetTarget(out var activity) == true && ReferenceEquals(activity, this)) current = null;
            Services.AndroidAuthDiagnostics.Write("main.destroy");
            base.OnDestroy();
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            Microsoft.Identity.Client.AuthenticationContinuationHelper.SetAuthenticationContinuationEventArgs(requestCode, resultCode, data);
            Services.AndroidAuthDiagnostics.Write("main.activity-result-forwarded");
        }
    }
}
