# Mission 012B: controlled customer Crater It

Mission 012 implementation and completed local test. Product 139 is the only supported product. Operator authentication, permissions, routes, provider configuration and artwork storage are unchanged.

## Mission 012E/F completion — November 1, 2026

The owner reports clicking Generate exactly once and receiving Completed with a visible private preview for the Chicken Crimpy artwork. Independent filesystem inspection confirms one Completed Product 139 journal and a decodable 1024 × 1024 JPEG (305,749 bytes). The customer ownership identifier remains private. No second generation request was initiated. Actual provider cost is unverified; the journal does not contain billing evidence.

Preserved JPEG SHA-256: `B2E19464C12ED322BEF533F7737B1A9F53723B39382D90E5944CEAC73B7B507F`.

Preserved journal SHA-256: `53AE9301E7C1781869CD03F23B2F6E5A483D25305EC7B2DD7EEF6D54C3EDC779`.

Authorized cleanup updated exactly one fingerprint-verified customer from CraterEnabled=1 to 0 using a parameterized, target-checked transaction. API/Web CustomerCrater:Enabled are false, the temporary API allowlist was removed, and both temporary Web/API processes were stopped, removing their command-line overrides. Existing operator-local launch profiles retain both generation flags false. No journals/artwork were deleted or reset, and no public publication or deployment occurred. Files remain in ignored customer-only DATA storage, separate from operator/public artwork.

The initial local configuration save failed because the PowerShell File.Replace invocation with a null backup argument raised ArgumentException, "The path is not of a legal form." Its failure cleanup completed before SQL enablement. The successful retry used supported, project-specific dotnet user-secrets set with JSON stdin, privately verifying unrelated values were preserved. Customer identifiers and credentials were not placed in source or command-line arguments. Gate disablement and allowlist removal also used the supported CLI.

Mission 012F quality gate reran all 484 offline checks successfully; Web/API builds passed with zero warnings/errors. Credential/artifact and ignore-protection scans returned zero findings, and the mission files contained no match for the private customer owner identifier. Git whitespace checks passed. JPEG and journal hashes remained unchanged after cleanup. Artwork and journals are excluded from the mission source checkpoint. The intended checkpoint branch is main with existing upstream origin/main; unrelated workspace edits remain excluded.

The implementation-only and not-yet-authorized statements in the historical validation/enablement procedure below describe earlier checkpoints; this completion record supersedes them. Paid authorization was limited to the single completed test. A different-customer live preview denial was not exercised in this test; ownership denial remains covered by offline checks. No further paid attempt or Facebook implementation is authorized by this record.

## Routes and authorization

API routes:

- `GET /api/customers/crater/139/attempt`: authoritative readiness/attempt status.
- `POST /api/customers/crater/139/attempt`: reserve and execute the single attempt, or return existing state. No client payload is used; names, prompt and ownership are server-derived.
- `GET /api/customers/crater/139/image`: private completed artwork only.

Each route explicitly authenticates CustomerBearer and authorizes Customer.Access, independently of the operator fallback. Development, HTTPS, loopback client/Host, CustomerAuthentication:Enabled, CraterIt:Enabled and CustomerCrater:Enabled are required. The customer is read from SQL using validated tenant/object claims. Missing/inactive customers, missing/invalid/nonmatching CustomerCrater:AllowedCustomerId and SQL failures fail closed. POST additionally requires the actual SQL CraterEnabled flag. No provisioning occurs here. Status/preview remain readable for the allowlisted Active owner after SQL eligibility is revoked, while the local feature gates remain enabled. Production rejects all routes before database/storage/provider resolution, regardless of SQL flags.

The existing `/customer/me` response continues to mask CraterEnabled=false under Mission 011A; it is not the readiness endpoint. The new attempt endpoint evaluates actual SQL eligibility. Existing operator policy and production hard-block code is unchanged.

Web routes mirror the flow at `/customer/crater/139`, `/customer/crater/139/attempt` (POST) and `/customer/crater/139/image`. They require the customer cookie policy, acquire the customer delegated token server-side, and forward only to the existing validated HTTPS API root, with redirects disabled. Web POST requires antiforgery. Web gates CustomerCrater:Enabled and CraterIt:Enabled default off and require Development. The ProductCraterEntry component links to this page for signed-in customers on Product 139. Ready/Generating/Completed/Failed/Unknown are displayed; an unapproved customer sees a disabled/not-eligible state. The Generate button appears only after an authoritative CanGenerate=true result, and server authorization is repeated on POST. Status/preview refreshes are GET only.

## Private storage and one-attempt safety

Customer files live under ignored `DATA/CustomerCraterArt/<CustomerId>/139/`, outside all operator and legacy readers (`DATA/CraterArt`). They are never static assets or public publication input. CustomerId is resolved in SQL, never supplied by a browser. A journal contains CustomerId, ProductId, State and UpdatedUtc; a committed JPEG is the private artwork. Public status returns only state, CanGenerate and HasImage, never owner identifiers.

An exclusive filesystem lease covers reservation, provider dispatch and commit, including across API processes sharing this same root. `attempt.json` is created with CreateNew and flushed before contacting the provider. It is never deleted/reset by the app. A second POST cannot buy another image, including after failure, refresh or process restart. Provider SDK retries are explicitly zero for the customer adapter; operator behavior is untouched. There is no reference-image submission.

States:

- Pending: durable reservation created.
- Generating: dispatch about to start/in progress.
- Completed: JPEG persisted first, then completed journal committed.
- Failed: failure before provider dispatch; reservation still consumed.
- Unknown: any failure after dispatch, including timeouts, cancellation or persistence failures. A Pending/Generating journal without a live lease is recovered as Unknown.

A corrupt/unreadable journal fails closed. A missing preview never enables generation. If the final journal update fails, the prior reservation remains and blocks dispatch. Browser cancellation does not cancel the reserved provider operation; it has a separate two-minute timeout. The Web request timeout is three minutes. A response can be lost after successful generation; refresh status rather than resubmitting to obtain another image.

This is a local filesystem safety boundary, not a distributed payment ledger. Preserve the storage root and journal across restarts. Deleting/rolling back/moving it, using another checkout/root, or host/storage failure can invalidate the one-attempt guarantee. Do not do so for the test. Failed/Unknown attempts need manual investigation and separate approval; this mission provides no reset/retry-generation endpoint. Temporary files may remain after filesystem errors and must not be interpreted as completed artwork. Private files contain internal customer identifiers and must be protected by host permissions and excluded from source, diagnostics and backups shared outside the approved environment.

## Offline validation

`tests/CustomerCrater.Checks` uses fake SQL/product repositories, image provider and storage for API/Web tests. It also uses an isolated temporary filesystem fixture for lease, atomic persistence and reconstructed-store recovery tests. No real SQL, Google, Entra, image generation or Blob calls are made. A real SDK test uses dummy credentials and a fake HTTP transport to verify zero retries on a retryable response.

Run:

```powershell
dotnet run --project tests/CustomerCrater.Checks --no-launch-profile --artifacts-path .artifacts/mission011a -p:NuGetAudit=false
```

Regression suites: CustomerAuthentication.Checks, WebAuthentication.Checks, Authentication.Checks, CraterIt.Checks, ImageGeneration.Checks, CraterArtwork.AzureChecks and ProductSecurity.Checks. Build Web/API with isolated artifacts to avoid locking the running F5 binaries. Offline results do not establish successful paid generation, actual cost or production readiness.

Final implementation validation: all 484 checks passed (CustomerCrater 62, CustomerAuthentication 58, WebAuthentication 76, Authentication 53, CraterIt 119, ImageGeneration 15, CraterArtwork.Azure 54, ProductSecurity 47). Final incremental Web/API builds passed with zero warnings/errors. Earlier recompilation reported the two existing unused Offers fields and existing test-only service-provider analyzer warnings. Missing isolated restore assets for the new test and two regression projects were restored from available packages before successful reruns. Repository credential/artifact scanner returned zero findings; git diff --check passed with existing line-ending normalization notices.

## Changed files

```text
FlyerMonkey.Api/Program.cs
FlyerMonkey.Api/Controllers/CustomerCraterController.cs
FlyerMonkey.Api/Services/CustomerCraterGeneration.cs
FlyerMonkey.Api/Services/CustomerCraterStore.cs
FlyerMonkey/FlyerMonkey.Shared/Components/ProductCraterEntry.razor
FlyerMonkey/FlyerMonkey.Shared/Model/CustomerCraterAttempt.cs
FlyerMonkey/FlyerMonkey.Web/Program.cs
FlyerMonkey/FlyerMonkey.Web/Services/CustomerAuthentication.cs
FlyerMonkey/FlyerMonkey.Web/Services/CustomerCrater.cs
tests/CustomerCrater.Checks/CustomerCrater.Checks.csproj
tests/CustomerCrater.Checks/Program.cs
tests/CustomerCrater.Checks/WebChecks.cs
docs/CustomerCraterGeneration.md
```

Existing unrelated changes in docs/ApprovedArtworkPublication.md, .vscode/settings.json and FlyerMonkey.Api/dotnet-tools.json were preserved. Implementation did not change launch profiles, real User Secrets, customer SQL data, operator routes or operator storage. Subsequent approved local enablement/cleanup is recorded above.

## Historical manual enablement procedure — requires fresh approval for future use

1. Obtain explicit approval for local feature enablement and the exact single customer SQL update, including cleanup. Separately obtain approval for ONE paid request before pressing Generate. Keep operator-local CraterIt:LocalDevelopmentGenerationEnabled=false on both processes throughout; that flag controls operator paid generation.
2. Stop F5 manually. Confirm the verified API SQL target, original Active test customer and Product 139. Privately compare customer identities; do not put a real CustomerId in chat, tracked launch settings or documentation. Confirm there is no previous customer reservation. If one exists, stop; do not delete it.
3. Only after approval: in API User Secrets set CustomerCrater:Enabled=true and CustomerCrater:AllowedCustomerId to the privately verified internal CustomerId. In Web User Secrets set CustomerCrater:Enabled=true. The existing operator-local profiles set CraterIt__Enabled=false, overriding User Secrets. For this approved test only, launch each existing profile with a command-line configuration override, which has higher precedence; do not change tracked profiles:

   ```powershell
   dotnet run --project FlyerMonkey.Api --launch-profile operator-local -- --CraterIt:Enabled=true
   dotnet run --project FlyerMonkey/FlyerMonkey.Web --launch-profile operator-local -- --CraterIt:Enabled=true
   ```

   Run these in separate terminals only after approval, instead of simultaneously starting F5. Confirm Development/HTTPS loopback and keep CraterIt:LocalDevelopmentGenerationEnabled=false in both profiles. These commands do not contain credentials or customer identifiers.
4. Only after SQL approval: with the administrative identity, use a parameterized transaction against the verified development database. Update CraterEnabled from 0 to 1 and UpdatedUtc to SYSUTCDATETIME only WHERE CustomerId=@ApprovedCustomerId AND Status=N'Active' AND CraterEnabled=0. Require exactly one affected row; otherwise roll back and stop. Do not change tenant/object identity or other customers. Runtime still needs only its existing SELECT/INSERT permissions.
5. After the approved SQL change, start the profiles using the commands above; verify the effective gates privately. Use the approved customer session and Product 139. Open Crater It then the customer eligibility link. The server must report Ready. Missing credentials/model configuration will fail closed and consume a reservation if Generate is pressed, so privately check existing API User Secrets configuration first. Do not print credentials or invoke a provider call to check them.
6. STOP for explicit paid-call approval. Once granted, press Generate one image ONCE. Do not use operator generation. Wait for the completed preview. Refresh status/preview only if necessary. A Failed/Unknown outcome ends the test; no second generation is permitted. Record only safe status/timing and cost if independently available; otherwise actual cost is unverified.
7. Verify the owner can preview and another customer cannot. Do not publish. Restore CraterEnabled=0 for exactly the approved customer using the approved cleanup transaction; update UpdatedUtc. Restore both local feature overrides to false/remove them and CustomerCrater:Enabled=false in Web/API User Secrets, then restart manually. Confirm readiness is disabled. Retain the journal and artwork so no later attempt can silently repeat.

This procedure grants no authorization for another live enablement, SQL update, reservation reset or paid test.
