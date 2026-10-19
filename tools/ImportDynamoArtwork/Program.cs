using FlyerMonkey.Api.Services;
using FlyerMonkey.Tools;

// No network in default mode. No approval entry is ever created by this tool.
if (args.Length != 2 || args[0] != "--live-approved-import")
{
    Console.WriteLine("Disabled. After separate approval: --live-approved-import <exact-local-artwork-directory>");
    return 0;
}
try
{
    await DynamoArtworkImporter.ImportAsync(args[1], () => ApprovedArtworkReader.CreateContainer(
        "https://flyermonkey.blob.core.windows.net/", managedIdentityOnly: false));
    Console.WriteLine("Exact Dynamo objects copied and verified. No publication approval was created.");
    return 0;
}
catch (Exception)
{
    Console.Error.WriteLine("Import failed. Inspect destination before any retry; no deletion, repair or rollback is automatic.");
    return 1;
}