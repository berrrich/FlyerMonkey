// A small offline activity host: exercises the real app lifecycle overrides,
// not Android/MSAL internals. Android behavior still requires a later live test.
public class MauiAppCompatActivity : Android.App.Activity;

namespace Android.OS
{
    public sealed class Bundle;
    public static class Process { public static int MyPid() => 1; }
}
namespace Android.Content.PM
{
    public enum LaunchMode { Multiple, SingleTop, SingleTask }
    [Flags] public enum ConfigChanges { ScreenSize = 1, Orientation = 2, UiMode = 4, ScreenLayout = 8, SmallestScreenSize = 16, Density = 32 }
}
namespace Android.Content
{
    [Flags] public enum ActivityFlags { NewTask = 1, ClearTop = 2, SingleTop = 4 }
    public sealed class Intent
    {
        public Intent(object context, Type target) { ArgumentNullException.ThrowIfNull(context); Target = target; }
        public Type Target { get; }
        public ActivityFlags Flags { get; private set; }
        public string? Data { get; set; }
        public Dictionary<string, string> Extras { get; } = [];
        public void AddFlags(ActivityFlags flags) => Flags |= flags;
    }
}
namespace Android.Util
{
    public static class Log { public static void Debug(string tag, string message) { } }
}
namespace Android.App
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ActivityAttribute : Attribute
    {
        public string? Name { get; set; }
        public string? Theme { get; set; }
        public bool MainLauncher { get; set; }
        public bool Exported { get; set; }
        public bool NoHistory { get; set; }
        public Content.PM.LaunchMode LaunchMode { get; set; }
        public Content.PM.ConfigChanges ConfigurationChanges { get; set; }
    }
    public enum Result { Ok }
    public class Activity
    {
        public bool IsFinishing { get; private set; }
        public bool IsDestroyed { get; private set; }
        public Content.Intent? Intent { get; set; }
        public List<Content.Intent> Started { get; } = [];
        public int BaseNewIntents { get; private set; }
        protected virtual void OnCreate(OS.Bundle? state) { }
        protected virtual void OnNewIntent(Content.Intent? intent) => BaseNewIntents++;
        protected virtual void OnResume() { }
        protected virtual void OnDestroy() => IsDestroyed = true;
        protected virtual void OnActivityResult(int request, Result result, Content.Intent? data) { }
        public void Finish() => IsFinishing = true;
        public void StartActivity(Content.Intent intent) => Started.Add(intent);
        public void Create() => OnCreate(null);
        public void Destroy() => OnDestroy();
        public void NewIntent(Content.Intent intent) => OnNewIntent(intent);
        public void Result() => OnActivityResult(1, App.Result.Ok, null);
    }
}
namespace Microsoft.Identity.Client
{
    public sealed class AuthenticationActivity : Android.App.Activity;
    public class BrowserTabActivity : Android.App.Activity
    {
        public static int Forwards;
        protected override void OnCreate(Android.OS.Bundle? state)
        {
            base.OnCreate(state);
            Forwards++;
            StartActivity(new Android.Content.Intent(this, typeof(AuthenticationActivity)));
        }
    }
    public static class AuthenticationContinuationHelper
    {
        public static int Forwards;
        public static void SetAuthenticationContinuationEventArgs(int request, Android.App.Result result, Android.Content.Intent? data) => Forwards++;
    }
}
