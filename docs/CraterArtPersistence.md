# Mission 004A — persistent Crater artwork

Implemented 2026-10-18. The API saves each generated JPEG and its metadata before returning success. No second client save POST is used. No database, Azure, credentials, packages, deployment or production settings were changed. No commit or push was performed.

## Mission 005B — optional Azure adapter (2026-10-18)

Checkpoint: Mission 004A commit `4a7497cffab4d564c76c645c489ac8d250b76888`; working tree was clean before implementation. Work is on local branch `feature/crater-azure-storage`, without commits, pushes, merges or deployment.

`AzureCraterArtworkStore` now implements the existing interface. `CraterArtworkStoreFactory` selects it only with explicit Development configuration. Local remains the default, and the local implementation, endpoints, presets, canonical images and access checks are unchanged. No Azure resources, permissions, credentials, networking, production settings, SQL or existing artwork files were changed.

### Configuration and return to Local

No appsettings/launch/deployment configuration was edited. Leave all new settings absent to retain the existing DATA/CraterArt workflow. Explicit local selection is `CraterArt__Provider=Local` on the API process.

For a later, separately approved Azure smoke test, supply these nonsecret settings only to the local API process:

```text
CraterArt__Provider=Azure
CraterArt__Azure__ServiceUri=https://<account>.blob.core.windows.net/
CraterArt__Azure__Container=crater-artwork
```

Values Local/Azure are case-sensitive. The URI must be an HTTPS public-cloud Azure Blob account root, without a nondefault port, path, query/SAS, credentials or fragment. The container must explicitly equal crater-artwork; product-images is refused. Azure is rejected outside Development. Invalid configuration throws at store resolution rather than silently falling back. The factory constructs a client without making storage requests. Azure operation failures propagate through the existing storage-failure contract; outages never become empty history or unknown requests.

To return safely to Local, stop the local API, remove the API-process Azure override or set Provider=Local, and restart. Existing DATA/CraterArt remains intact. Azure artwork stays in Azure and will no longer appear in the local history; switching providers does not copy/import/delete artwork. Do not set the deployed API environment to Development to enable this adapter.

### Azure prerequisites — not provisioned or tested

An operator must separately arrange an existing private crater-artwork container in the intended account. A signed-in developer identity needs Storage Blob Data Contributor scoped to that container and network access through the account's existing rules. The role permits container properties/list/blob reads and writes; this implementation never issues create-container, policy-update or delete calls. No storage keys, SAS values or connection strings are required. DefaultAzureCredential uses an existing authorized developer sign-in; no sign-in or role assignment was performed during implementation.

Azure.Identity 1.14.2 was already resolved transitively through Microsoft.Data.SqlClient. The API now references that same version directly. The full resolved API library graph is unchanged. An explicit assembly alias disambiguates the credential type from a same-named type exposed by the installed Azure.Core assembly, without upgrading either package. Azure.Storage.Blobs remains 12.29.2.

### Publication, retrieval and limits

The adapter uses only artwork/{artworkId:N}/artwork.jpg and metadata.json in crater-artwork. Before operations, it checks the container name and reads its public-access property, refusing an explicitly public container without altering it. All blob writes use If-None-Match: * and no-store. JPEG bytes upload first; the manifest uploads last as the commit marker. Exact save retries compare existing content; differing content/request associations fail without overwrite.

Only valid committed manifests with an existing JPEG appear in product history. Known request lookup retains valid manifests whose JPEG has disappeared so the existing API refuses regeneration. Malformed manifests are logged and skipped. Authorization/network/container failures are explicit errors, including failures partway through enumeration. Missing blobs return missing results, distinct from a missing container. There is no directory rename or cross-blob transaction: interrupted saves can leave orphan JPEGs, and a lost response can report failure after commit. No deletion/cleanup is attempted; a later request lookup can discover a committed manifest. Corrupted/deleted manifests retain the existing limitations around request recovery.

This is a small, single-API-process Development adapter. History/request lookup scans manifests and does not add SQL, tags, distributed reservations, user ownership or public access. Container access can change after a check; administrators must keep it private. Selecting Azure does not bypass any Development/loopback/Host/header restrictions. The web client still calls the local API and MAUI remains disabled.

### Offline verification

The new tests/CraterArtwork.AzureChecks project links the adapter/factory and uses an in-memory HTTP transport for the real Blob SDK. Its host is offline.invalid, requests never reach sockets, and there is no live mode. Factory construction is exercised but credentials are not requested. The runner catches and prints failures and returns exit code 1 rather than generating an unhandled-exception Windows dialog; successful completion returns 0. UseAppHost=false is set only in this new test project.

Validation results:

| Check | Result | Exit code |
| --- | --- | --- |
| Existing CraterIt.Checks | 48 passed | 0 |
| New offline Azure checks | 54 passed | 0 |
| API build --no-restore | Passed | 0 |
| Web build --no-restore | Passed; two existing Offers.razor unused-field warnings | 0 |
| Windows MAUI build --no-restore | Passed | 0 |
| Restore from local package cache | Passed; no changes to existing API package versions | 0 |
| Git diff --check / preserved-source comparison | Passed | 0 / 0 |

New, untracked source/project files were also checked against /dev/null with git diff --no-index --check: no whitespace diagnostics (exit 1 denotes the new-file difference). Only normal LF-to-CRLF conversion notices were emitted. Existing artwork/staging ignore probes passed with exit 0. Final source comparison confirms the local store, interface, endpoints, access helper, generator, canonical image service, preset builder, Product Detail, artwork model and existing checks match the 004A checkpoint.

The first parallel API/web build collided on their shared output (CS2012, exit 1); a sequential API rerun passed with no code change. Initial compilation identified the credential alias and SDK overload requirements, both corrected before the passing tests. No live Azure tests, API host, browser, credentials or paid generation were used.

Commands (run from repository root):

```text
dotnet run --project tests/CraterIt.Checks/CraterIt.Checks.csproj --no-restore --no-launch-profile -p:UseAppHost=false
dotnet run --project tests/CraterArtwork.AzureChecks/CraterArtwork.AzureChecks.csproj --no-restore --no-launch-profile
dotnet build FlyerMonkey.Api/FlyerMonkey.Api.csproj --no-restore -v minimal
dotnet build FlyerMonkey/FlyerMonkey.Web/FlyerMonkey.Web.csproj --no-restore -v minimal
dotnet build FlyerMonkey/FlyerMonkey/FlyerMonkey.csproj --no-restore -f net10.0-windows10.0.19041.0 -v minimal
git diff --check
```

Coverage includes round trips/fresh-instance metadata, product ordering/isolation and multi-page lists, exact replays/conflicts, create-only writes, image/manifest failures, ambiguous completion, missing/corrupt records, mid-operation permission/container/download failures, network errors, private-container enforcement, early/mid-save cancellation, configuration validation, local default and non-Development denial. All storage fixtures are in memory; the existing local suite creates its usual temporary test data.

### Later smoke test — separate live approval required

1. Review the branch and separately authorize live storage access, any resource/permission preparation and any paid generation. None is included in this implementation run.
2. Confirm the correct account/private container, developer RBAC and permitted network path. Do not alter canonical container settings or expose either local host.
3. Start only the local API/Web in opted-in Development, bound to loopback, with the Azure overrides above on the API. Keep existing CraterIt flags/header checks. Azure settings are not needed on the web client.
4. For a no-generation-charge fixture round trip, use separately reviewed storage-only tooling that directly invokes this adapter. The offline check runner intentionally cannot contact Azure, and the existing API has no arbitrary fixture-upload endpoint.
5. Once a paid request is separately authorized, generate one image through Product Detail. Confirm JPEG then manifest under its GUID, distinct from product-images; refresh and restart both hosts to retrieve the same artwork without regeneration.
6. Replay the same API request ID/payload; verify the existing artwork ID returns. Verify anonymous Blob access fails and remote/missing-header/disabled/Production API access remains denied. A private container on a network-restricted account may reject anonymous access at the network layer as well.
7. Return Provider to Local and restart; verify the original local history remains. Leave Azure pilot blobs untouched for review; no deletion/import/rollback of resources is automatic.

Changed files for 005B: new API Services/AzureCraterArtworkStore.cs and CraterArtworkStoreFactory.cs; API Program.cs and FlyerMonkey.Api.csproj; new tests/CraterArtwork.AzureChecks/Program.cs and CraterArtwork.AzureChecks.csproj; this document. Existing /DATA/ ignore rules remain unchanged.

## Mission 004A — storage and contract

`POST api/images/generate` now accepts RequestId (nonempty GUID), ProductID (positive), ProductName (1–1000 characters), Preset (one of the existing five) and Prompt (unchanged 2000-character limit). Product/name are local-client snapshots; no SQL association is verified or modified. The response is saved-artwork metadata rather than JPEG bytes.

`ICraterArtworkStore` abstracts save, request lookup, per-product listing and image reading. Only LocalCraterArtworkStore is registered. A future Azure implementation can replace it; no storage URLs are exposed to clients.

The store resolves repository-root DATA/CraterArt from the API content root, requiring the expected source-project layout and failing closed otherwise. There is no working-directory-dependent fallback. Each final GUID directory contains artwork.jpg and metadata.json. Metadata includes artwork/request IDs, ProductID/name, preset identifier/name, server UTC timestamp, content type, filename, schema version and SHA-256 request fingerprint. User-supplied names and paths are never used as filesystem paths.

Files are written into a unique .staging-GUID directory, closed, then published together by a same-filesystem directory rename. Failure returns 503, never generation success. Cleanup of failed staging directories is attempted. Crash leftovers remain ignored and are excluded from retrieval. Atomic publication does not guarantee survival of power loss or disk failure.

The process-wide generation semaphore covers duplicate lookup, provider execution and saving. Identical completed requests return saved metadata without provider calls; conflicting reuse returns 409; concurrent generation returns 429. A missing image for a known saved request returns 503 rather than regenerating. There are no automatic POST retries. The client retains an uncertain request ID while the same product/preset/prompt is active. Navigation/refresh loses that pending ID: check restored history before repeating an uncertain generation.

Provider cancellation remains propagated. Once generated bytes arrive, saving uses an independent 30-second cancellation budget so browser cancellation does not discard artwork. Navigation/disposal cancel retrieval and suppress stale results. Cancellation may still incur a provider charge or finish saving.

## Retrieval and security

- GET api/images/artwork?productId=ID returns valid history newest first.
- GET api/images/artwork/ARTWORK_ID/image returns the JPEG. IDs use the GUID N format; malformed/missing IDs return 404.
- All three actions require Development, CraterIt:Enabled, loopback caller, loopback Host and X-CraterIt-Local: 1 before resolving storage/provider services. Artwork responses use no-store.
- No static route, CORS access, public URL or mobile enablement was added. The shared client fetches JPEG bytes with the header and renders a data URL.
- Both development hosts must remain loopback-bound without public proxies/tunnels. The server-side web client appears local to the API; these checks are not public-user authentication.

Product Detail restores newest valid artwork and offers a per-product history selector. Hide artwork does not delete files. Missing/incomplete records are omitted; corrupt/unreadable individual metadata is logged and skipped. Canonical image markup, Product.ImageBlobPath, Products gallery, preset definitions/prompts and provider settings remain unchanged.

Existing .gitignore /DATA/ covers final images, metadata and staging files. git check-ignore confirmed all three; git ls-files DATA returned no tracked files. No ignore-rule change was needed.

## Changed files

- FlyerMonkey.Api/Controllers/ImagesController.cs
- FlyerMonkey.Api/Models/GenerateImageRequest.cs
- FlyerMonkey.Api/Program.cs
- New FlyerMonkey.Api/Services/CraterItLocalAccess.cs
- New FlyerMonkey.Api/Services/ICraterArtworkStore.cs
- New FlyerMonkey.Api/Services/LocalCraterArtworkStore.cs
- New FlyerMonkey/FlyerMonkey.Shared/Model/CraterArtwork.cs
- FlyerMonkey/FlyerMonkey.Shared/Services/ImageGenerationApiService.cs
- FlyerMonkey/FlyerMonkey.Shared/Pages/ProductDetail.razor
- tests/CraterIt.Checks/Program.cs and CraterIt.Checks.csproj
- docs/CraterItImplementation.md and this document.

## Automated validation

48 package-free checks pass using a fake provider and temporary filesystem directories. Coverage includes unchanged presets, generation/list/image security checks, request validation, save-before-success, replay/conflict behavior, fresh-store retrieval, product isolation, concurrency, early/late cancellation, simulated/real failed writes, failed-commit cleanup, interrupted/corrupt records, missing files, missing duplicate images, invalid root layout, client metadata/history/JPEG handling and disabled MAUI/public clients.

Command: dotnet run --project tests/CraterIt.Checks/CraterIt.Checks.csproj --no-restore --no-launch-profile -p:UseAppHost=false. The apphost override avoids a Windows crash-dialog file lock encountered while correcting a failure-path harness issue; no project setting changed.

API, web and Windows MAUI builds passed with --no-restore and existing dependencies. API/shared compilation reported two pre-existing unused-field warnings in Offers.razor; final incremental web and Windows builds reported zero warnings/errors. git diff --check passed. No credentials, live provider calls or browser sessions were used. Fresh-store retrieval is automated; full process restart and browser behavior remain manual.

## Manual complete-restart verification

1. Start API and web in their existing opted-in Development configuration, bound only to loopback. Keep API content root at the FlyerMonkey.Api project directory.
2. Generate artwork for a product. Verify the original image remains unchanged and a GUID directory under DATA/CraterArt contains the JPEG and metadata. Record its artwork ID.
3. Generate another preset. Select both history entries and confirm their own images/captions. Hide and select again.
4. Navigate to another product and back, then refresh. Confirm history returns without generation.
5. Stop BOTH API and web processes completely. Leave DATA/CraterArt intact. Restart both against the same repository and reopen the product. Verify the same artwork IDs, captions and JPEGs return without a generation call.
6. Check API-unavailable recovery, rapid clicks, cancellation and navigation during generation/retrieval. Reload history after cancellation before deciding to generate again.
7. Temporarily move one saved JPEG aside and reload: it is omitted, other entries remain and direct retrieval returns 404. Restore it. Use disposable data for an unwritable-storage check and confirm no success is shown.
8. Confirm Production/disabled/nonlocal/missing-header calls cannot generate/list/retrieve. Confirm git status never lists generated artwork or staging files.

## Limits and deferred work

Use one local API process per artwork root. Idempotency covers committed records, not exactly-once provider execution across crashes, failed writes, deleted/corrupt metadata or multiple API instances. A crash between provider completion and commit can lose the result; retry may incur another charge. No backup, retention policy or automatic history deletion is included. Lookups scan local records and suit a small development collection.

Azure storage, multi-instance coordination, mobile access, Custom Crater, public galleries, publication-format optimisation, Facebook sharing and verified offer overlays remain deferred.
