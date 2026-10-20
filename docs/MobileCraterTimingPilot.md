# Mission 009B — Pixel 6 Pro timing pilot

Android Debug alone uses http://localhost:5145/ for the existing generation POST.
The existing local header, loopback checks, Development flag and API opt-in remain
required. Redirects are disabled; generation HTTP timeout is three minutes.
Release and other MAUI platforms remain disabled. No new endpoints are added.
Approved artwork continues through Azure HTTPS independently; original product
images and trolley placeholders are unchanged. Generated images remain unapproved.

The shared Product Detail reuses all five presets, cancellation, saved history and
preview. Progress is indeterminate: the API does not stream provider progress.
After the generation response, progress changes to loading the saved image.
Tap-to-display includes the POST, save, JPEG GET, decode and a double animation-frame
paint opportunity. This is a WebView display estimate, not physical screen scanout.
The JPEG GET has its existing 30-second timeout; display confirmation has a further
15-second bound. Keep the phone awake and the app foregrounded during measurement.

Successful API responses add X-CraterIt-Outcome (fresh/replay),
X-CraterIt-Generation-Ms and X-CraterIt-Save-Ms. Replay durations are zero because
the saved result is returned without generation or saving. Old servers without
these headers show timing unavailable rather than claiming a fresh generation.
The response body and persisted metadata format are unchanged.

## First physical test — run manually after review

1. In a normal developer PowerShell terminal at the repository root, ensure the
   existing server-side FLYERMONKEY_SQL_CONNECTION and OpenAI:ApiKey configuration
   is available. Never put the provider key in the mobile app. Select Local
   artwork storage for the first timing run:

   ```powershell
   $env:CraterArt__Provider = 'Local'
   dotnet run --project FlyerMonkey.Api/FlyerMonkey.Api.csproj --launch-profile http
   ```

   Wait until it reports listening on http://localhost:5145. The existing http
   profile sets Development and CraterIt__Enabled=true. Restart any old API
   instance so the new timing headers are present. Do not use a LAN listener,
   public tunnel, emulator address or production generation API.

2. In another developer terminal identify the authorized physical Pixel and
   forward only the required port:

   ```powershell
   adb devices -l
   adb -s <pixel-serial> reverse tcp:5145 tcp:5145
   adb -s <pixel-serial> reverse --list
   ```

3. In Visual Studio select FlyerMonkey/FlyerMonkey/FlyerMonkey.csproj, Debug,
   net10.0-android and the Pixel 6 Pro. Run/install the updated app locally.
   Keep internet enabled for products and approved artwork.
4. Open Dynamo Product 102 after API startup. Confirm the trolley/original area,
   approved Comet Crush artwork, local saved history and Crater It! controls.
   If history errors, reopen the page after fixing API/forwarding before testing.
5. Choose Comet Crush (or record another selected preset). Tap Crater It! once.
   This manual step invokes the paid provider. Do not double tap or automatically
   retry. Observe elapsed progress, then the separately labelled generated image.
6. Record product/preset, fresh versus replay, request ID, tap-to-display seconds,
   server generation seconds and save seconds. For Local storage, verify the
   new DATA/CraterArt/<artwork-id>/ directory contains artwork.jpg and metadata.json.
   Reopen Product Detail and confirm it appears in saved history. Approved Azure
   artwork must remain unchanged; this result is not automatically approved.
7. On cancellation, timeout or failure, inspect saved history before retrying.
   Cancellation after bytes exist does not discard the independently bounded save.
   An unchanged retry on the same page retains a pending request ID until a saved
   response is received. A completed request or page reload starts a new ID;
   pressing Crater It! again then can create another paid image. Selecting history
   only reads an image and is not a generation replay.
8. After testing, remove the forwarding:
   `adb -s <pixel-serial> reverse --remove tcp:5145`, then stop the local API.

No paid calls, deployment, publishing, commits or pushes were performed during
implementation. Offline tests exercise fake provider/HTTP behavior. Physical
timing and JavaScript paint confirmation require this manual phone test.

## Mission 009C — completion reveal and progress polish

Only a successful generation completion automatically reveals the preview. The
image must load, decode and reach a paint opportunity before a smooth centered
scroll. Cancellation, product navigation, disposal, detached elements and changed
image sources prevent a stale reveal. A scroll failure does not turn successful
image display into a generation error. Gallery selection and initial history
loading retain their existing behavior and do not automatically scroll.

A gently moving comet accompanies the existing indeterminate progress bar.
There is no completion percentage or estimated duration. Elapsed time remains
unchanged. Reduced-motion settings disable the comet animation and smooth scroll.

### Verify without another paid image

1. Start the local Development API and reverse port 5145 as described above.
   Run the updated Android Debug app on the Pixel. Open the steak product and
   select the existing Pull the Lever result in saved history. Confirm it displays
   and ordinary selection does not scroll unexpectedly.
2. To exercise the same decode/paint/reveal helper with saved bytes, attach the
   Android Debug WebView developer console. With that saved preview selected,
   scroll down to the Crater controls and run:

   ```javascript
   const image = document.querySelector('.fm-artwork figure:last-child img');
   const timing = await import('./_content/FlyerMonkey.Shared/craterTiming.js');
   await timing.afterPaint(image);
   timing.reveal(image, image.getAttribute('src'));
   ```

   Confirm a comfortable centered smooth reveal on the phone. This does not POST
   or generate an image. Enable Android reduced-motion settings and repeat to
   verify the immediate reveal instead. Reopen the page to reset console variables
   if needed.
3. With the API unavailable, reopen the page and confirm local-history errors do
   not scroll to an absent result. Restore the API before further testing.
4. During the next separately intended manual generation, verify the elapsed
   timer continues, the comet animates without a percentage, and the result is
   revealed only once it loads. Cancel a run or navigate away before completion
   to verify there is no late scroll. These full completion/cancellation device
   checks cannot be proved by selecting history alone; they were not run during
   implementation and must not be automated against the paid provider.

Offline helper checks:
`node --preserve-symlinks --preserve-symlinks-main tests/CraterPreview.Checks.mjs`.
They cover decode/paint ordering, scroll placement, detached/replaced/incomplete
images, failed decode and reduced motion using DOM/frame doubles. Actual WebView
scrolling, visual animation and navigation during generation remain device checks.
