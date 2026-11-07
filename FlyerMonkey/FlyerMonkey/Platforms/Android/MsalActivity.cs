using Android.App;
using Android.Content.PM;
using Microsoft.Identity.Client;
using Android.Content;
using Android.OS;
using FlyerMonkey.Services;

namespace FlyerMonkey;

[Activity(Name = "com.flyermonkey.MsalActivity", Exported = true, LaunchMode = LaunchMode.SingleTask,
    NoHistory = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MsalActivity : BrowserTabActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var hasCaller = MainActivity.HasLiveInstance;
        AndroidAuthDiagnostics.Write(hasCaller ? "redirect.live-caller" : "redirect.no-caller");
        // MSAL owns the redirect data and forwards it to AuthenticationActivity.
        // Its BrowserTabActivity does not finish itself. Do not leave this UI-less
        // activity as the task's foreground screen after that handoff.
        base.OnCreate(savedInstanceState);
        Finish();
        AndroidAuthDiagnostics.Write("redirect.finished");
        if (!hasCaller)
        {
            // Process death destroys the pending MSAL task; never replay the URL
            // or pretend authentication succeeded. Return to the app's real UI.
            var launch = new Intent(this, typeof(MainActivity));
            launch.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            StartActivity(launch);
            AndroidAuthDiagnostics.Write("redirect.reopen-main");
        }
    }
}
