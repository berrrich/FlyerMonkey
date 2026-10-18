# Crater It! implementation

Mission 004A supersedes the temporary-preview behavior below: generated artwork is now saved automatically by the API. See [Mission 004A implementation and restart verification](CraterArtPersistence.md).

The following sections record the earlier missions. They originally implemented Product Detail previews in memory without persistence.

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

## Mission 003 — creative presets (2026-10-16)

Added shared Services/CraterItPromptBuilder.cs with Crater Classic, Comet Crush, Alien Exposure, Comety and Pull the Lever definitions. Classic preserves the existing Product Detail prompt opening, metadata order, 300-character field bounds and plain background; restrained fidelity instructions follow it. Other styles use the same metadata without the plain-background constraint. Prompts exclude movie-specific scenes and copyrighted characters.

Product Detail now offers a labelled preset selector and responsive original/generated image comparison. The original remains the authoritative reference. Each successful temporary preview retains its preset caption; failed regeneration preserves the previous preview. Existing cancel, loading, errors, duplicate-request and stale-result safeguards remain.

Products now uses responsive cards with a compact red SVG comet, accessible action name and hover/focus tooltip. The comet navigates to Product Detail using ?crater=true#crater-controls, where controls receive focus after loading. Navigation never starts generation. Actions remain gated by existing local client enablement.

Changed files: both product pages, Product Detail CSS, new Pages/Products.razor.css, new Components/CraterComet.razor, new Services/CraterItPromptBuilder.cs, the package-free checks project/program and this report. Both existing Razor UTF-8 BOMs are preserved.

API guards, generation clients, reviewer workflow, provider/model settings, dependencies, canonical product/offer records, SQL, Azure storage and deployment configuration are unchanged. No persistence, accounts, credits or save controls were added.

Validation: API, web and Windows MAUI builds with --no-restore passed. Web compilation reported two existing unused-field warnings in Offers.razor; Windows reported three existing PRI249 qualifier warnings. All 21 package-free checks passed: the original 15 plus Classic wording/order, five distinct presets, prompt bounds for every preset, absent metadata, creative background constraints and unknown preset rejection. An asparagus metadata fixture protects prompt assembly; it is not evidence of the historical asparagus request or generated-image quality. git diff --check passed.

No browser, mobile platform other than Windows, live provider request or real image-quality assessment was run. Manual checks remain for tooltip/focus behaviour, narrow-screen comparison, cancellation, error recovery and navigation during generation. Generation is still text-only and cannot promise exact packaging/branding. Cancellation may not prevent a provider charge.

Future retained gallery: use a separate artwork entity with product/user associations, its own image reference, preset, prompt version, creation time and review status. Keep canonical Product.ImageBlobPath independent. This is design direction only; no persistence types, schema or endpoints are introduced here.
