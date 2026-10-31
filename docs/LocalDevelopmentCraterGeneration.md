# Mission 010D: local development generation

The implementation defaults to disabled. The first user-run manual attempt failed with HTTP 400, `image_generation_user_error: invalid_value`, parameter `model`: the previously hardcoded `gpt-image-1` was unavailable. After the model configuration fix below, the operator reported a successful live generation and private preview. The agent made no paid generation calls.

## Final checkpoint artwork verification (October 31, 2026)

The saved Arnott's artwork is `b9f9cd79-f479-4e70-90d8-e73f9240fb33`, request `ffa63950-6488-4b45-af79-a2aa485288da`. Its manifest records **ProductID 139**, rather than the product 102 stated in the checkpoint request. Its product name is `Arnott's Arnott's Shapes, Originals, Barbecue, Chicken Crimp, 160g-190g`. Existing product 102 manifests describe Dynamo Professional Laundry Liquid. This discrepancy remains outstanding; stored metadata was not altered.

The JPEG decodes as 1024 × 1024, 245,487 bytes, SHA-256 `38945BE8A2AADD6028F97C4186B8E3C56591CE1F6B300621E50BC707DBC5E4EC`. The manifest creation time is `2026-10-30T18:46:35.5953196Z` (October 31 at 02:46:35 in Perth). The artwork ID matches its directory. Schema version 1 records the request ID, product, Comet Crush preset, creation time, JPEG filename/content type, request fingerprint and nonempty owner tenant/object IDs. Owner tenant matches the configured local tenant; owner object ID is present, but was not compared with User Secrets. The private endpoint requires both tenant and object ownership.

The manifest does **not** record provider/model, full prompt, SDK version or provider response ID. The default model in source is `gpt-image-1.5`; the exact model used for this saved image cannot be independently established from its manifest. No provenance fields were invented or retroactively added.

`/DATA/` is ignored and contains no tracked files. Both operator-local generation flags have been reset to false for the checkpoint. Restart any running F5 processes for those settings to take effect. No production configuration was changed.

## Enable for a manual localhost test

1. Stop the existing F5 Web/API session before rebuilding. Use the existing `operator-local` Web and API launch profiles, which bind HTTPS localhost. Keep the existing operator identity, tenant, client allowlist, `Authentication:ViewOwnOnly=true`, and `OperatorPermissions=[Crater.ViewOwn]` unchanged.
2. In **both** `operator-local` launch profiles set `CraterIt__Enabled` to `true` and add `CraterIt__LocalDevelopmentGenerationEnabled=true`. These are explicit local launch settings, not production settings. Both processes must run in `Development`. Leave other launch profiles unchanged. The new flag defaults to false when absent.
3. Keep Web `Authentication:ApiBaseUrl=https://localhost:7255/`, API `CraterIt:PrivateAccessEnabled=true`, and API `CraterArt:Provider=Local`. The Web flow uses the authenticated API base URL, not the legacy anonymous Crater client address.
4. Ensure the existing API local User Secrets contain `OpenAI:ApiKey`. The API image model is selected by `OpenAI:ImageModel`, defaulting to `gpt-image-1.5` when absent. Do not place keys in tracked files or production configuration. Existing operator identity/Web credential and SQL configuration are still required. The model fix does not read or modify User Secrets.
5. Start F5, sign in explicitly at `/operator/signin`, and open a product detail page. Click **Crater It** to open its panel. It should show **LOCAL DEVELOPMENT**, **Ready**, and an optional prompt. Opening the page/panel does not generate anything.
6. **Stop here before any paid call.** Clicking **Generate image** is the explicit action that calls the paid provider. Perform that manual test only when ready to incur the charge. Expected states: Ready → Generating → Success with a JPEG preview, or Failure with guidance.
7. Disable the new flag in both profiles after testing and restart the processes.

## Image model configuration fix

The API previously hardcoded `gpt-image-1` in `Services/ImageGenerationService.cs`. It now reads `OpenAI:ImageModel` through `IConfiguration`, so normal JSON, User Secrets, environment and command-line configuration precedence applies. An absent setting uses `gpt-image-1.5`; surrounding whitespace is trimmed, and an explicitly blank setting fails before any provider call. There is no automatic fallback/retry with a different model after a provider rejection.

No launch-profile change is required to use the new default. For an explicit local override, add this **nonsecret** entry under the **API** `operator-local` profile's `environmentVariables`:

```json
"OpenAI__ImageModel": "gpt-image-1.5"
```

No Web model setting is needed. Keep `OpenAI:ApiKey` in API User Secrets; do not copy it into a launch profile. Restart the local API after rebuilding/configuration changes. Existing generation opt-in flags and permissions remain unchanged.

The installed `OpenAI` .NET package remains **2.14.0**. Its `ImageClient` accepts the configured model as a string. An offline test exercises the real SDK through an in-memory HTTP handler and verifies `/v1/images/generations`, the selected model, `size=1024x1024`, `output_format=jpeg`, `output_compression=80`, and base64 image decoding. No package upgrade is needed for this request shape.

The [official Images API reference](https://developers.openai.com/api/reference/resources/images/methods/generate) lists `gpt-image-1.5` as supported, records `gpt-image-1` shutdown on October 23, 2026, and documents these output options. The [model page](https://developers.openai.com/api/docs/models/gpt-image-1.5) describes the replacement model. SDK/request compatibility is confirmed; access for the operator's OpenAI project is **not** verified by offline tests. No authenticated model-list or generation call was made.

`tests/ImageGeneration.Checks` adds 15 offline checks for default/override selection, whitespace validation, missing credentials, SDK serialization and fake response handling. Run with `dotnet run --project tests/ImageGeneration.Checks --artifacts-path .artifacts/mission010d -p:NuGetAudit=false`. All credentials used by this test are dummy strings and the transport cannot reach the network.

## Security and storage

`POST /api/images/local-development/generate` has a separate policy, `Crater.LocalDevelopmentGenerate`. It requires authentication enabled, Development, both explicit Crater flags, a loopback remote IP and Host, the local request header, and the existing validated v2 tenant/operator/client/scope checks with ViewOwn permission. It does not grant `Crater.Generate`. Production rejects the policy even if the local flags are mistakenly set. The ordinary generation route and `productionGenerationHardBlocked=true` remain unchanged.

The Web server acquires the existing operator's delegated token and forwards it only to the configured HTTPS loopback API, with redirects disabled. Private preview requests use the same token and existing ownership checks. Anonymous and nonlocal/disabled operator messaging remain in place. No customer authentication or public generation was added.

The existing generation service, shared write semaphore, request fingerprint/replay protection, JPEG storage and provenance metadata are reused. With the local provider, artwork goes to ignored `DATA/CraterArt/<artwork-id>/artwork.jpg` and `metadata.json`, including selected product ID, request ID, preset, timestamp and owner claims. Nothing is approved or published. No Azure deployment or storage configuration change was made.

Each panel instance permits one explicit attempt and disables the prompt/button immediately. Success and failure both remain disabled to prevent a second charge after an uncertain result. A deliberate page reload opens a new attempt; check private artwork before doing so after a failure. The existing semaphore prevents overlapping API generation and completed request replay avoids another provider call. This is local single-process protection, not a distributed billing/idempotency system.

## Validation

Final checkpoint rerun: Web and API builds each passed with zero warnings/errors using cached restores and isolated artifacts. All four offline suites passed: CraterIt 119, Authentication 53, WebAuthentication 76, ImageGeneration 15 (263 total). Working-tree and index `git diff --check` passed. The index remains empty pending review of the product-ID discrepancy; no commit or push was made. Unrelated `docs/ApprovedArtworkPublication.md`, `.vscode/settings.json` and `FlyerMonkey.Api/dotnet-tools.json` are excluded from the proposed checkpoint.

- Web and API builds passed using `--artifacts-path .artifacts/mission010d` to avoid DLL locks from the running F5 session.
- `tests/CraterIt.Checks`: 119 checks, fake image provider and temporary local storage.
- `tests/Authentication.Checks`: 53 checks, locally signed tokens and fake repositories/storage; production/disabled/anonymous/foreign operator/client denials tested, with zero paid provider resolutions.
- `tests/WebAuthentication.Checks`: 76 checks, offline authentication and rendered UI plus fake asynchronous generation; selected product, Ready/Generating/Success/Failure, no generation on render, and duplicate-click prevention covered.
- Earlier validation reported unrelated unused Offers fields and test service-provider warnings. Initial restore could not fetch NuGet vulnerability data under restricted network access; builds completed with cached packages. The agent performed no live Entra or paid OpenAI smoke test; the operator's live success is described above.

## Changed files

- API: `Services/CraterAuthorization.cs`, `Services/EntraAuthentication.cs`, `Controllers/ImagesController.cs` — separate local authorization/route and safe provider failure response.
- Shared: `Services/LocalDevelopmentGeneration.cs`, `Components/ProductCraterEntry.razor`, `Pages/ProductDetail.razor` — generation state, optional prompt, private preview and selected product context.
- Web: `Services/LocalDevelopmentGeneration.cs`, `Program.cs` — authenticated loopback API integration and service registration.
- Tests: `CraterIt.Checks/Program.cs`, `Authentication.Checks/Program.cs`, `WebAuthentication.Checks/Program.cs`.
- This document. Pre-existing unrelated workspace changes were left untouched. No commit, push or deployment was performed.
- API/Web `Properties/launchSettings.json`: only the operator-local profile gains the explicit disabled local-development flag; both generation flags are false after testing.
- Image model fix: API `Services/ImageGenerationService.cs` and `tests/ImageGeneration.Checks/{ImageGeneration.Checks.csproj,Program.cs}`.
