using Android.App;
using Android.Content;
using Android.Content.PM;
using FlyerMonkey;
using Microsoft.Identity.Client;

var checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS: " + name);
}

var main = new MainActivity();
main.Create();
Check(MainActivity.HasLiveInstance, "Live MAUI caller is available during browser authentication");
var redirect = new MsalActivity();
redirect.Create();
Check(BrowserTabActivity.Forwards == 1, "Live callback is forwarded through the SDK exactly once");
Check(redirect.IsFinishing, "UI-less redirect finishes after SDK forwarding");
Check(redirect.Started.Count == 1 && redirect.Started[0].Target == typeof(AuthenticationActivity),
    "Live return preserves the original MAUI activity instead of launching another");
Check(MainActivity.HasLiveInstance && !main.IsDestroyed, "Original MAUI caller survives callback handoff");
var incoming = new Intent(main, typeof(MainActivity));
main.NewIntent(incoming);
Check(ReferenceEquals(main.Intent, incoming) && main.BaseNewIntents == 1, "Reused MainActivity delivers new intent to MAUI and updates its intent");
main.Result();
Check(AuthenticationContinuationHelper.Forwards == 1, "MainActivity still completes the MSAL activity-result continuation");
var replacement = new MainActivity();
replacement.Create();
main.Destroy();
Check(MainActivity.HasLiveInstance, "Destroying an old activity does not forget its replacement");
replacement.Destroy();
Check(!MainActivity.HasLiveInstance, "Destroyed caller is unavailable after process/activity loss");
var orphan = new MsalActivity();
orphan.Create();
Check(orphan.IsFinishing && BrowserTabActivity.Forwards == 2, "Orphan callback still finishes after SDK handoff");
var recovery = orphan.Started.Last();
Check(recovery.Target == typeof(MainActivity), "Missing caller returns to real MAUI UI");
Check(recovery.Flags == (ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop),
    "Recovery reuses an existing main task when available");
Check(recovery.Data is null && recovery.Extras.Count == 0, "Recovery never replays OAuth data into MainActivity");
Check(typeof(MainActivity).GetCustomAttributes(typeof(ActivityAttribute), false).Cast<ActivityAttribute>().Single().LaunchMode == LaunchMode.SingleTop,
    "MainActivity uses singleTop launch semantics");
Console.WriteLine($"{checks} Android return checks passed. Fake activity host/SDK only; no device, identity or provider calls.");
