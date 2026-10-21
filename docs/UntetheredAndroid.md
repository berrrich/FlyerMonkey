# Mission 010 — untethered Android and Release readiness

## Investigation before implementation

All MAUI catalog clients already used the published API, with no emulator-specific
switch. The gap was that approved artwork was enabled only in Android Debug.

| Configuration | Products/offers/original images before this mission | Approved artwork before | Private Crater before |
| --- | --- | --- | --- |
| Android emulator Debug | Azure HTTPS | Azure HTTPS | localhost:5145, needs reverse forwarding |
| Physical Android Debug | Azure HTTPS | Azure HTTPS | localhost:5145, needs reverse forwarding |
| Android Release | Azure HTTPS | Disabled | Disabled |
| Windows Debug/Release | Azure HTTPS | Disabled | Disabled |

The public root is
`https://flyermonkeyapi-g7htasdacxfzgbcd.australiaeast-01.azurewebsites.net/`.
GET-only live verification returned:

| Route | Result | Use |
| --- | --- | --- |
| `api/products` | 200, 25 products | Product list and current Product Detail lookup |
| `api/products/102` | 200 | Supported individual lookup; mobile currently uses the list |
| `api/offers` | 200, 24 offers | Offer Detail lookup |
| `api/offers/current` | 200, 8 offers | Current offers |
| `api/offers/previous` | 200, 16 offers | Previous offers |
| `api/products/102/image` | 404 | Dynamo has no canonical image; retain packaged trolley fallback |
| `api/products/102/approved-artwork/image` | 200, JPEG, 164538 bytes | Approved Dynamo Comet Crush |
| `api/images/artwork?productId=102` | 404 | Expected denial of private history on production |

Counts describe this investigation, not guarantees about future data. JSON field
names match existing Product and OfferSummary models. Current Product Detail's
Compare Offers section is still a placeholder. Production does not provide public
access to arbitrary saved/generated history; Release must not attempt to use it.
No generation, approval or product-creation requests were sent.

The host index, Bootstrap/app/scoped CSS, placeholder image and comet modules are
packaged. `_framework/blazor.webview.js` is provided by the MAUI WebView framework.
Relative `_content`/`_framework` URLs use the internal hybrid asset origin, not a
development server. The internal WebView origin must not be replaced with Azure.
Only the deliberately gated local Crater clients use localhost/ADB reverse.

## Minimal implementation

- PublicApiClientRegistration owns the explicit HTTPS root, canonical image URL
  helper and MAUI product/offer/approved client registration. Native public clients
  use 30-second timeouts and do not follow redirects.
- Android Debug and Release both enable the existing approved-artwork GET client.
  Other MAUI platforms keep their previous approved-artwork default off.
- Only `ANDROID && DEBUG` enables localhost generation/history. Release retains
  null Crater base addresses, no generation controls, no floating invitation, and
  no private-history requests. The shared library still contains the Debug routing
  code; its presence as an inactive string is not a Release network dependency.
- Canonical image components use the same published-root helper. Original imagery,
  trolley fallback and approved creative artwork remain separate.
- Product list/detail show a safe error/retry state for network or API failure.
  Missing products no longer remain on the loading screen. Existing offer error
  handling and approved-artwork 404/failure behavior remain in place.
- The base manifest explicitly denies cleartext. Android Debug's existing network
  security overlay continues to allow only exact localhost/127.0.0.1 on API 24+.
  Release excludes that overlay/resource. No TLS trust bypass is used.

No server controllers, approval manifests, storage, prompts, authentication or
Azure configuration are changed. New authentication and public galleries are out
of scope.

## Release prerequisites and limits

- Package ID remains `com.companyname.flyermonkey`; display version is `1.0`, version
  code `1`, minimum Android API 24. These template identity/version values need an
  operator decision before any public release.
- Release evaluates to embedded assemblies, partial trimming, AOT, ARM64/x64 and
  APK/AAB outputs. JSON reflection is enabled in this configuration. Offline model
  tests do not prove trimmed/AOT behavior on a physical device.
- No custom signing keystore/alias/password is configured in the project. Do not
  treat a locally test-signed Release APK as a store/distribution release. Keep any
  future production signing material outside the repository.
  The built Release APK passed apksigner verification with signer
  `CN=Android Debug, O=Android, C=US`; it is not production-signed.
- Debug and Release share the package ID. Updating the installed app requires a
  compatible signer and nondecreasing version code. If Android rejects the signer,
  stop and choose a signing strategy; do not uninstall and erase app data casually.
- Existing API source includes `POST api/products` without an authorization
  attribute or a visible global authentication policy. Deployed gateway/App Service
  protections are not established by this source audit. No write probe was made.
  Review this existing server-write exposure separately before public distribution;
  the MAUI clients added here expose only the existing read operations.
- Cellular access, actual Release launch, trimmed-model behavior, WebView rendering,
  carrier TLS/DNS and image loading still require the physical smoke test. A
  successful desktop GET/build is not a completed phone/network verification.

## Validation commands

Use installed/restored .NET 10 MAUI and Android SDK/JDK tooling. Do not run a
simultaneous IDE build against the same configuration outputs.

```powershell
dotnet run --project tests/CraterIt.Checks/CraterIt.Checks.csproj --no-restore --no-launch-profile -p:UseAppHost=false
dotnet run --project tests/ApprovedArtwork.Checks/ApprovedArtwork.Checks.csproj --no-restore --no-launch-profile
dotnet run --project tests/CraterArtwork.AzureChecks/CraterArtwork.AzureChecks.csproj --no-restore --no-launch-profile
node --preserve-symlinks --preserve-symlinks-main tests/CraterPreview.Checks.mjs
dotnet build FlyerMonkey/FlyerMonkey/FlyerMonkey.csproj --no-restore -f net10.0-android -c Debug
dotnet build FlyerMonkey/FlyerMonkey/FlyerMonkey.csproj --no-restore -f net10.0-android -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tests/AndroidReadOnlyPackage.Checks.ps1 -Configuration Debug
powershell -NoProfile -ExecutionPolicy Bypass -File tests/AndroidReadOnlyPackage.Checks.ps1 -Configuration Release
```

The existing invitation browser checks can consume the synthetic `--ux-fixture`
output from ApprovedArtwork.Checks. Inspect merged Debug/Release manifests, packaged
assets and signer after building; build success alone is insufficient.

Observed results: 118 approved/public-client/model/failure checks, 112 Crater checks,
54 offline Azure checks, preview helper checks, and Pixel-size invitation checks
(normal/reduced motion, native Tab/Escape) passed. Web and Android Debug builds
passed. The normal optimized Android Release build passed with zero warnings/errors
after allowing the SDK to write metadata sidecars into its NuGet cache outside the
filesystem sandbox; trimming/AOT were not disabled. Debug/Release package audits
passed 20/21 checks respectively, including all expected assets and Release's
non-debuggable/cleartext-denial/Debug-resource-exclusion rules. The merged manifests
target API 36 with minimum API 24. The local Release APK is 103638301 bytes and
includes ARM64/x64 plus existing transitive dependencies; package-size optimization
was not attempted in this minimal mission. No APK was installed or distributed.

## Physical Pixel 6 Pro — operator-run, read-only

1. Install or launch the locally built **Release** APK using the operator's normal
   local-install workflow. No APK is distributed by this mission. If installing
   with ADB, use `adb -s <pixel-serial> install -r <local-release-signed.apk>` only
   after checking its signer/version. Do not use an old Debug APK for this test.
2. Stop the local Development API. Remove existing port forwarding if present:
   `adb -s <pixel-serial> reverse --remove tcp:5145`. Disconnect USB. Force-stop and
   reopen the Release app from its launcher to check cold startup without a host.
3. Disable Wi-Fi and confirm mobile data is enabled. Browse Products, current
   Offers, Previous Offers and an Offer Detail. Verify retailer, price and dates.
4. Open Dynamo Product 102. Verify the original trolley fallback and separately
   labelled approved Comet Crush JPEG. Test an unapproved product: no approved
   figure is expected for a normal 404. Scroll and navigate back normally.
5. Confirm there is no active Crater style picker, generation button, floating
   invitation or private saved-history gallery on Product Detail. Offer Detail's
   pre-existing disabled Coming Soon button is not a generation capability.
6. Disable mobile data temporarily and reopen Product Detail/Products. Verify a
   friendly failure/retry state, not an unhandled error or indefinite loader.
   Restore data and retry. Approved-image failures must not replace canonical
   images. The app does not promise offline cached browsing.
7. Reconnect USB, restore Wi-Fi if desired, and run the normal **Debug** build. Start
   the local API using its existing http Development profile, restore
   `adb -s <pixel-serial> reverse tcp:5145 tcp:5145`, and reopen Product Detail.
   Confirm public HTTPS browsing plus the gated local gallery/controls. Do not
   initiate generation merely to test connectivity.

Future design work is recorded in ApprovedArtworkPublication.md under Mission 011
(Top Banana) and Mission 012 (Crater artwork experience). It is not implemented here.
No commits, pushes, merges, Azure deployments, Play submissions, APK distribution
or paid generation are part of this mission.
