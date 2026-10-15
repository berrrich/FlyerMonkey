# Crater It! implementation

Implemented on Product Detail only. The original product image remains visible and unchanged; generated JPEGs appear separately as labelled, in-memory previews. No persistence is added.

## Changed files

- FlyerMonkey/FlyerMonkey.Shared/Pages/ProductDetail.razor: generation button, progress/error messages, cancel and clear-preview actions, duplicate-click guard, bounded prompt, navigation/disposal cancellation and stale-result protection. UTF-8 encoding and EF BB BF BOM preserved; edits avoided the first line.
- FlyerMonkey/FlyerMonkey.Shared/Services/ImageGenerationApiService.cs (new): injectable client for the existing POST api/images/generate endpoint, loopback-only enablement, request header, JPEG validation, cancellation and no automatic client retries.
- FlyerMonkey/FlyerMonkey.Web/Program.cs: explicit Development-only opt-in and loopback API URL, three-minute client timeout and redirects disabled.
- FlyerMonkey/FlyerMonkey/MauiProgram.cs: registers the client without a base address so shared-page injection works while generation remains disabled.
- FlyerMonkey.Api/Controllers/ImagesController.cs: rejects generation unless Development, explicitly enabled, loopback caller and loopback Host, with the local request header present. Guards execute before resolving the generation service. Validates nonblank prompts up to 2000 characters, rejects concurrent requests with 429, returns no-store JPEG responses, and propagates cancellation.
- FlyerMonkey.Api/Services/ImageGenerationService.cs: forwards cancellation to the existing provider operation.
- tests/CraterIt.Checks/CraterIt.Checks.csproj and Program.cs (new): package-free console checks using linked controller/client source and a fake provider service/HTTP handler.
- docs/CraterItImplementation.md: this report.

Offer Detail, SQL, blob storage, deployment settings and the investigation report were not changed. No packages installed, credentials accessed, database commands executed, publication or commits performed.

## Validation results

- dotnet build FlyerMonkey.Api/FlyerMonkey.Api.csproj --no-restore -v minimal: passed; two unused-field warnings in existing Offers.razor.
- dotnet build FlyerMonkey/FlyerMonkey.Web/FlyerMonkey.Web.csproj --no-restore -v minimal: passed, zero warnings/errors.
- dotnet build FlyerMonkey/FlyerMonkey/FlyerMonkey.csproj --no-restore -f net10.0-windows10.0.19041.0 -v minimal: passed; three PRI249 resource-qualifier warnings.
- dotnet run --project tests/CraterIt.Checks/CraterIt.Checks.csproj --no-launch-profile: all 15 checks passed. Checks cover Production/disabled/nonlocal/missing-header rejection before service resolution, invalid prompts, JPEG return, concurrency rejection, API/client cancellation, one client POST, invalid JPEG rejection and disabled default/public-address clients.
- git diff --check: passed.
- Byte inspection confirmed ProductDetail.razor still begins EF BB BF. Original image markup remains unchanged.
- No live application or provider was started. Automated checks do not exercise browser interactions or the real provider. Mobile targets other than Windows were not built.

## Manual local configuration

Use process-local configuration; no deployment or launch-settings changes were made.

1. Run both the web and API hosts with ASPNETCORE_ENVIRONMENT=Development, bound only to loopback addresses. Do not use a public listener, reverse proxy or public tunnel for this PoC.
2. Set CraterIt__Enabled=true for both processes. It defaults to disabled when absent. Production rejects generation regardless of this flag.
3. For the web process, set CraterIt__ApiBaseUrl to the actual local API root, including trailing slash, for example http://localhost:5001/. Use the port/scheme of the local API listener. HTTPS requires a trusted development certificate. Redirects are deliberately rejected, so configure the final address directly.
4. The API still needs its existing startup configuration (including FLYERMONKEY_SQL_CONNECTION) and server-side OpenAI:ApiKey configuration. The operator must supply existing configuration; no values were read or changed during implementation. Product loading and canonical images retain their existing data/API behavior.
5. The web typed client adds X-CraterIt-Local: 1. Requests without that header return 404, including the unchanged reviewer client. This is intentional: the old unrestricted deployed generation path is disabled outside opted-in local Development. The custom header is a browser cross-origin safeguard, not authentication for public use.
6. MAUI intentionally has no generation URL and stays disabled in every build configuration.

## Remaining manual checks

In the opted-in local web app, open a Product Detail page and verify generation progress, labelled JPEG preview, original-image preservation, clear preview, error recovery with the API unavailable, cancel, rapid repeated clicks, and navigation to another product while generation is pending. Confirm production/disabled configuration hides the action. These browser checks and a real paid generation request were not run because they require operator configuration and credentials.

Cancellation stops client waiting and propagates to the provider request, but cannot guarantee that an already accepted provider operation stops or avoids a charge. There is no save/upload operation. Preview data is cleared on route changes or disposal; a failed regeneration leaves any previous preview intact. The process-wide API semaphore permits only one active generation request and releases in finally.

## Interrupted-work recovery

At resumption git status showed only untracked docs/ and git diff was empty; no partial application changes existed. This implementation used smaller patches below the BOM-prefixed first line. All patches in this run succeeded, and existing work was preserved.
