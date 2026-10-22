using Android.App;
using Android.Content.PM;
using Microsoft.Identity.Client;

namespace FlyerMonkey;

[Activity(Name = "com.flyermonkey.MsalActivity", Exported = true, LaunchMode = LaunchMode.SingleTask,
    NoHistory = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MsalActivity : BrowserTabActivity;
