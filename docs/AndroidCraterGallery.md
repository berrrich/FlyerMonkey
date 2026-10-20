# Mission 005D — read-only Android Debug Crater gallery

Mission 009B supersedes the generation-disabled Android Debug policy described
below. Android Debug now enables the existing local generation client, with a
three-minute timeout and redirects disabled. Release and other MAUI platforms
remain disabled. See [Mobile Crater timing pilot](MobileCraterTimingPilot.md).

Web and MAUI continue to share FlyerMonkey.Shared/Pages/ProductDetail.razor. Saved-artwork viewing now uses CraterArtworkApiService, which exposes only history and JPEG GET operations. ImageGenerationApiService retains only generation. The gallery is rendered and loaded using its own enablement condition, with its own loading/error messages; generation controls remain independently guarded. Original product images, presets/prompts, API endpoints/access checks, storage configuration and existing saved files are unchanged.

CraterClientRegistration registers both clients. Web keeps its existing Development + enabled flag + loopback HTTP/HTTPS policy, three-minute generation timeout and disabled redirects. Android Debug gets a read-only gallery address of http://localhost:5145/ and a 30-second timeout. Generation has no MAUI address in any configuration. Release and other MAUI platforms have neither gallery nor generation addresses. Both clients keep the local API header, and gallery requests never POST or follow redirects.

## Local setup

The following are manual setup instructions; no application host, ADB forwarding, live Azure access or paid generation was started during implementation.

1. Use previously saved artwork associated with a product visible in the app. Start the existing local API from the repository root using its HTTP development profile:

   ```powershell
   dotnet run --project FlyerMonkey.Api/FlyerMonkey.Api.csproj --launch-profile http
   ```

   That existing profile listens on localhost:5145 and opts in to CraterIt in Development. Supply the application's existing server configuration securely as usual. Leave the artwork store on the existing Local default for this test; do not enable Azure or change production settings. No image-provider call is made by history/image retrieval.

2. Connect a trusted Android device with USB debugging, or start an emulator. Use Android SDK Platform Tools (or the Visual Studio Android ADB command prompt) to identify the intended device and forward its local port:

   ```powershell
   adb devices
   adb -s <device-serial> reverse tcp:5145 tcp:5145
   adb -s <device-serial> reverse --list
   ```

   Replace the serial placeholder before executing. Use adb from its SDK platform-tools directory if it is not on PATH. The device's localhost:5145 is forwarded to the host's loopback API. Do not substitute 10.0.2.2, a LAN listener, public tunnel or deployed API: the existing API still requires loopback remote address and Host, Development, enabled flag and the local header. If the host resolves localhost to IPv6 only, verify the listener/ADB connection path rather than weakening the guard.

3. Build/run the Android **Debug** app from Visual Studio against that device. Product/offer loading retains its existing deployed API configuration; only saved-artwork requests use the local development API. Open the matching Product Detail, select history entries and verify each caption/image. Generation controls must be absent and the page must say generation is disabled on this device.
4. Navigate away/back and restart the app/API. The same history must return without generation. Stop forwarding or the API and confirm a recoverable gallery error. No new artwork should be created by viewing. A fresh local store with no saved records correctly displays an empty gallery.
5. Remove the forwarding rule when finished:

   ```powershell
   adb -s <device-serial> reverse --remove tcp:5145
   ```

ADB forwarding is development transport, not public authentication. API endpoints remain capable of paid generation for their existing trusted local Web workflow, but Android has no generation client address or UI action; its read-only client has no generation/upload API.

## Android network policy

A Debug-only AndroidManifestOverlay references crater_debug_network_security_config.xml. The base denies cleartext; only exact localhost and 127.0.0.1 allow HTTP, with subdomains excluded. No certificate callback, custom trust anchor or TLS validation bypass is added. The original manifest is unchanged; Release excludes both overlay and resource. Android network-security configuration is enforced on API 24+; older platform limitations do not expand the gallery client's loopback-only destination checks or permit redirects.

References: [ADB reverse forwarding](https://developer.android.com/develop/ui/views/layout/webapps/access-local-server), [Android network security configuration](https://developer.android.com/privacy-and-security/security-config).

## Verification

Run the complete gate:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-PreCommitGate.ps1
```

The existing local suite now has 73 checks, including actual MAUI/Web DI registration, Android Debug GET-only retrieval, generation denial before HTTP, Release/non-Android disablement, Web policy preservation and redirect handling. The Azure suite remains 54 offline checks. Repository checks now include real Debug/Release Android MSBuild item evaluation and loopback-only/no-trust-override XML checks (49 total). The complete gate includes the Android Debug build as well as API/Web/Windows MAUI/Reviewer builds.

Complete quality gate result: PASS, exit 0. All 73 local/client checks, 54 offline Azure checks and 49 repository/policy checks passed. API, Web, Windows MAUI, Windows Reviewer and Android Debug builds each exited 0; final incremental builds reported no compiler diagnostics. The initial Android build reported only the two existing Offers.razor unused-field warnings. Git whitespace and API/prompt/original-manifest preservation checks exited 0.

The generated Debug manifest was inspected and references the intended network resource. Offline handler tests and Android compilation do not establish actual device-to-host networking, ADB behavior or runtime HTTP policy. Device/emulator gallery and restart tests remain manual. No live Azure connection or paid generation is required.

Changed application files: new shared CraterArtworkApiService.cs and CraterClientRegistration.cs; shared ImageGenerationApiService.cs and ProductDetail.razor; Web Program.cs; MAUI MauiProgram.cs and FlyerMonkey.csproj; new Android debug overlay and network resource. Tests/CraterIt.Checks links the new clients and adds behavioral checks. Existing 005C gate files are extended to include Android policy/build verification. Prior uncommitted 005C changes are preserved.
