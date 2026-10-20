# Mission 009C — Android Debug approved artwork

Final Mission 009 closeout also includes the separately authorized Android Debug
generation pilot described in [MobileCraterTimingPilot.md](MobileCraterTimingPilot.md).
Approved HTTPS display remains independent of that localhost-only path.

Android Debug explicitly enables the existing ApprovedArtworkApiService against
https://flyermonkeyapi-g7htasdacxfzgbcd.australiaeast-01.azurewebsites.net/.
Release and other MAUI platforms retain the disabled default. Web registration,
API settings, approval manifest and generation restrictions are unchanged.

The shared ApprovedArtworkDisplay on Product Detail performs only the existing
product-scoped approved-image GET. It does not require the local API or ADB reverse.
The original image/trolley placeholder remains separate. Products and Offers
thumbnails remain canonical. A 404 adds no image; network/API errors or timeout
show an unavailable message. JPEG reads remain limited to 2 MiB, HTTPS only,
without redirects, with a 30-second deadline covering headers and body.

## Pixel 6 Pro smoke test

1. Build the Android Debug app:
   `dotnet build FlyerMonkey/FlyerMonkey/FlyerMonkey.csproj -f net10.0-android -c Debug --no-restore`.
2. In Visual Studio select the same MAUI project, Debug, net10.0-android and the
   authorized physical Pixel 6 Pro. Run/install that Debug build. This installs
   locally on the phone; it does not publish any server application.
3. Keep the local Development API stopped. In a normal developer terminal run
   `adb devices -l`, identify the Pixel serial, then
   `adb -s <pixel-serial> reverse --list`. If tcp:5145 is present, remove only it
   using `adb -s <pixel-serial> reverse --remove tcp:5145`.
4. With internet access on the phone, open Dynamo Product 102 from Products.
   Verify the approved Comet Crush image appears as a separate figure with the
   approved creative-artwork caption. Verify the original image or trolley
   placeholder is still present. A local saved-history error is independent and
   may still appear because that gallery intentionally retains localhost routing.
5. Return to Products and Offers. Confirm their thumbnails have not changed to
   creative artwork. Open an unapproved product: no extra approved figure or
   unavailable message should appear for a normal 404.
6. Disable phone internet, leave and reopen Product Detail. Confirm product
   metadata/original placeholder remain usable if already loaded and artwork
   failure shows the unavailable message. For a deterministic artwork-only
   failure, use the offline render tests: a cold product load also needs the
   public product API and can fail independently. Restore internet and reopen
   Product Detail to retry approved artwork.
7. The final Android Debug build includes local generation controls and the comet
   invitation; neither should generate on its own. Do not press the actual
   generation button in this read-only test. Release/non-Android MAUI keeps both
   generation and approved-artwork display disabled.

No deploy, publish, commit or push is part of this mission. Physical display
verification must be recorded separately from offline tests and build success.
