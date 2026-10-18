extern alias CraterAzureIdentity;
using Azure.Storage.Blobs;

namespace FlyerMonkey.Api.Services;

public static class CraterArtworkStoreFactory
{
    public static ICraterArtworkStore Create(IConfiguration configuration, IWebHostEnvironment environment,
        ILoggerFactory loggers)
    {
        var provider = configuration["CraterArt:Provider"] ?? "Local";
        if (provider == "Local")
            return new LocalCraterArtworkStore(LocalCraterArtworkStore.ResolveRoot(environment),
                loggers.CreateLogger<LocalCraterArtworkStore>());
        if (provider != "Azure") throw new InvalidOperationException("CraterArt:Provider must be Local or Azure.");
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Azure Crater artwork is Development-only.");
        if (!Uri.TryCreate(configuration["CraterArt:Azure:ServiceUri"], UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || !uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath != "/" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("CraterArt:Azure:ServiceUri must be an Azure public-cloud HTTPS Blob account root without credentials.");
        if (configuration["CraterArt:Azure:Container"] != "crater-artwork")
            throw new InvalidOperationException("CraterArt:Azure:Container must explicitly be crater-artwork.");

        // Construction does not authenticate or send traffic. Credentials are resolved only by an actual operation.
        var client = new BlobServiceClient(uri, new CraterAzureIdentity::Azure.Identity.DefaultAzureCredential());
        return new AzureCraterArtworkStore(client.GetBlobContainerClient("crater-artwork"),
            loggers.CreateLogger<AzureCraterArtworkStore>());
    }
}
