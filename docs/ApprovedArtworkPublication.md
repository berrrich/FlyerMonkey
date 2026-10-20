# Approved artwork pilot — publication inactive

The runtime `FlyerMonkey.Api/approved-artwork.json` contains exactly one approval: the selected Dynamo Comet Crush image below, recorded under FMAdmin at the actual UTC time of writing. API and clients default off. Mission 008C prepares approval locally; it does not enable publication or deploy.

## Mission 008C — completed single-image entry

The following is the exact installed local manifest. The user supplied FMAdmin and authorized recording the actual current UTC timestamp when writing the new entry. The inspected manifest was empty, so there was no earlier approval timestamp to preserve. Preset 1 is Comet Crush. No other artwork, including fixtures or Dynamo Classic, is approved by this selection.

```json
{
  "SchemaVersion": 1,
  "Entries": [
    {
      "ProductID": 102,
      "ArtworkId": "28071cca-5456-4f78-b621-28c504a3440a",
      "Sha256": "D83EC0500BD2152E02E571BF2B7E825E31AD4DC1565DF49A78F6AD1DE99E4817",
      "Length": 164538,
      "Preset": 1,
      "Provenance": "generated",
      "ApprovedBy": "FMAdmin",
      "ApprovedUtc": "2026-10-19T09:13:58.5307916+00:00"
    }
  ]
}
```

Retain API ApprovedArtwork__Enabled=false. The API must be published/republished with the updated manifest to update Azure: it reads a local deployed content file, not Blob metadata or the developer's working tree. Web also needs the prepared approved-artwork display deployed and its independent flag enabled in a later approved activation. MAUI remains a separate opt-in build.

Later activation, requiring separate authorization: publish API with the valid one-entry manifest while disabled; verify baseline and the approved route's 404; enable the API flag; verify Product 102 returns image/jpeg, 164538 bytes, the approved SHA-256 and no-store, while an unapproved product and private galleries remain denied. Then deploy/enable the Web display and verify /products/102, original-image fallback, artwork caption and navigation. Do not send generation requests. Immediate rollback switch: API ApprovedArtwork__Enabled=false; the Web flag may also be disabled. Revoke persistently by removing the entry and republishing; do not delete or overwrite artwork.

## Publication boundary

`GET /api/products/{productId:int}/approved-artwork/image` is intentionally a public, curated image endpoint. Disabled and unapproved requests return 404 before storage/credential resolution. It does not expose artwork IDs, user history, request fingerprints, Blob URLs, SAS or credentials. Responses use no-store, including errors. Storage/validation failures return 503; cancellation propagates.

The manifest is a deployment-controlled allowlist, not user input. Each entry requires ProductID, ArtworkId, Sha256 (JPEG), Length, Preset (existing enum), Provenance=`generated`, ApprovedBy and ApprovedUtc (UTC). SchemaVersion is 1. Duplicate products, malformed entries and the known fixtures are denied. The separate reader checks the private container, exact metadata association, preset and JPEG length/hash on each request. It never lists, uploads, creates a container, changes policy or invokes generation. Missing objects return 404. Invalid approval or altered content fails closed. Blob hashes pin content but do not prove provenance; an administrator must review the exact image and association before adding any entry.

Only the single entry above is installed in the local runtime manifest. Revoke by removing an entry or disabling the API flag. There is no response cache. An already downloaded image cannot be recalled from a user, and an in-flight request may finish during revocation. Deployment/RBAC control is the administrative approval boundary; no public admin endpoint exists.

The existing Development factory/store, generation guards, private-gallery routes, five presets and local MAUI gallery remain unchanged. Canonical images and SQL are not modified. Public artwork is a separate labelled figure on Product Detail; no replacement or automatic promotion occurs.

## Configuration — not applied

API App Service, remaining in Production:

- `ApprovedArtwork__Enabled=true` only after explicit publication approval; absent/false is disabled.
- `ApprovedArtwork__ServiceUri=https://flyermonkey.blob.core.windows.net/` (nonsecret). Container is fixed to `crater-artwork`.
- System-assigned managed identity with Storage Blob Data Reader scoped to that private container, and an existing permitted network path. The production reader uses ManagedIdentityCredential only, with no developer credential fallback. No account key, SAS or new storage connection string.

Do not use `CraterArt__Provider=Azure` to enable this path; the original factory remains Development-only. Do not change CraterIt generation or fixture flags.

Web App Service:

- `ApprovedArtwork__Enabled=true` only at the separately approved UI trial.
- `ApprovedArtwork__ApiBaseUrl=https://flyermonkeyapi-g7htasdacxfzgbcd.australiaeast-01.azurewebsites.net/`.

The HTTPS GET-only client rejects redirects and bounds JPEG reads to 2 MiB. Disabled/404 responses render no extra figure. Errors show a small unavailable message while preserving the original image. Browser requests do not access Blob storage directly.

MAUI registers the same client disabled. A later approved build can opt in at the existing AddApprovedArtworkClient call with the deployed HTTPS root. This is independent of AddMauiCraterClients and requires no networking-policy changes. Local Debug gallery access remains available as before; cloud approved artwork requires neither localhost nor ADB reverse.

## Mission 008E — Web release preparation

The user resolved the API feature-disabled condition by deleting/recreating and applying the single Azure setting; no API binding-code correction was needed. A GET-only verification returned HTTP 200, image/jpeg, 164538 bytes and the approved Dynamo SHA-256. The live Web Product Detail probe timed out, so the current public UI is not verified.

No production code correction is needed for Web display. Existing ProductDetail, ApprovedArtworkDisplay and ApprovedArtworkApiService already support the separate approved image. The last local Web Deploy output lacks both the display component and approved-client registration; configuration alone will not update that old build. Prepare a Web-only Release publish using the existing `FlyerMonkey - Web Deploy` profile, target App Service `FlyerMonkey`. No API publish or MAUI build is required for this release.

After separate deployment/configuration approval, set these two application settings on the **Web App Service FlyerMonkey**, independently of the API settings:

```text
ApprovedArtwork__Enabled=true
ApprovedArtwork__ApiBaseUrl=https://flyermonkeyapi-g7htasdacxfzgbcd.australiaeast-01.azurewebsites.net/
```

No key, SAS, Blob URL or storage permissions belong on the Web client. Keep the Web environment Production: the existing Web registration then disables private saved history and Crater generation regardless of the approved-image flag. MAUI's existing no-argument AddApprovedArtworkClient registration remains disabled. Canonical images and offer thumbnails are unchanged.

Shortest release procedure, not executed:

1. Retain a verified previous Web deployment/package for rollback. In Visual Studio select FlyerMonkey.Web, Release, Publish, existing FlyerMonkey - Web Deploy profile; verify target FlyerMonkey.
2. Publish Web only. Apply the two Web settings above (the API setting does not enable the Web client). Do not change the API, storage, SQL or MAUI settings.
3. Open `https://flyermonkey-fmaqaub3gqhhfzez.australiaeast-01.azurewebsites.net/products/102`. Verify the approved artwork and creative/AI-generated caption, separate original-image area/trolley placeholder, navigation and reload. Check an unapproved product, Products and Offers; no creative thumbnails, generation controls or private saved history should appear.
4. Immediate UI rollback: Web ApprovedArtwork__Enabled=false. If the release itself fails, restore the verified previous Web package. Do not disable the working API merely to hide the Web image.

Offline rendering checks exercise the actual Shared Product Detail and component with HTTPS client registration and synthetic responses: approved image/caption, null-canonical placeholder, absent private controls, unapproved 404, disabled flag/no request and recoverable API failure. No browser, Azure change, publish or deployment is performed by those tests.

## Prepared single-artwork importer — do not run live without approval

Project: `tools/ImportDynamoArtwork/ImportDynamoArtwork.csproj`. Default invocation is disabled and performs no source reads, credential resolution or traffic.

After separate approval only:

```powershell
dotnet run --project tools/ImportDynamoArtwork/ImportDynamoArtwork.csproj --no-launch-profile -- --live-approved-import "<exact local Dynamo artwork directory>"
```

It pins ProductID 102, Comet Crush, artwork ID `28071cca-5456-4f78-b621-28c504a3440a`, request ID, metadata bytes and JPEG bytes. JPEG SHA-256 is `D83EC0500BD2152E02E571BF2B7E825E31AD4DC1565DF49A78F6AD1DE99E4817`, length 164538. Original metadata SHA-256 is `FB70CD7A12720B8C8461CA9D6B65E0D06C4D555AFA96DB9581CF62130D7CF37A`. No fixture or other source passes validation. Original files are only read.

The destination is fixed to flyermonkey/crater-artwork. An existing authorized developer identity is used. The tool checks privacy and absence, uploads JPEG then unchanged metadata with If-None-Match:* and no SDK retries, and verifies both byte-for-byte. It creates no approval. Existing or partially created objects block reruns; interrupted writes can leave an orphan or a committed pair after a lost response. There is no delete, repair, overwrite, automatic retry, container creation or policy update. Coordinate with the local writer before live use; this tool does not provide distributed request reservations.

## Offline validation and next checkpoints

Restore the new project references using existing dependency versions, then run:

```powershell
dotnet run --project tests/ApprovedArtwork.Checks/ApprovedArtwork.Checks.csproj --no-launch-profile
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-PreCommitGate.ps1
```

The new suite uses synthetic JPEGs and an in-memory SDK transport at offline.invalid. It needs no local artwork, credentials, SQL or Azure. Import tests call an internal copy primitive with synthetic bytes; production source validation remains pinned to the reviewed originals. The operator CLI alone exposes the live flag. Tests do not invoke it.

The exact two-object Dynamo import has completed and the user reports the API identity/RBAC and disabled settings are configured. The local one-entry approval now records FMAdmin and its UTC timestamp. Next checkpoints: (1) separately authorize API publication while disabled; (2) separately authorize publication activation; (3) Web deployment/UI enablement; (4) MAUI opt-in build and physical-phone test without forwarding. Mission 008C does not authorize these live actions.

## Future backlog

- Read-only Crater Artwork Inventory tool: list local artwork IDs, ProductIDs, presets, creation dates and approval status. Report approval from the explicit publication manifest; do not infer it from preset or storage location. Backlog only; not implemented in this checkpoint.

### Mission 009 closeout — deferred design ideas

These are planning items only. They do not authorize implementation, account work,
publication, production configuration changes or new generation access.

- Reduce customer-facing technical text.
- Explore an artwork-first Product Detail with creative captions.
- Keep authentic product imagery separate from generated creative artwork.
- Show current and historical offer prices with accurate retailer and validity dates.
- Use "As low as" only when justified by available pricing data.
- Invite signed-in users to request future sale notifications.
- Move preset selection into a second comet-dialog stage with Back and Cancel.
- Prepare Facebook-ready public artwork/product pages with suitable social previews.
- Test read-only untethered cellular access against the published HTTPS API; keep generation restrictions intact.
