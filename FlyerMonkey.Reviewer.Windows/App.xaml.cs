using System.Windows;
using Syncfusion.Licensing;

namespace FlyerMonkey.Reviewer.Windows
{
    public partial class App : Application
    {
        public App()
        {
            Syncfusion.Telemetry.Telemetry.Disable();
            var syncfusionKey =
                Environment.GetEnvironmentVariable(
                    "FLYERMONKEY_SYNCFUSION_KEY");

            if (string.IsNullOrWhiteSpace(syncfusionKey))
            {
                throw new InvalidOperationException(
                    "Syncfusion licence key is not configured.");
            }

            SyncfusionLicenseProvider.RegisterLicense(syncfusionKey);
        }
    }
}
