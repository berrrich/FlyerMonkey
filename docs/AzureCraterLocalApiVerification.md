# Mission 006B — Azure-backed artwork through the local API

## Configuration and scope

Git checkpoint at start: main, d3f87d92717d9242106cd0411dcab2351425413a. The two existing AzureSmoke files were already staged; their content and index state were preserved. No commits, pushes, merges or branch changes were performed.

The existing Azure adapter/factory, ImagesController endpoints, local access helper and shared gallery clients were inspected. Initial work added a process-local API launcher, an explicitly opted-in GET-only probe and its restart runner. Following approval to prepare a fixture endpoint for ProductID 102, the API now also contains the default-off fixture controller described below and a shared write slot; the generation provider, gallery clients, storage implementations and access helper remain unchanged. The API factory still defaults to Local and rejects Azure outside Development. The launcher configures only its process/child, restores its original environment on exit, and does not edit user secrets, tracked settings, user/machine environment or deployed configuration.

To run the real local API manually, after ensuring localhost:5145 is available:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Start-CraterAzureDevelopmentApi.ps1
```

The launcher uses the existing settings:

```text
DOTNET_ENVIRONMENT=Development
ASPNETCORE_ENVIRONMENT=Development
CraterArt__Provider=Azure
CraterArt__Azure__ServiceUri=https://flyermonkey.blob.core.windows.net/
CraterArt__Azure__Container=crater-artwork
CraterIt__Enabled=true
```

The API listens only on http://localhost:5145. Its existing loopback remote-address/Host/header checks remain mandatory. Existing server startup prerequisites, including the SQL environment variable and developer sign-in, must already be available. No account key, SAS or new connection string is introduced. Azure storage uses the existing DefaultAzureCredential path; the earlier storage smoke selected VisualStudioCredential. This API probe verifies successful access but does not independently instrument the API's selected credential source.

Important: CraterIt:Enabled is currently shared by the generation and retrieval guards. Enabling gallery access leaves the existing trusted-local generation endpoint available. Do not invoke POST api/images/generate for this mission: it would call the paid provider. Android generation remains disabled by its existing registration. No production/staging, TLS, canonical-image or Azure configuration files were changed.

## Live read-only API verification

An API was already listening on port 5145. It was left untouched. The isolated test instead used localhost:5146, started the actual built FlyerMonkey.Api twice with the Development/Azure settings, and stopped only the two processes it created.

The probe read the previously approved Azure smoke fixture:

- Artwork ID: e21ce070019e4429b32d9872e38b5475
- Request ID: b32a97e1-4da6-4d86-a4d5-1de0bc66b498
- ProductID: 2147483647 (reserved smoke association, not a verified catalogue product)
- Expected JPEG size: 631 bytes
- Expected SHA-256: B879F70CAC2DCA989DA9691B445CDD5E0370BF1274E7EC00E66A827614D65928

Both API instances passed metadata verification and image hash/size checks through the existing Web Development DI registration and CraterArtworkApiService. Missing-header and nonloopback-Host requests returned 404. Each probe exited 0, and the restart runner exited 0. This establishes that the previously saved Azure fixture is retrievable through the real API across a complete API process restart. It does not establish a new API save or paid-generation behavior.

Endpoints used:

- GET api/images/artwork?productId=2147483647
- GET api/images/artwork/e21ce070019e4429b32d9872e38b5475/image

The probe's HTTP handler rejects anything except GET requests to the selected localhost artwork API. Its default invocation performs no network operation. The restart runner also requires explicit opt-in; neither live probe is part of the offline quality gate. Server/credential logs are captured in memory and withheld, not saved or printed.

To repeat this read-only check deliberately, build the API and probe first, ensure the selected loopback port is free, then run:

```powershell
dotnet build FlyerMonkey.Api/FlyerMonkey.Api.csproj --no-restore -v minimal
dotnet build tests/CraterArtwork.ApiReadOnly/CraterArtwork.ApiReadOnly.csproj --no-restore -v minimal
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-CraterAzureApiReadOnly.ps1 -LiveReadOnly -Port 5146
```

Normal approved restore is required first on a fresh checkout; the new project's packages were restored from the existing local package cache during implementation.

## Web and Android status

The actual Web application UI/browser and Android application/device were NOT tested. The Web registration's existing C# gallery client was tested against the isolated API; that is client-service compatibility, not UI verification. Android's fixed Debug gallery port remains 5145, so the live probe explicitly skipped its registration when using 5146. Existing offline tests continue to cover Android GET-only access, disabled generation, Web configuration and Debug/Release network policy.

For later manual testing, use the standard loopback port 5145 only after coordinating the currently running process. Android Debug requires adb reverse tcp:5145 tcp:5145. Use a real, approved catalogue ProductID for any new clearly labelled test artwork so Product Detail can load the matching product. The existing fixture's reserved ProductID is suitable for API testing but does not establish a viewable product page. Do not change canonical images or broaden network/production restrictions to make this work.

## Fixture endpoint and approved live verification

The user approved preparation of a fixture endpoint and selected ProductID 102, then separately approved one create, one identical replay and read-only integrity/restart checks subject to preflight. The new endpoint is POST api/images/artwork/development-fixture and defaults off. The approved live test passed as recorded below. The existing generation endpoint still calls the paid provider and was not invoked. Prior smoke approval was not reused.

The endpoint requires all existing Development/CraterIt-enabled/loopback/Host/local-header guards, plus CraterIt:FixtureImportEnabled=true, CraterArt:Provider=Azure and a positive CraterIt:FixtureProductId matching the request. Normal startup leaves it disabled, including when the Azure launcher is used without its extra fixture switch. It accepts only request/artwork GUIDs and ProductID in a body limited to 1024 bytes; it has no image-upload, path, prompt or client-supplied name/preset fields. The server embeds the already verified JPEG, checks its SHA-256, reads the existing product via ProductService.GetAsync, and writes clearly labelled [DEV FIXTURE] metadata. It never resolves a generation service or modifies SQL.

Replay with the same request/artwork/product identity returns the saved record without another write or SQL lookup. Conflicts return 409. A missing/changed saved JPEG returns 503 without repair. Store failures return 503 without success. Fixture import and generation share one semaphore to prevent overlapping request checks/writes within the existing single-process model. Storage remains create-only; no delete/overwrite path was added.

Preparation files: new DevelopmentCraterFixturesController.cs, ImportDevelopmentFixtureRequest.cs, DevelopmentCraterFixture.cs and CraterArtworkWriteSlot.cs; ImagesController's semaphore now references the shared slot. The linked local checks add a fake read-only product lookup and in-memory fixture store, with no Azure/provider/SQL calls. The launcher exposes an explicit fixture switch for later approved use and restores all process-local settings on exit. No user secrets or production settings were changed.

The following exact operation was separately approved and executed once for creation, then once as an identical replay:

```http
POST http://localhost:5146/api/images/artwork/development-fixture
X-CraterIt-Local: 1
Content-Type: application/json

{
  "requestId": "0e135ff0-58b8-4f4d-9365-0ff981599a5b",
  "artworkId": "df6a9c89-85b1-40e4-bbc9-d80103913b6c",
  "productID": 102
}
```

The API read ProductID 102 and created these two previously absent paths in flyermonkey/crater-artwork:

```text
artwork/df6a9c8985b140e4bbc9d80103913b6c/artwork.jpg
artwork/df6a9c8985b140e4bbc9d80103913b6c/metadata.json
```

The JPEG is 631 bytes with SHA-256 B879F70CAC2DCA989DA9691B445CDD5E0370BF1274E7EC00E66A827614D65928. ProductID 102 was verified through GET api/products/102 as Dynamo Professional Laundry Liquid. Saved ProductName is [DEV FIXTURE] Dynamo Professional Laundry Liquid; PresetName is Development fixture (not generated), preset identifier Classic, timestamp server UTC. Both destination paths were verified absent twice before the first POST. The container was verified private using developer-only DefaultAzureCredential restricted to VisualStudioCredential.

For future separately approved fixture tests, the loopback API can be started with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Start-CraterAzureDevelopmentApi.ps1 -Port 5146 -EnableFixtureImportForProduct102
```

That switch enables the fixture-import flag and product scope only for the child API. It does not submit the POST. No further writes or replay runs are approved by this documentation. Both GUID paths now exist; the opt-in live test will fail its absence preflight if rerun. Leave both blobs in place. Do not enable production/staging, broaden permissions or invoke paid generation.

### Actual live result

Added tests/CraterArtwork.ApiFixtureSmoke (separate console project, not in the solution or quality gate). Its live mode requires --live-approved-product-102. It starts only owned loopback API processes, uses the exact approved IDs, rejects nondeveloper credential overrides, and does not retry POSTs. Direct Azure calls from the harness are GET/HEAD-only and limited to the container and these two paths; only the real local API performs the approved writes. All normal invocation modes are non-live by default. Restoring from the existing local package cache and building succeeded with exit 0 and zero compiler warnings/errors.

The live execution exited 0:

- ProductID 102 existed and was identified as Dynamo Professional Laundry Liquid.
- The private flyermonkey/crater-artwork destination and both absent blob paths were confirmed before writing; product-images was never addressed.
- The owned API listened only on localhost:5146, with Development confirmed in its lifecycle log, Azure provider/service/container configured explicitly, fixture import enabled and scoped to 102.
- Exactly one create POST and one identical replay POST were sent, both returning HTTP 200. The original saved record was returned on replay; both Azure blob ETags and LastModified values were unchanged.
- Both blobs were visible with the expected content types/length. The Web-registered C# gallery client retrieved the full saved metadata; API image bytes matched the embedded fixture and its SHA-256. Content-Type image/jpeg and Cache-Control no-store were verified.
- The first API process was stopped. A new process started with fixture import false and the product scope cleared. The same metadata and JPEG integrity checks passed through GET endpoints after this complete restart.
- GET against the POST-only import route returned 405. This only proves GET cannot write; disabled-flag behavior is covered by the offline suite and the restarted process's explicitly false configuration. No third POST was sent.
- Both owned processes were stopped afterward, so no fixture-enabled process from the test remains. Existing services, local/user/machine configuration and production settings were untouched. Both blobs remain for inspection; no cleanup/deletes occurred.

Actual Web UI/browser and Android device tests remain outstanding. The test invoked the existing Web C# gallery service, not a browser. Android's fixed ADB port 5145 was not used by the isolated API test; its generation-disabled policy remains unchanged.

## Automated verification and preservation

After fixture endpoint preparation, the complete pre-commit quality gate passed with exit 0: 100 local/client/fixture checks, 54 offline Azure checks, 49 repository/policy checks, and API, Web, Windows MAUI, Windows Reviewer and Android Debug builds. Final gate builds reported no compiler diagnostics. The separate read-only probe project built successfully; its first compilation reported only the two existing Offers.razor unused-field warnings. Fixture checks use an in-memory store and fake product lookup with no registered paid provider; the live fixture endpoint was not enabled or invoked.

Git whitespace checks passed. Storage/provider/client/prompt/SQL/canonical-image source files are unchanged; API changes are limited to the new gated fixture path and shared write slot. All six local artwork/metadata hashes match the baseline from the prior storage-only smoke test. The only new live writes are the two approved fixture blobs above. No product-images operations, SQL changes, local artwork changes, source credential additions or Git index/branch mutations occurred.

## Rollback and remaining work

Stop only the API process you started. Use a fresh shell with no Azure override, or select CraterArt__Provider=Local before starting the normal Development API. Existing DATA/CraterArt files are neither imported nor changed; Azure records remain in place. Switching providers changes visible history, not data ownership or storage contents.

Remaining: perform actual Web UI and Android device gallery/restart checks for ProductID 102. Use the normal loopback API port 5145 only after coordinating its existing owner, select Azure in the API process and keep fixture import disabled. Android Debug requires adb reverse tcp:5145 tcp:5145. No more writes are needed to view the saved fixture. Canonical product-images, SQL contents, local artwork and API security guards remain outside the write scope.
