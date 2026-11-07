# Mission 012P - Azure customer authentication readiness

Audit date: November 3, 2026 (Perth). Status: deployed API binary inspected and customer code gap verified; effective configuration and generation safety remain unverified. No Azure configuration or deployment changes made.

## Access and verified deployment information

- At the initial preflight, neither Azure CLI (`az`) nor Az.Accounts, Az.Websites or Az.Resources was discoverable. Standard Azure CLI installation paths were absent. The module/command checks were repeated in the owner context.
- No Azure context/profile metadata files were found in standard owner-profile locations. Token caches were not read. This does not establish that every possible credential source is absent, and the browser portal session does not establish a PowerShell session.
- No subscription-scoped Azure query was made. No Azure RBAC permission denial was observed; the immediate blocker is missing tooling and an unverified authenticated command-line context.
- Intended target, established by the earlier portal overview: **FlyerMonkeyApi**, resource group **flyerMonkeyResourceGroup**. The earlier overview showed Running, Windows, .NET 10. These are prior observations, not a new PowerShell verification. The subscription must be selected and verified against that resource before any audit query.
- Existing public API: `https://flyermonkeyapi-g7htasdacxfzgbcd.australiaeast-01.azurewebsites.net/`.
- Deployed artifact hash/version, deployment timestamp, bundled configuration and route inventory remain unknown.

## Configuration readiness

| Setting | Working local baseline | Azure effective value |
| --- | --- | --- |
| `CustomerAuthentication__Enabled` | true in operator-local profile | Unknown |
| `CustomerAuthentication__MobileClientId` | Approved Android registration configured in local User Secrets during prior setup; not reread here | Unknown |
| `CraterIt__Enabled` | false in test launch | Unknown |
| `CustomerCrater__Enabled` | explicitly false in test launch | Unknown |
| `CraterIt__LocalDevelopmentGenerationEnabled` | false in test launch | Unknown |

The approved Android public-client ID is `dbc3555c-669f-423e-acf3-f1cef013ce69`; it is nonsecret application metadata. Production generation gates are **not verified false** by this audit. Local test settings are not evidence of production state.

Missing App Service settings must not be equated with false until deployed appsettings files, environment-specific files, startup arguments and other providers have been checked. The local source defaults customer authentication off when its effective Enabled value is absent, and denies mobile clients when the effective MobileClientId is absent/invalid. Azure might run different code or configuration.

## Local bearer contract to compare with the deployed artifact

The current local source uses these nonsecret constants:

- Tenant: `bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab`.
- Audience: `c47581be-9d3d-4ed3-b0ce-7518fb3094c6` or its `api://` identifier.
- Issuer: `https://bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab.ciamlogin.com/bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab/v2.0`.
- Requested delegated scope: `api://c47581be-9d3d-4ed3-b0ce-7518fb3094c6/Customer.Access`; API requires `Customer.Access` in `scp`.
- CustomerBearer additionally validates signature, lifetime, v2 token version, tenant, object claim and authorized `azp`. Website and approved mobile client are allowed separately from operator authentication.

These issuer/audience/scope values are code constants in the local implementation, not additional configurable customer-auth App Service keys. Their presence and enforcement in the deployed artifact remain unverified. No client secret is needed for mobile bearer validation. Preserve HTTPS metadata and normal TLS validation.

## Customer profile findings and local differences

Prior anonymous host probes returned: health 200, Product 139 image 200, Product 139 data 500 and `/api/customers/me` 404. They were not repeated during this blocked audit.

The local CustomersController defines GET `/api/customers/me`. It returns 404 before authentication when customer auth is disabled; with auth enabled, an anonymous request returns 401. A valid customer request may also return 404 if the mapped account does not exist. Therefore the prior anonymous Azure 404 is consistent with disabled customer auth, but does not establish that the deployed endpoint exists. Deployment artifact/configuration inspection is required to distinguish this from an absent route or another hosting response.

Local GET maps validated tenant/object claims through the existing repository, does not provision a customer, and masks CraterEnabled=false. Preserve that mapping and read-only behavior. Do not invoke the provisioning POST to test readiness. The deployed database identity must already have the required read access and the existing schema/account mapping; inspect without changing SQL or exposing connection strings.

Mobile customer profiles still target `https://localhost:7255/`, whereas catalogue/images use Azure. Pixel USB authentication and profile reads passed with local HTTPS forwarding; untethered Wi-Fi/cellular profile reads failed. This local endpoint is a confirmed blocker independent of the unresolved Azure gaps.

## Smallest proposed next action and approval boundaries

1. Obtain owner approval to install the minimum Azure tooling (Az.Accounts/Az.Websites, or Azure CLI). This audit did not install software. Have the owner complete normal interactive Microsoft sign-in if needed; never request or print a token. Verify the correct subscription and resource group before querying App Service.
2. Resume a GET-only audit. Filter app-setting results in memory to the five allowlisted nonsecret keys; never dump all values. App-setting APIs may return a whole sensitive collection, so retrieve it only when necessary, do not persist it, and output only allowlisted fields. Inspect existing deployment metadata/artifact using authorized read access, without publishing credentials or a full environment dump. If access is denied, report the exact denied operation; app-setting inspection commonly requires the App Service configuration-list action as well as resource-read access.
3. Only after the gaps are verified, propose either configuration-only activation (if compatible customer code is already deployed) or a reviewed API deployment plus the minimum customer settings. Verify all three generation gates remain false and preserve operator/customer isolation. Preserve existing SQL, identity-provider setup and account mapping. Resolve or explain the prior product-data 500 before claiming general API health.
4. After separate owner approval and Azure readiness validation, build mobile with the existing Azure HTTPS customer API base URL. No website endpoint change is inherently required. Verify anonymous profile rejection, valid mobile profile access, wrong-client/scope/operator rejection, and untethered Wi-Fi/cellular behavior without generation or provisioning requests.

Explicit approval is required before tooling installation, any Azure setting change or deployment, mobile endpoint modification/build deployment, or changes to SQL/Entra. This report grants none of those approvals. No restart, deployment, SQL write, Entra change, mobile change, generation or Git operation occurred.

## Approved prerequisites follow-up

The owner approved installation of the minimum official PowerShell Gallery modules for CurrentUser. Install-Module used the registered PSGallery source https://www.powershellgallery.com/api/v2 for Az.Accounts and Az.Websites. Imports verified Az.Accounts 5.5.4 and Az.Websites 4.3.0. No unrelated modules or utilities were requested for installation; normal package dependencies were permitted.

Get-AzContext returned no active context. Stopped before invoking interactive authentication, per owner instruction. The owner must complete normal Microsoft interactive sign-in in PowerShell using Connect-AzAccount, selecting the subscription containing FlyerMonkeyApi. Credentials, codes and tokens must not be shared in chat. After sign-in, resume by checking the context and confirming the target subscription/resource group before any App Service query. Tooling installation approval is now satisfied; the earlier proposed tooling-approval step is historical. Cloud settings, effective production gates and deployed artifact remain unverified. No Azure resource queries, deployment, restart, SQL/Entra/mobile changes, generation or Git operations occurred in this follow-up.
## Authenticated PowerShell audit findings

The owner completed authentication. The saved context was available to this process and matched subscription 3931ffd1-70b6-441b-a51f-d21e219c2c9d and tenant 4112a2a6-70d8-47f0-a0fe-352edd57cc04. No new authentication flow was initiated. Get-AzWebApp verified FlyerMonkeyApi in flyerMonkeyResourceGroup, Running, runtime v10.0, HTTPS-only true, with the expected public hostname. LastModifiedTimeUtc was returned as October 31, 2026 17:28:28 UTC; this is resource modification metadata, not proof of a code deployment date/version.

Az.Websites returned six NameValuePair AppSettings entries with the site object. Only the five approved nonsecret keys and their colon-form equivalents were selected for output; no other setting values were printed, persisted or copied. All ten names were absent from that returned collection. No separate bulk secret-list endpoint or publishing-profile retrieval was invoked. This establishes absence from the returned site settings, not effective configuration after deployed files/startup/other providers. Do not infer false values solely from absence.

| Requested setting | Returned App Service setting | Effective deployed value |
| --- | --- | --- |
| CustomerAuthentication__Enabled | Absent (colon form also absent) | Unknown |
| CustomerAuthentication__MobileClientId | Absent (colon form also absent) | Unknown |
| CraterIt__Enabled | Absent (colon form also absent) | Unknown |
| CustomerCrater__Enabled | Absent (colon form also absent) | Unknown |
| CraterIt__LocalDevelopmentGenerationEnabled | Absent (colon form also absent) | Unknown |

ARM deployment-history GET returned HTTP 200 with zero records and no continuation. No artifact version/hash or route inventory was established. This does not mean the application has never been deployed. No publishing credentials, token output, deployed secret configuration download, application restart or cloud mutation was used to investigate further.

Fresh anonymous HTTPS probes: /api/status returned 200; /api/customers/me returned 404 with no Cache-Control header and no content type; /api/products/139 failed at transport level in this attempt (20-second timeout configured). The previous product-data 500 remains historical evidence rather than the current probe result. The local customer controller always sets Cache-Control: no-store, even before its disabled-authentication 404. The missing header is evidence that Azure may use a different deployed implementation or response path; it is not definitive proof of a missing endpoint because intermediaries/hosting can affect responses.

Readiness verdict: the localhost mobile URL is a confirmed blocker. The returned App Service settings do not contain customer enablement/mobile authorization. Whether bundled configuration supplies these, whether deployed code enforces Customer.Access and the existing issuer/audience contract, and whether the three production gates are false remain unknown. Customer.Access is a code policy/scope check in the current implementation, not a setting whose absence proves missing permission. No RBAC denial occurred during the performed queries; inspection is limited by absent deployment records and lack of a verified deployed artifact, not by an observed permission failure.

Smallest proposed next step is an authorized read-only inspection of the deployed API assembly and allowlisted configuration fields through an existing secure SCM/deployment-artifact path, without downloading or revealing secret configuration. Establish a recoverable deployment checkpoint before any proposed publish. If current customer code is absent, prepare a reviewed API package; if present, avoid an unnecessary deployment. Only after configuration sources are verified, propose CustomerAuthentication__Enabled=true and the existing approved mobile-client ID if missing, while explicitly preserving all three generation gates=false, existing customer mapping and operator isolation. Do not assume these two settings alone are sufficient until the deployed code and SQL read mapping are verified. After separately approved Azure readiness changes and anonymous rejection validation, propose a separately approved mobile customer base-URL build/deployment using the existing Azure HTTPS host. No new tenant, scope, public local-server exposure or TLS bypass is needed by this plan.

Approval boundary: this audit has not changed Azure, SQL, Entra or mobile configuration. Any configuration activation, cloud deployment/restart, mobile endpoint change or installation requires separate owner approval. There is no verified production-generation-disabled claim in this report. The earlier preflight/sign-in sections above are historical checkpoints superseded by these authenticated findings.
## Deployed assembly verification - Mission 012P

Authenticated SCM GET of `/api/vfs/site/wwwroot/FlyerMonkey.Api.dll` succeeded using the existing Azure session. Only this specific DLL was saved locally under `.artifacts/azure-customer-audit`; no publishing credentials or configuration dump was requested. The bearer token was kept in memory and never printed or persisted. Metadata inspection used System.Reflection.Metadata/PEReader without loading or executing deployed code.

| Identity | Result |
| --- | --- |
| Deployed assembly | FlyerMonkey.Api, assembly version 1.0.0.0 |
| Deployed SHA-256 | 7803D9319CBEA5FFDE3643AC5770E284074D13C0B7BE266441990A23688D64E9 |
| Deployed MVID | 585d8949-b050-42b0-bbb2-debac546269c |
| Local Debug SHA-256 | 1ECEB162F62BA7C5C1278E70A1CA03802794643AD848C9423123D1EEB7D7B01B |
| Local Release SHA-256 | B3DB530154F0BCB496E73893DCD3F8F620B5E0C18FD5BAE2C866F5EFEF00541C |

The deployed API has ProductsController and ImagesController, but no CustomersController, CustomerCraterController or CustomerAuthentication type. Current local Debug/Release APIs contain the customer controller/authentication implementation. Assembly version 1.0.0.0 alone is not a reliable release identifier; hashes differ. There is no source-commit identity established from this inspection.

Deployed code readiness: **verified not ready for the current customer-profile implementation**. The required implementation is missing from the retrieved API binary. The observed public `/api/customers/me` 404 is consistent with a missing implementation, rather than evidence that the current customer controller is deployed but merely disabled. Other assemblies, hosting route rewrites and live worker-loaded module identity were not exhaustively inventoried; the claim is based on the actual wwwroot API binary plus the public response, not a full runtime route dump.

Customer-auth readiness: **unverified / blocked by missing code**. The retrieved API DLL lacks the local CustomerBearer/Customer.Access implementation. Setting Enabled or MobileClientId alone cannot add that code. Existing local issuer, audience, tenant/scope, azp checks and tenant/object repository mapping must be included and reviewed in a future API artifact. No new Entra registration/scope, client secret or SQL migration is demonstrated as necessary.

Generation safety: **unverified**. The deployed binary contains a CraterIt:Enabled read, but the modern customer generation implementation and all three effective configuration values have not been established. Prior App Service settings did not return the requested gate keys. Missing keys do not prove effective false values. No configuration files/dumps, process environments or credentials were downloaded to resolve other providers. Current local default-provider precedence cannot be asserted for a different deployed startup implementation without further analysis. The read-only audit made no generation requests and did not change a gate.

Configuration precedence for a future current-source deployment: Program uses WebApplication.CreateBuilder, so appsettings files and environment-specific files provide defaults, environment variables override them, and command-line configuration has higher priority. Local launchSettings.json selects the working development profile and is not evidence of Azure runtime settings; User Secrets apply to the local Development environment. The effective cloud environment/startup and bundled configuration remain unverified. Inspect only approved nonsecret fields at source through an owner-reviewed remote read method before claiming final effective values; do not download whole configuration dumps.

### Product 139 HTTP 500 follow-up

Fresh anonymous HTTPS GETs returned `/api/products/139` 200 in about 1.02 seconds and `/api/products/139/image` 200 in about 1.41 seconds. Bodies were discarded without printing customer/product records. The previous 500 was not reproduced; an earlier follow-up had timed out. SCM application-log directory metadata returned two diagnostic files dated October 20 and 21, with no November 3 file. Those historical logs were not downloaded or used to infer the current error. Root cause is **unverified**, and no SQL fault, schema problem or authentication cause is claimed. No data was modified.

### Exact proposed next change and approvals

1. Prepare a narrowly reviewed API deployment artifact containing the current GET customer profile controller, CustomerBearer validation, mobile azp allowlist and existing read-only tenant/object account mapping. A configuration-only fix is insufficient for the inspected binary. Review the full publish contents because publishing current source may include changes beyond customer authentication. Preserve existing public catalogue/images and operator/customer isolation. Establish a verified recovery checkpoint before publishing; no deployment was prepared or executed in this audit.
2. Before activation, verify the deployed configuration sources/startup and target database read compatibility without exposing secrets or mutating records. Propose explicit `CustomerAuthentication__Enabled=true`, `CustomerAuthentication__MobileClientId=dbc3555c-669f-423e-acf3-f1cef013ce69`, and all three generation gates=false. Confirm no higher-priority provider overrides them. Keep production environment, valid TLS, existing issuer/audience/scope and account mapping. Do not weaken authentication or invoke provisioning POSTs.
3. Validate anonymous profile 401, valid authorized customer GET, wrong client/scope/operator rejection, and public catalogue/image reads, with generation disabled. Do not claim readiness from the anonymous response alone.
4. After Azure readiness is separately verified, propose the mobile build property `FlyerMonkeyCustomerApiBaseUrl` set to the existing Azure HTTPS root and an approved in-place Pixel update, followed by Wi-Fi/cellular profile and restart tests. No automatic endpoint switch was made.

Separate owner approval is required for the reviewed API deployment (including its restart implications), cloud settings/activation, any mobile URL change/build installation, and any broader SQL/Entra action if later evidence shows one is necessary. This audit authorizes none of those changes. No Azure setting, deployment, restart, SQL/Entra/mobile mutation, generation or Git operation occurred. No additional permissions were needed for the successful DLL/log-directory GETs; no publishing-profile or secret-list access was attempted.

References for the inspection method: [Kudu REST API](https://github.com/projectkudu/kudu/wiki/rest-api) and [Microsoft Kudu access overview](https://learn.microsoft.com/en-us/azure/app-service/resources-kudu). These describe access methods, not evidence of this app's deployed behavior.
## Prepared customer API release candidate - November 3, 2026

**Prepared locally; not deployed. Owner approval and the predeployment checks below are required.** This section supersedes earlier proposals that a configuration-only release might suffice.

### Artifact and exact local changes

Release framework-dependent publish command:

```powershell
dotnet publish FlyerMonkey.Api/FlyerMonkey.Api.csproj -c Release `
  -p:UseAppHost=false -p:NuGetAudit=false --no-restore `
  -o .artifacts/customer-api-release/publish
```

- Candidate: `.artifacts/customer-api-release/FlyerMonkey.Api-customer-profile-candidate.zip`, 156,287,490 bytes, 462 published files at ZIP root (no enclosing publish directory).
- ZIP SHA-256: `8C3C9694438800E5A1D0225D733CEEB9193CFEA792172F7CBCC4C0EDA38EB637`.
- Candidate API DLL SHA-256: `A02136BB0910B323EF0A8903F3AC6FE24C2EB4049C7054185D4966873ED2343E`.
- Candidate assembly version 1.0.0.0; MVID `f1090a94-f895-4013-9815-defae9ff5db4`. Metadata inspection confirms CustomersController, CustomerAuthentication and CustomerCraterController in the actual published DLL.
- File hashes/lengths: `.artifacts/customer-api-release/file-manifest.json`. Proposed nonsecret Azure values: `.artifacts/customer-api-release/proposed-azure-settings.json` (outside the deployment ZIP).
- Added source `FlyerMonkey.Api/appsettings.Production.json` containing only the three false generation gates. Verified the same booleans in the published file. It does not enable customer authentication by itself.
- Extended CustomerCrater.Checks with explicit-false cases proving denial before customer repository reads or provider invocation for the eligible, authorized fixture identity.
- Corrected a stale ApprovedArtwork.Checks assertion: the collapsed anonymous entry does not display an expanded explanation. The test now checks anonymous/operator separation in the current collapsed rendering. No website UI or authentication code was changed.
- Published web.config uses the existing in-process ASP.NET Core host with `dotnet .\FlyerMonkey.Api.dll`, stdout logging disabled. No new environment override or credential is embedded. Local appsettings and Development logging defaults contain no credentials. No PFX/P12, publish profile, user settings, secrets.json or .env file was found in the package inventory. This inventory check is not a guarantee about arbitrary future builds; approve this exact hash.

### Deployed versus candidate functionality

| Area | Inspected Azure artifact | Prepared candidate |
| --- | --- | --- |
| GET customer profile | Current CustomersController absent; anonymous 404 observed | GET /api/customers/me with explicit CustomerBearer enforcement |
| Customer identity validation | Current CustomerAuthentication absent | Existing External ID issuer/tenant, API audience, RSA signature/lifetime, v2/object claims |
| Scope/client authorization | Current customer policy absent | Customer.Access plus approved mobile azp; website client remains separately allowed |
| Account mapping | Not established in old artifact | Validated tenant/object to existing CustomerRepository.GetAsync; GET does not provision |
| Catalogue/offers/canonical images | Existing Azure paths; Product 139 data/image most recently 200 | Existing public GET routes retained; fake-repository/Blob tests passed |
| Operator behavior | Old artifact; current config not comprehensively verified | Existing independent operator bearer/policies retained; isolation/permission tests passed |
| Generation | Effective old production gates unknown | All three false in Production defaults; customer/operator writes also require Development/local boundaries |

The candidate includes FlyerMonkey.Api, Shared and SQLServerConnection assemblies and their dependencies, ASP.NET runtime/dependency descriptors, static web assets and the local approved-artwork manifest. It is a full current API publish, not a DLL-only patch. Route/dependency/storage differences beyond customer authentication must be approved before publishing. Existing deployed artwork manifest parity and operator configuration compatibility are predeployment blockers, not verified by the offline tests.

Important release surface: the existing source also publishes authenticated POST `/api/customers/session` (website provisioning) and local-only customer attempt/image routes. Mobile uses GET only. Enabling customer auth makes the provisioning route available to policy-authorized clients, although it was not invoked against real SQL during preparation. Owner review must explicitly accept that existing route surface or request a separately scoped change; do not describe this artifact as containing only a new GET. Customer generation routes remain blocked by Production/loopback/HTTPS and false gates. No database migration or startup schema update is present in Program.cs.

### Validation performed

| Check | Result |
| --- | --- |
| Release publish | Succeeded, zero compiler errors; final publish output had no warnings |
| CustomerAuthentication.Checks | 79 passed |
| CustomerCrater.Checks | 65 passed (3 new explicit-false checks) |
| Authentication.Checks | 53 passed |
| ProductSecurity.Checks | 47 passed |
| ApprovedArtwork.Checks | 126 passed after stale rendering assertion correction |
| CraterIt.Checks | 119 passed |
| ImageGeneration.Checks | 15 passed |

Total: **604 offline checks passed**. Fresh Release test builds were used; the initial Debug run was blocked by the running local API locking its DLL. It was not stopped or restarted. Existing two unused Offers fields and test-only BuildServiceProvider analyzer warnings appeared during test compilation. Tests use locally signed JWTs, fake SQL/repositories/storage, fake providers and in-memory HTTP/SDK transports; any generation exercises were fixtures, not production or paid calls. No real SQL write, schema change or provider call occurred.

Evidence covers issuer/audience/expiry rejection, mobile client allowlisting, invalid configuration denial, website/mobile identity continuity without GET provisioning, customer/operator isolation, anonymous reads, canonical image confinement, offers routes and approved artwork. Live Azure SQL/identity/storage compatibility and real CustomerId continuity are not established by those fixtures.

Generation proof: CustomerCrater checks deny a Production request even for an otherwise eligible/granted identity before SQL/provider access. Explicit false CustomerCrater:Enabled or CraterIt:Enabled also rejects in Development before those accesses. Authentication checks reject Production operator generation despite all grants/flags and reject the false local-development generation flag without resolving a provider. The three gates apply to their respective routes; no claim is made that each gate alone controls every generation path. With all three false and Production environment, neither customer eligibility nor operator grants permit provider dispatch in the tested routes.

### Exact proposed Azure settings (NOT APPLIED)

| App Service setting | Proposed string value |
| --- | --- |
| CustomerAuthentication__Enabled | true |
| CustomerAuthentication__MobileClientId | dbc3555c-669f-423e-acf3-f1cef013ce69 |
| CraterIt__Enabled | false |
| CustomerCrater__Enabled | false |
| CraterIt__LocalDevelopmentGenerationEnabled | false |

Preserve existing SQL connection, Blob/approved-artwork configuration, managed identity, operator settings and all unrelated settings. Do not replace the complete app-setting dictionary with these five entries. The local issuer/audience/scope are code constants already listed above; no new customer client secret or operator scope is required. Do not populate AllowedCustomerId or grant generation permissions.

Require effective Production environment; ASPNETCORE_ENVIRONMENT/DOTNET_ENVIRONMENT and startup configuration must be checked without conflict. If either needs modification, prepare its exact additional diff and obtain approval before applying it. Verify colon/double-underscore aliases and higher-priority command-line/hosting overrides, so the three false settings remain effective. The bundled Production file is a default safeguard, not evidence of the current Azure process configuration. Normal HTTPS and token metadata validation remain mandatory.

### Deployment checklist and acceptance plan (NOT EXECUTED)

1. Reconfirm subscription/resource group/site and existing context. Review the candidate hash, full file manifest, additional authenticated route surface, runtime/dependency changes and test results with the owner. Confirm no concurrent deployment.
2. Obtain a verified full current-artifact recovery source and protected settings snapshot as described below. The previously downloaded DLL alone is insufficient. Confirm host runtime, operator config compatibility (including any newly required operator identifiers when Authentication:Enabled=true), current approved-artwork manifest and SQL read-schema/mapping compatibility. Use no SQL writes/migrations; stop for missing schema or access.
3. Confirm effective production generation safety and all configuration sources. Review the exact five-setting patch and any discovered additional diff; preserve unrelated settings. Settings changes and publishing can restart the app, so coordinate one approved window. Do not transiently enable customer authentication against an incompatible old artifact. Keep generation false throughout, including failed deployment/rollback states.
4. Owner explicitly approves cloud settings and deployment of the reviewed artifact. Use the existing supported deployment method only after confirming compatibility with the framework-dependent package; this ZIP has not been uploaded. Preserve current artwork/manifest and external data. No new slot, resource or public development endpoint is authorized by this plan.
5. Verify deployed DLL hash matches the candidate, HTTPS/runtime startup and effective false gates. Anonymous GET /api/customers/me must be 401, with no-store and no customer response body. Check public GET /api/products, /api/products/139, /api/offers, /api/offers/current, /api/offers/previous, canonical image and approved artwork. Avoid live POST generation/provisioning probes.
6. With owner-assisted approved customer authentication, check valid profile GET and privately compare identity continuity, without printing CustomerId/tokens. Verify wrong client/scope and operator/customer cross-use are rejected. Disabled/missing account results must not create records. Offline tests cover these denials; live test tokens must remain private.
7. Confirm operator read/ownership behavior using the existing authorized session; no write/admin grant changes. Investigate any recurrence of Product 139 500 using time-correlated sanitized existing logs, not speculative SQL edits. Stop activation on catalogue/image/operator regressions, anonymous customer exposure or any generation uncertainty.
8. Only after Azure acceptance, separately approve a mobile build URL change and Pixel update. The current mission does not change/install mobile. Future acceptance includes unplugged Wi-Fi and cellular profile refresh, provider first returns, sign-out/re-entry and new-process restoration. Browser authentication success alone is insufficient.

### Rollback plan and deployment risks

**Rollback is planned, not yet ready to execute. No full recoverable checkpoint was captured during this preparation.** The inspected old API DLL hash is 7803D9319CBEA5FFDE3643AC5770E284074D13C0B7BE266441990A23688D64E9; that single DLL cannot reconstruct all dependencies/content/configuration. ARM deployment history previously returned no records. Do not deploy until an actual matching protected package/backup and a supported restore method are verified.

- Before deployment, identify an existing Azure restore point/package or capture a protected full artifact checkpoint under separately agreed handling. Whole wwwroot archives/settings exports may contain secrets; do not put them in this document, normal logs or Git. No such archive/settings dump was downloaded here. Store any owner-approved backup encrypted with access restricted, and validate hashes/readability and required dependencies/web.config/current approval manifest. Do not expose or rotate secret values.
- Record the five planned key states privately from all effective providers. Prior audit found these keys absent in returned App Service settings, but bundled/other sources remain unverified. Preserve the entire unrelated settings set without printing it. App-setting snapshots and code backups are separate recovery requirements.
- On failure, retain sanitized diagnostics; disable/revert customer activation in an owner-approved action if needed, restore the verified old full artifact through the supported deployment method, and restore the exact prior customer setting states. Do not restore a database, delete artwork, reset attempts or clear mobile caches. Keep all generation gates false during rollback; if old settings cannot be shown safe, stop and obtain an explicit safety-preserving rollback diff rather than blindly restoring unknown gate values.
- Recheck HTTPS/status, public catalogue/offers/images, approved-artwork hashes, expected old customer-route behavior and operator reads. Restoring old code removes customer-profile capability, so untethered mobile customer status would become unavailable again. No mobile endpoint has changed in this mission.
- Risks: full-publish transitive changes; new authenticated provisioning surface; .NET/dependency/startup and operator-config mismatch; SQL read-schema/account-map mismatch; approved-artwork manifest replacement; stale files from the chosen deploy method; settings/deployment restarts; unknown prior generation state; and incomplete rollback material. These are release blockers to resolve before approval, not reasons to modify data or bypass authentication.

Preparation is complete for review. **STOP before deployment:** owner approval must name the exact artifact and settings patch after the recovery/configuration blockers are resolved. No cloud setting, deployment, restart, database mutation, mobile change, paid generation or Git operation was performed.

## Final release safety gate - November 3, 2026

**Decision: NO-GO for publishing at this checkpoint.** Offline release preparation, secure rollback capture and database metadata compatibility passed. A live restore/startup rehearsal was not performed, and the live worker's final effective environment/provider values have not been independently attested. No deployment/configuration action is authorized by this report. The 604 offline checks remain valid for the unchanged candidate.

### Secure rollback checkpoint - captured and verified

Protected local directory: `.artifacts/customer-api-release/protected-checkpoint-20261103T145010Z` (captured November 3 at 22:50 Perth). Files are encrypted using AES with a random 256-bit key; that key is wrapped with Windows DPAPI CurrentUser. NTFS inheritance is disabled and access is restricted to the capturing Windows account and SYSTEM. Restricted-sandbox reads were denied, as expected. The directory is not a portable disaster-recovery backup: DPAPI recovery requires the authorized account's Windows profile/key material. No separate protected off-host copy has been established.

Captured via existing authenticated read-only SCM/management access:

- Complete wwwroot ZIP, including 473 file streams and the existing API/dependencies, hosting files, configuration and artwork manifest.
- Site resource metadata and web/startup configuration, App Service app settings, connection-string configuration and authsettingsV2. The settings-list POST operations were read-only retrieval actions, not updates. Sensitive collections were serialized in memory and persisted only inside the encrypted envelope, never printed or placed in documentation. Key Vault references, if present, are references; external vault/identity/RBAC resources and databases are not backed up by this snapshot.
- `wwwroot.zip.aes`, `configuration.json.aes`, DPAPI-wrapped key, restricted file inventory/checkpoint metadata and hashes of encrypted files. No plaintext ZIP/configuration backup was written.

Verification reopened the persisted wrapped key/envelopes, decrypted into memory, matched the archive/configuration SHA-256 values, checked required files, rejected unsafe ZIP paths and read/hashed all 473 archive entries. The captured API SHA-256 matches the inspected production DLL: `7803D9319CBEA5FFDE3643AC5770E284074D13C0B7BE266441990A23688D64E9`. ZIP plaintext SHA-256: `6C1B58A74A6469306C41972AD4142CE97FA6231D41A269A1FEF5D23591F4C2E4`. Individual secret values/hashes were not reported.

The API remains Running, HTTPS-only, runtime v10.0. Resource modification metadata remained October 31, 2026 17:28:28 UTC. The API hash did not change during the checkpoint. This supports stability but does not make a live filesystem/configuration capture transactionally atomic; reconfirm no concurrent deployment at the approved publish window.

**Restore capability verified:** encrypted material can be decrypted and every archived file recovered/read with matching hashes under the authorized Windows identity. **Not verified:** restoring/starting the previous application in Azure or an isolated equivalent host. No restore, slot/resource creation, service restart or production secret injection into another host was performed. A single old DLL is no longer the only recovery material, but offline ZIP integrity alone is not a successful application restore rehearsal.

### Configuration-source inspection

The captured deployed archive contains two appsettings JSON files; the requested customer-auth/generation keys were absent from their inspected fields. No appsettings.Production.json exists in the deployed archive. The management App Service settings and colon-form aliases contain none of the five planned keys, and neither ASPNETCORE_ENVIRONMENT nor DOTNET_ENVIRONMENT was present in that setting collection. Captured web.config starts `dotnet .\FlyerMonkey.Api.dll`, contains no relevant environment-variable overrides, and the captured web configuration reports no custom startup command or relevant conflicting settings.

No conflict was found in these inspected sources. Do not promote that statement to live worker attestation: worker process environment, hosting-injected providers and the old Program's complete configuration-building logic were not exhaustively evaluated. In particular, the current deployed binary lacks the new customer authentication implementation and cannot be made ready by settings alone. Its three requested generation values are not explicitly false settings. They remain **unverified as live effective values**, rather than silently equated to absent=false.

For the candidate, CreateBuilder configuration precedence is known and the published Production file sets CraterIt:Enabled=false, CustomerCrater:Enabled=false and CraterIt:LocalDevelopmentGenerationEnabled=false. The proposed App Service patch explicitly repeats those false strings and enables customer authentication plus the approved Android client only. Normal default configuration providers give environment variables precedence over JSON and command-line values precedence over environment settings. Require an effective Production environment with no conflicting startup override, and verify these gates after an approved publish before customer acceptance. No gate was changed in Azure during this check.

The unchanged candidate ZIP SHA-256 remains `8C3C9694438800E5A1D0225D733CEEB9193CFEA792172F7CBCC4C0EDA38EB637`. The deployed/candidate approved-artwork manifest byte hashes differ, but canonicalized JSON contents match semantically, with one entry in each. No artwork approval change is intended. Preserve the current deployed bytes in the rollback archive and verify unchanged approval semantics/hashes during acceptance.

### Provisioning route security and write review

POST `/api/customers/session` uses the same Account helper as GET `/api/customers/me`. The controller's AllowAnonymous attribute avoids the operator fallback policy; it does not bypass the explicit CustomerBearer authentication and Customer.Access authorization inside that helper. Disabled customer authentication returns 404; missing/invalid customer bearer returns 401; policy failures return 403.

- The existing External ID issuer, API audience, signature, expiry, v2, tenant/object and Customer.Access scope checks apply.
- The authorized azp is the existing website public identifier or the explicitly configured approved mobile client. Both allowed clients can call provisioning with Customer.Access; there is no website-only restriction on this POST. Mobile's current code does not call it. Enabling this release therefore authorizes that existing write surface for policy-valid mobile tokens as well as website tokens; owner approval must explicitly accept it or request a separately scoped restriction before publication.
- Tenant/object GUIDs come only from the validated principal. No body, URL, email or arbitrary CustomerId selects the identity/account. SQL parameters bind those claims; SQL creates a new CustomerId with NEWID().
- A missing identity mapping results in one INSERT with Status=Active, CraterEnabled=0 and UTC timestamps; the returned profile masks CraterEnabled=false. This is an expected provisioning write after activation, even though this preparation made no SQL writes. No credits, grants or operator permissions are assigned.
- Existing mappings are selected without UPDATE, and disabled records remain disabled (403). A Serializable transaction plus UPDLOCK/HOLDLOCK and the verified unique tenant/object index prevents duplicate concurrent first-account insertion. Repeated calls return the existing mapping; no real idempotency/provisioning write test was run in this mission.
- Operator tokens do not authenticate CustomerBearer; customer credentials do not confer operator scope/identity/policies. Offline isolation tests passed. Customer generation still requires separate guards and is blocked by Production/local boundaries and the three false settings.

### Database compatibility - metadata-only verified

The encrypted captured deployment's existing SQL connection was passed privately via stdin to a local metadata-only probe using Microsoft.Data.SqlClient 6.1.1, the same driver version used by the candidate. The connection string was not printed or placed on the command line. No connection/TLS options were weakened. An initial legacy System.Data probe failed without a diagnostic claim; the API-driver retry succeeded.

The probe queried sys catalog metadata and permission functions only. It read no customer rows and performed no INSERT/UPDATE/DELETE, transaction commit, migration or schema operation.

Verified dbo.Customers has exactly the seven required nonnullable columns: CustomerId/ExternalTenantId/ExternalUserId uniqueidentifier; Status nvarchar(16); CraterEnabled bit; CreatedUtc/UpdatedUtc datetime2. Primary key on CustomerId and enabled unfiltered unique index UQ_Customers_ExternalIdentity on ExternalTenantId,ExternalUserId are present. The existing connection has SELECT and INSERT permission; no enabled table triggers were reported. These support the reviewed GET/provisioning schema contract. Real customer mapping continuity, SQL insert behavior and Azure-worker-specific connectivity were not exercised. No new database permission or schema change is proposed.

### Remaining blockers, exact publish sequence and rollback triggers

Before changing the NO-GO decision:

1. Independently verify live effective Production environment/startup and generation configuration through an owner-reviewed nonsecret-only method, or approve a narrowly specified preparation/verification approach. Do not dump the worker environment or enable a diagnostic endpoint exposing secrets.
2. Verify the rollback application can start through an existing safe isolated recovery route, or explicitly review/accept the documented offline-only restore evidence and Windows-DPAPI dependency. Do not create resources, restore production or restart anything under this mission. Retain a protected second recovery copy if host-loss recovery is required; moving encrypted files alone does not remove DPAPI dependence.
3. Owner must explicitly accept the authenticated provisioning route and expected one-time INSERT behavior for both authorized clients, the full publish/dependency delta, the exact five-setting patch and deployment/restart window. Any narrower route/client restriction needs a separately reviewed candidate/hash and affected checks.

After blockers are resolved and the owner explicitly approves publishing this exact artifact/settings:

1. Reconfirm site/subscription, candidate/checkpoint hashes, no concurrent publish and decryptability of the recovery material; preserve unrelated settings/secrets/storage/identity. Confirm effective Production and false gates before activation.
2. Use the existing supported App Service deployment method with the reviewed full package, accounting for deployment restart and stale-file behavior. Do not replace only the DLL or overwrite all settings with the five-setting patch. Apply the approved nonsecret setting changes in a coordinated window; never enable customer auth against the incompatible old binary or enable generation.
3. Confirm deployed DLL hash, safe startup and effective false gates. Anonymous profile must return 401/no-store, not customer data. Verify catalogue/offers/canonical and approved images, operator reads/isolation and owner-assisted authorized profile GET/identity continuity. Do not execute provisioning or generation POSTs during acceptance without separate explicit authorization for their expected effects.
4. Only after cloud acceptance, separately approve any mobile URL/build/install work and untethered Wi-Fi/cellular acceptance. No mobile change is part of this release safety gate.

Immediate rollback/stop triggers: anonymous customer exposure; unexpected generation availability/provider dispatch; inability to establish false gates; startup/health failure; recurring catalogue/offer/image failures; operator isolation/read regression; unexpected customer mapping; or changed artwork approvals. Freeze further changes and retain sanitized evidence. With separate owner authorization, return customer activation to its prior state, restore the verified old *complete* artifact through the supported method, and restore the captured relevant settings privately while preserving unrelated secrets and external data. Keep generation false throughout recovery; do not blindly restore unknown unsafe gate values. No database restore, artwork deletion, attempt reset or mobile-data clearing is part of rollback. Verify the original DLL hash and old public/operator behavior; customer profiles are expected to become unavailable again on old code.

Protected restore procedure: run only under the authorized Windows recovery account, verify encrypted-file hashes, unwrap the DPAPI key, decrypt/archive-verify into a protected stream or restricted encrypted staging area, and use the supported App Service restore/deployment mechanism after approval. Parse/apply the captured relevant configuration internally without console output; stop if any credential/Key Vault reference or provider cannot be restored without additional authorization. Do not paste decrypted configuration into portal editors, logs or this report. A verified Azure restart/boot result cannot be promised from the offline check alone.

No Azure settings, deployment, restart, SQL records/schema, Entra/mobile configuration, generation or Git operations were changed. All sensitive backup material remains encrypted at rest under the protected checkpoint directory. Final verdict remains **NO-GO pending the listed runtime/recovery assurance and explicit owner acceptance/approval**, not a claim that the candidate failed its offline checks.
