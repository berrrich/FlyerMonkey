#if ANDROID
using System.Diagnostics;

namespace FlyerMonkey.Services;

internal static class AndroidAuthDiagnostics
{
    // Call sites pass fixed stage names only. Never log intents, URLs, accounts or exceptions.
    [Conditional("DEBUG")]
    internal static void Write(string stage) =>
        Android.Util.Log.Debug("FlyerMonkey.AuthReturn", $"pid={Android.OS.Process.MyPid()} {stage}");
}
#endif
