# Mission 010A — Product Detail invitation

The existing saved gallery and single Crater control section now sit directly below
the artwork column. Presets, prompts, timer, generation, saving and completed-image
reveal behavior are unchanged. The floating comet and native modal invitation use
the same ImageGeneration.IsEnabled gate as the existing controls; disabled clients
receive neither. No new service or API is involved.

The 48px floating button has an accessible name, keyboard focus outline, a gentle
animation and reduced-motion support. Product Detail reserves a narrow right gutter
so the button cannot cover its controls or images. Below the existing 640.98px
navigation breakpoint it sits 104px plus the safe-area inset above the viewport
bottom, clear of the normal bottom navigation. Large Android font/display settings
and actual gesture insets still need a Pixel check.

Final Mission 009 polish reuses the existing CraterComet SVG and its colours inside
a white circular surround. No new icon style is introduced. Approved-artwork load
failures now explicitly distinguish that selection from saved/generated previews;
an approved API failure does not hide a valid Alien Exposure preview. Approval and
access rules are unchanged. Deferred design ideas are recorded under Future backlog
in [ApprovedArtworkPublication.md](ApprovedArtworkPublication.md).

The invitation opens only when requested. Native showModal provides background
inertness and keyboard focus containment. Initial focus is on No. No and Escape
return focus to the comet; Yes closes the modal, focuses the one existing control
section and scrolls it into view. It never invokes generation. Reduced motion uses
an immediate scroll. Detached dialogs cannot reopen after navigation.

## Offline validation

Generate a synthetic Product Detail fixture using the existing offline checks:

```powershell
$cometFixture = Join-Path $env:TEMP 'FlyerMonkey-CometProduct.html'
dotnet run --project tests/ApprovedArtwork.Checks/ApprovedArtwork.Checks.csproj --no-restore --no-launch-profile -- --ux-fixture $cometFixture
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Test-CometInvitation.ps1 -FixturePath $cometFixture
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Test-CometInvitation.ps1 -FixturePath $cometFixture -ReducedMotion
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Test-CometInvitation.ps1 -FixturePath $cometFixture -Width 1280 -Height 900
```

The browser test uses real rendered Razor markup, production page/layout CSS and
invitation JavaScript, with synthetic images and locally connected event handlers.
It emulates the requested viewport, blocks remote hostname resolution, and never
loads Blazor or connects to an API. Checks cover no horizontal overflow, reserved
content clearance, mobile navigation clearance, dialog bounds, initial focus,
Yes/No, native close requests, trusted Tab/Escape and reduced motion. Screenshots
and browser logs remain in the reported temporary directory. These checks validate
browser behavior; they do not replace MAUI event-routing/device verification.

## Pixel 6 Pro smoke test — no new generation needed

1. Run the updated Android Debug app using the established local Development API
   and ADB reverse configuration. Open a product with existing saved artwork.
2. Confirm the gallery and the one style/generation section are below the artwork.
   Select another saved image and hide/show artwork; existing behavior should remain.
3. Confirm the comet clears Offers/Previous/Products and does not cover page buttons
   while scrolling, in portrait/landscape and with larger Android text settings.
4. Tap the comet. Read both invitation sentences. Choose No; it closes without any
   generation. Reopen and choose Yes; the style picker/control section is revealed
   and focused. Do not press the actual Crater It! generation button.
5. With a hardware keyboard, check Tab/Shift+Tab containment, Escape dismissal and
   focus return. With TalkBack, check the comet name, dialog title and action labels.
6. Enable reduced motion: the comet should stop drifting and control navigation
   should be immediate. Navigate to another product/page: no invitation should open
   itself or persist. Repeat opening/closing to ensure it remains available on demand.

No paid provider requests, deployment, publishing, commits or pushes are required.
