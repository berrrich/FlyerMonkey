# Mission 010B — local Web/API Entra integration

The developer confirms the real-browser smoke test passed: explicit Entra operator sign-in, matching tenant/operator, ViewOwn-only permission, local logout without automatic reauthentication, optional Microsoft logout, explicit sign-in message on subsequent protected access, public catalogue access and disabled Production generation. These are developer-reported integration results; the agent's checks remain offline. The Git checkpoint does not authorize Azure changes, credentials, Android registration, production configuration, deployment or paid calls.

## Agreed identity settings

| Setting | Exact value |
| --- | --- |
| Tenant ID | `4112a2a6-70d8-47f0-a0fe-352edd57cc04` |
| API application/client ID; expected v2 access-token audience | `52691bc6-97d4-4abc-bc11-19b3d198a288` |
| API scope requested by Web | `api://52691bc6-97d4-4abc-bc11-19b3d198a288/Crater.Access` |
| API validated issuer | `https://login.microsoftonline.com/4112a2a6-70d8-47f0-a0fe-352edd57cc04/v2.0` |
| Web application/client ID; expected Web ID-token audience and API access-token azp | `fcadb3a5-2941-4b27-946a-785608758ff1` |
| Local Web sign-in callback | `https://localhost:7094/signin-oidc` |
| Local Web sign-out callback | `https://localhost:7094/signout-callback-oidc` |
| Proposed front-channel logout URL, owner must inspect | `https://localhost:7094/signout-oidc` |
| Local private API root | `https://localhost:7255/` |
| Operator Object ID | Owner supplies privately; `Authentication:OperatorObjectId` in both User Secrets stores |

API and Web administrator-consented Crater.Access agree. The scope URI and v2 audience have different purposes: request the full scope URI, validate the bare API application GUID. API additionally requires `ver=2.0`, `tid` matching the tenant, `oid` matching the operator, `scp` containing `Crater.Access`, and `azp` matching the Web client. Signatures, issuer, audience, expiry/not-before and signing-key rollover use the supported JWT middleware. An ID token with Web audience cannot call the API.

The nonsecret tenant/API/Web identifiers live only in the dedicated `operator-local` launch profiles, together with `Authentication__Enabled=false`. Both tracked `appsettings.Development.json` files are restored to logging-only templates as required by the pre-commit gate. Never put the operator Object ID or credentials into either location; their existing User Secrets stores are unchanged. No Production appsettings were edited. The profiles bind HTTPS localhost only and force authentication off until the developer supplies an explicit command-line enable flag. The API profile sets `CraterIt:Enabled=false`, Local storage and a ViewOwn-only policy cap. This cap denies Generate/Approve/Admin even if older grant-array entries exist in User Secrets. Existing SQL/Blob/approved-artwork settings remain unchanged.

## Provide the operator ID and local credential

Use an ordinary interactive PowerShell terminal, outside chat and without transcripts/debug tracing. From the repository root:

```powershell
./scripts/Set-LocalOperatorSecrets.ps1 -Operator
./scripts/Set-LocalOperatorSecrets.ps1 -WebCredential
```

Both commands use masked prompts; values are never command-line arguments. The helper validates the operator GUID and writes it as `Authentication:OperatorObjectId` to **both** existing stores, then writes the credential only as Web `Authentication:WebClientSecret`. It pipes JSON into `dotnet user-secrets set --id ...`, preserving unrelated secret keys. It does not enable authentication. Do not run `dotnet user-secrets list`, paste values into commands/chat, or send screenshots of the secret. If either store write fails, rerun `-Operator` to make both consistent.

| Project | Existing UserSecretsId | Stored keys for this mission |
| --- | --- | --- |
| FlyerMonkey.Api | `c6eccfba-8735-4ee1-8d47-af392dde717e` | `Authentication:OperatorObjectId` only |
| FlyerMonkey.Web | `67477fe5-280d-4b9b-86b5-0500cc141dc8` | `Authentication:OperatorObjectId`, `Authentication:WebClientSecret` |

ASP.NET Core loads these stores automatically in Development through the existing project UserSecretsId properties. Standard Windows location: `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`, outside the repository. User Secrets is **not encrypted**: protect the developer account, disk and file permissions; it is for local development only. The helper briefly holds plaintext in process memory for the Secret Manager pipe; it does not guarantee erasure of managed strings. The agent did not read, initialize or overwrite either real secret store. Existing SQL connection configuration is a separate startup prerequisite and must stay private.

If the Web credential does not already exist, the **developer** must perform these portal steps:

1. Switch to tenant `4112a2a6-70d8-47f0-a0fe-352edd57cc04` in Microsoft Entra admin center.
2. App registrations > FlyerMonkey Web; verify application ID `fcadb3a5-2941-4b27-946a-785608758ff1` (not the API or a historical registration).
3. Certificates & secrets > Client secrets > New client secret. Use description `FlyerMonkey local Web sign-in pilot`; choose the shortest practical expiry allowed by policy and privately record the expiry date.
4. Copy the newly displayed **Value**, not Secret ID, directly into the helper's masked `-WebCredential` prompt. The value is shown only at creation; do not ask the agent to view it. Clear the clipboard afterward. Do not create an API secret.
5. If no valid Value is available, the developer must decide whether to create a replacement. The agent does not create, rotate or delete credentials.

Remaining portal inspection: verify both registered callbacks, single-tenant account type, implicit grants disabled and confidential-client settings. Inspect front-channel logout URL separately; it was not included in the confirmed inventory. If `Assignment required? Yes`, the dedicated operator must be assigned to the Web enterprise application; no directory administrator role is needed. Do not alter historical registrations. App assignment is an extra gate; the API's exact tenant/object allowlist remains mandatory.

## No User.Read requirement

Web uses OIDC `openid`/`profile` and `offline_access`, plus the API's delegated Crater.Access scope. It does not call Microsoft Graph or `/me`. UserInfo calls are explicitly disabled; `tid`/`oid` come from validated identity-provider tokens. Do **not** add `User.Read`, Graph `.default`, Graph client code or application permissions to fix a callback, consent, audience or credential error. Offline challenge tests verify the requested scope and absence of User.Read/Graph. No consent change is performed by the agent.

## Exact first local smoke test — developer operated

No browser or API is started by the preparation work. Execute only after providing the operator ID/credential locally and deciding to enable this test. Do not send generation POSTs or paid requests at any step.

1. Check the local ASP.NET HTTPS development certificate. If it is not trusted, review and run `dotnet dev-certs https --trust` locally; never disable certificate validation. Ensure ports 7255 and 7094 are free. Keep the API's existing `FLYERMONKEY_SQL_CONNECTION` available without printing its value; public catalogue reads can use the existing database. Do not import fixtures or modify artwork.
2. Optional disabled baseline: run either project with `--launch-profile operator-local` and **without** the enable flag. Authentication remains off; private access remains denied. Stop that process before the enabled test. A legacy launch profile with its old local-generation opt-in is not the profile for this mission.
3. In terminal A, start the API explicitly:

   ```powershell
   dotnet run --project FlyerMonkey.Api/FlyerMonkey.Api.csproj --launch-profile operator-local -- --Authentication:Enabled=true
   ```

4. In terminal B, start Web explicitly:

   ```powershell
   dotnet run --project FlyerMonkey/FlyerMonkey.Web/FlyerMonkey.Web.csproj --launch-profile operator-local -- --Authentication:Enabled=true
   ```

   CLI configuration takes precedence over the profile's explicit false default. The profile limits private Web requests to `https://localhost:7255/`; a missing or nonloopback Development private API target fails closed. Do not set permanent `Authentication:Enabled=true` in User Secrets. No Android setup is part of this test.

5. Anonymous baseline, using a fresh browser profile or a client with no bearer token:
   - `https://localhost:7255/api/products` returns the existing public catalogue (subject to existing SQL connectivity).
   - `https://localhost:7255/api/products/139/image` and `/api/products/102/approved-artwork/image` retain existing image/feature-flag behavior, without a sign-in redirect or authentication challenge. A disabled approved-artwork flag can legitimately return 404; do not change the flag to make the test pass.
   - `https://localhost:7255/api/images/artwork?productId=102` returns 401.
   - Browse `https://localhost:7094/products` signed out. Existing Web public catalogue clients still read the published public API; they are not repointed or given private tokens. The direct localhost catalogue GET above is what verifies the local API's anonymous boundary.
6. Visit `https://localhost:7094/operator/signin`. Use the dedicated operator account; complete MFA if Entra requires it. MFA registration alone does not prove that each sign-in will require MFA; inspect actual sign-in policy/outcome. Expect `/operator` after the real provider callback. Never capture a full callback URL, authorization code, token, credential, full claims dump or configuration dump.
7. Follow **Check local identity and API permissions** to `https://localhost:7094/operator/access`. This obtains the API token on the server and GETs the Development-only API diagnostic endpoint. Expected JSON:

   ```json
   {
     "authenticated": true,
     "tenantMatches": true,
     "operatorMatches": true,
     "permissions": {
       "Crater.Generate": false,
       "Crater.ViewOwn": true,
       "Crater.Approve": false,
       "Crater.Admin": false
     },
     "productionGenerationHardBlocked": true
   }
   ```

   This compares the bearer token's **validated** tid/oid with local configuration and evaluates the real policies. It does not return IDs or tokens and does not invoke generation, approval or administration. The Production-generation field describes the preserved hard guard; the offline Production test independently proves denial before provider construction. If any expected match/grant differs, stop the test and inspect private configuration. Do not widen grants or add Graph permissions.
8. Return to `/operator`, enter product ID 102 and select View my artwork. Expect HTTP 200 and only the operator's records, commonly `[]` because existing records have no owner. Ownerless and other-owner images must remain hidden; no migration, reassignment or new generation is needed for this test.
9. Use the sign-out form. Its antiforgery-protected POST revokes the local server session, expires the cookie and redirects to `/operator/signed-out`. Microsoft logout is a separate optional antiforgery-protected button on that page. `/operator`, `/operator/access` and `/operator/artwork` must return **401 without any sign-in/provider redirect** after local logout, whether Microsoft logout is skipped, cancelled, completed or fails. Only explicitly visiting `/operator/signin` may start a new Entra login. Logout is not immediate revocation of already-issued API bearer tokens.
10. Test a different **existing** identity in a fresh browser profile. Do not create a tester account. Either enterprise-app assignment denies it or Web's validated tid/oid check rejects its callback; it must never obtain an authorized local operator session. The API independently returns 403 for a valid token from a wrong operator (covered offline without exporting a real bearer token). If there is no authorized second account available, mark this live check pending rather than broadening access.
11. Stop both owned local processes with Ctrl+C. Do not leave enabled auth switches in environment/User Secrets. Restart without the explicit enable flag to return to the disabled baseline. Record only app version, timestamps, HTTP results, identity-match booleans, permission decisions and pass/fail. No tokens, secrets or object ID need to be recorded in the report.

## Offline results and limitations

Existing Stage 3 results are historical in AuthenticationFoundation.md. All identity tests use ephemeral local keys/static metadata and fake storage/repositories. They are not real Entra login/MFA/consent integration tests.

| Verification run for this mission | Result |
| --- | --- |
| `dotnet run --project tests/Authentication.Checks --no-restore --no-launch-profile` | 47 passed, including private identity-match evidence, ViewOwn-only cap despite stale write/admin grants, wrong-operator denial, diagnostic denial outside Development and Production generation hard block before provider construction. |
| `dotnet run --project tests/WebAuthentication.Checks --no-restore --no-launch-profile` | 25 passed, including matching actual local Web/API IDs, disabled defaults, no credential/owner ID in development files, API scope without Graph, no UserInfo call, explicit HTTPS root, local-target restriction, cookie and antiforgery logout. |
| `dotnet run --project tests/CraterIt.Checks --no-restore --no-launch-profile -p:UseAppHost=false` | 118 passed; fake generator only, including ownership persistence and existing guards. |
| `dotnet run --project tests/ProductSecurity.Checks --no-restore --no-launch-profile` | 47 passed; product writes remain unavailable and image paths remain container-confined. Cached NU1900 warning: current package vulnerability lookup unavailable, so no complete current audit is claimed. |
| `dotnet build FlyerMonkey.Api/FlyerMonkey.Api.csproj --no-restore -v minimal` | Passed, zero warnings/errors. |
| `dotnet build FlyerMonkey/FlyerMonkey.Web/FlyerMonkey.Web.csproj --no-restore -v minimal` | Passed, zero warnings/errors. |
| Secret helper PowerShell parser | Passed; helper was not executed, no secret-store writes performed. |
| `git diff --check` | Passed; only existing LF/CRLF conversion notices. |

## Files changed in this mission

These changes sit on top of the previous uncommitted foundation. No Android or Shared files were edited in this mission, and unrelated publication notes/tool files were not changed.

| Files | Change |
| --- | --- |
| `FlyerMonkey.Api/appsettings.Development.json`; `FlyerMonkey/FlyerMonkey.Web/appsettings.Development.json` | Logging-only templates; local identity defaults moved out before the Git checkpoint. |
| `FlyerMonkey.Api/Properties/launchSettings.json`; `FlyerMonkey/FlyerMonkey.Web/Properties/launchSettings.json` | Nonsecret IDs, ViewOwn-only settings and new `operator-local` profiles with HTTPS localhost binding and explicit false auth defaults. |
| `FlyerMonkey.Api/Services/CraterAuthorization.cs` | Read-only pilot cap, preserving the existing Production hard guard. |
| `FlyerMonkey.Api/Controllers/OperatorAccessController.cs` | New authenticated Development-only identity-match and policy evidence endpoint; no token/ID output or writes. |
| `FlyerMonkey/FlyerMonkey.Web/Services/OperatorAuthentication.cs`; `FlyerMonkey/FlyerMonkey.Web/Program.cs` | Explicit private API target, local-only Development target, missing credential failure, no UserInfo call and local access-evidence proxy/link. |
| `scripts/Set-LocalOperatorSecrets.ps1` | Masked operator/credential input into existing User Secrets stores through stdin. |
| `tests/Authentication.Checks/Program.cs`; `tests/WebAuthentication.Checks/Program.cs` | Regressions for local configuration, scopes, capped permissions and diagnostics. |
| `docs/AuthenticationFoundation.md`; `docs/LocalEntraSignIn.md` | Updated identity inventory and this current procedure/results. |

The developer now confirms real local login and API token acquisition work. The logout-security changes below still require a browser retest. Existing SQL availability, trusted HTTPS and identity-provider connectivity remain real-run prerequisites. Private galleries in Production remain outside scope because the existing Azure private store is Development-only. In-memory token/session caches are local pilot choices; no production cache/Data Protection design or credential provisioning is performed.

References:
- [Microsoft User Secrets and its development-only storage model](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0)
- [Microsoft scopes and OIDC permissions](https://learn.microsoft.com/en-us/entra/identity-platform/scopes-oidc)
- [Microsoft claims validation and API audience](https://learn.microsoft.com/en-us/entra/identity-platform/claims-validation)

## First real sign-in repair

The Web test harness previously omitted the shared Blazor endpoint set and did not exercise token-acquisition calls. It therefore missed the two developer-reported runtime failures.

- `/operator` had both the minimal HTTP endpoint and the shared mobile Blazor page. Web retains the protected HTTP `/operator` endpoint and its existing sign-in return URL. The mobile component now uses `/operator/mobile`, and its conditional mobile navigation link follows that path. No mobile authentication options changed.
- All three Web `GetAccessTokenForUserAsync` calls now supply `authenticationScheme: OpenIdConnectDefaults.AuthenticationScheme`: the access-check endpoint, artwork endpoint and Blazor token provider. Cookie authentication remains the local session mechanism; OIDC remains the challenge/token-acquisition scheme. No default scheme, credential, permission or allowlist was weakened. This follows [Microsoft.Identity.Web's explicit-scheme guidance for IDW10503](https://github.com/AzureAD/microsoft-identity-web/wiki/multiple-authentication-schemes).

The expanded Web harness loads the same shared Blazor endpoints as Web, verifies one `/operator` endpoint, checks the protected sign-in return target and separate mobile route, and intercepts all three token calls to require the OIDC scheme, authenticated user and exact delegated API scope. The interceptor deliberately raises an interaction-required exception: it issues no token and makes no API/identity network request. HTTP paths must return to sign-in, and the Blazor provider must propagate the interaction requirement. Real Identity.Web internal lifecycle behavior is retained for the cookie/sign-out checks.

Repair verification:

| Check | Result |
| --- | --- |
| WebAuthentication.Checks | 34 passed with shared Blazor routes loaded, including sign-in, access/artwork reauthentication, scheme selection, antiforgery sign-out and cookie removal. |
| Authentication.Checks | 47 passed; operator allowlist, ViewOwn cap, ownership and Production generation denial remain covered. |
| CraterIt.Checks | 118 passed. |
| ProductSecurity.Checks | 47 passed; cached NU1900 vulnerability-source warning remains. |
| Web and API builds | Passed, zero warnings/errors in final builds. |

Changed for this repair: `FlyerMonkey.Shared/Pages/Operator.razor`, `FlyerMonkey.Shared/Layout/NavMenu.razor`, `FlyerMonkey.Web/Services/OperatorAuthentication.cs`, `tests/WebAuthentication.Checks/Program.cs`, and this guide. No Entra settings, credentials, local identity configuration or Production settings changed.

Manual browser retest:

1. Stop/restart the owned local API and Web processes using the explicit enabled `operator-local` commands above. The Web process restart clears the in-memory token cache; use a fresh browser profile or repeat `/operator/signin` to acquire a fresh session/token.
2. Sign in as the dedicated operator. `/operator` must render once without AmbiguousMatchException.
3. Follow `/operator/access`. It must return the expected identity-match booleans and ViewOwn-only permissions, without IDW10503. If interaction is required after restart, complete a fresh sign-in and retry. No permission widening is needed.
4. Submit a product ID on `/operator`. Owned artwork may be an empty array; it must not fail with scheme inference or expose ownerless artwork.
5. Use the sign-out form and verify local 401 responses first. Microsoft logout is optional and separate; see the logout-security retest below. A redirect back into Entra on protected access is now a failure, not a successful logout check.
6. Recheck anonymous catalogue and, if an existing approved second identity is available, wrong-operator denial. Do not send generation requests. Production generation remains hard-blocked and is verified offline.

These browser checks remain unexecuted by the agent; offline challenge/cookie tests do not establish a successful real Entra token exchange or callback.

## Operator logout security fix — current behavior

### Root cause and evidence

The previous default challenge scheme was OpenIdConnect. Even after cookie deletion, a fresh protected request automatically redirected to Entra. An existing Microsoft SSO session could then issue a fresh local cookie without a visible login prompt. Token-acquisition interaction failures also redirected straight into `/operator/signin`.

The regression reproduced **local cookie expiration followed by HTTP 302 to the identity provider on `/operator/access`**. That establishes an automatic reauthentication defect in the application; the agent did not inspect the developer browser's actual cookie jar or claim to have captured its exact network trace. The previous test asserted that a post-logout redirect was success, so it missed this distinction. The reported successful Microsoft logout does not establish removal of every browser SSO session.

### Changes

- `FlyerMonkey/FlyerMonkey.Web/Services/OperatorAuthentication.cs`: Cookies is now the default challenge/forbid scheme. Unauthorized operator requests return 401 with no Location header; forbidden requests return 403. Only the explicit `/operator/signin` endpoint challenges OpenIdConnect. All API token requests still explicitly select OpenIdConnect. Interaction-required token errors return 401 rather than navigating into login.
- The same file makes `/operator/signout` a local-only antiforgery-protected POST. It deletes the cookie and revokes its server-side ticket before returning a local redirect to `/operator/signed-out`. Provider discovery/sign-out is absent from this response. The signed-out page offers an optional second POST to `/operator/signout/microsoft`, also antiforgery protected, which uses the existing OIDC logout/callback settings. It does not falsely show an active session as signed out.
- `FlyerMonkey/FlyerMonkey.Web/Services/OperatorTicketStore.cs`: in-memory expiring ITicketStore for the existing single-process pilot. Logout invalidates the server ticket, so replaying the old protected cookie is denied even if a browser retains it. Process restart invalidates existing sessions. This requires no SQL, Azure or credential changes. A future multi-instance deployment needs a separately reviewed shared ticket/cache/Data Protection design; this is not a production rollout.
- `tests/WebAuthentication.Checks/Program.cs`: verifies cookie expiration, server-side old-cookie replay denial, protected 401/403 without redirects, no token acquisition after logout, both antiforgery boundaries, skipped/cancelled/completed/failed provider logout, explicit new sign-in, and real Web status-code-page middleware behavior.
- This guide records the corrected behavior and manual procedure. No other source/configuration files changed for this fix. Existing operator allowlist, ViewOwn-only cap, public catalogue routing and Production generation hard block are unchanged.

The local server session is revoked; existing Entra/API bearer tokens are not globally revoked by this change. The API still independently validates tokens and operator permissions. The in-memory MSAL token cache cannot be reached through the now-revoked HTTP session; no browser token is exposed or stored.

### Validation

| Check | Result |
| --- | --- |
| WebAuthentication.Checks | 56 passed; static offline OIDC metadata, ephemeral cookies and fake token acquisition, no real provider/API calls. |
| Authentication.Checks | 47 passed; anonymous catalogue, operator allowlist, ViewOwn cap, ownership and Production generation block preserved. |
| CraterIt.Checks | 118 passed; fake generator only. |
| ProductSecurity.Checks | 47 passed; public reads and removed product writes/container confinement preserved. |
| Web/API builds | Passed with cached NU1900 vulnerability-source warnings; no compile errors. |

A running developer Web process locked its normal apphost output. It was left running; subsequent Web/API validation used ignored `DATA/LogoutValidation` artifacts and `UseAppHost=false`, without stopping another process or touching its configuration. The initial normal build failure was a file-lock issue, not a compilation failure. A current package vulnerability audit remains unverified because the NuGet source was unavailable.

Reproduction commands (initial isolated restore/build omits `--no-restore`):

```powershell
dotnet run --project tests/WebAuthentication.Checks --artifacts-path DATA/LogoutValidation --no-restore --no-launch-profile -p:UseAppHost=false
dotnet run --project tests/Authentication.Checks --artifacts-path DATA/LogoutValidation --no-restore --no-launch-profile -p:UseAppHost=false
dotnet run --project tests/CraterIt.Checks --no-restore --no-launch-profile -p:UseAppHost=false
dotnet run --project tests/ProductSecurity.Checks --no-restore --no-launch-profile
dotnet build FlyerMonkey/FlyerMonkey.Web/FlyerMonkey.Web.csproj --artifacts-path DATA/LogoutValidation --no-restore -p:UseAppHost=false -v minimal
dotnet build FlyerMonkey.Api/FlyerMonkey.Api.csproj --artifacts-path DATA/LogoutValidation --no-restore -p:UseAppHost=false -v minimal
```

### Exact manual browser retest

1. Stop **your** existing local Web process in its terminal/IDE, then rebuild/restart it using the enabled `operator-local` command above. Keep the existing local API running, or start it with its existing enabled profile. Do not modify User Secrets or portal settings. The new ticket store rejects old cookies, so fresh explicit sign-in is expected after restart.
2. In a browser with Developer Tools Network open, visit `https://localhost:7094/operator/signin` explicitly. Complete Entra sign-in, then check `/operator/access` reports the expected matching identity and ViewOwn=true only. Do not record/export cookies, tokens, authorization codes or a full network archive.
3. Click **Sign out** on `/operator`. Its POST should redirect locally to `https://localhost:7094/operator/signed-out`, not to Microsoft. Inspect status/header names only: the local cookie is expired in this response, and the server ticket is revoked. Local sign-out is already complete here.
4. Before clicking the Microsoft button, request `/operator/access` freshly, including from another already-open tab. Expect **401**, no Location redirect and no authorization/login request to Microsoft. Repeat for `/operator` and `/operator/artwork?productId=102`. Do not judge an old displayed JSON page or a browser Back snapshot as a new authenticated response; confirm the actual Network request status.
5. Return to the signed-out page and choose **Also sign out of Microsoft**. Cancel/close the provider flow, then freshly request `/operator/access`: still 401. If the Microsoft page is unreachable, local access must remain denied as well.
6. Repeat an explicit sign-in/local sign-out, then complete the optional Microsoft logout. The existing callback may return to `/`; fresh `/operator/access` remains 401 without automatically logging back in.
7. Verify `/products` and the public local `/api/products` remain anonymously accessible, subject to existing connectivity. Only explicitly clicking **Sign in explicitly** or visiting `/operator/signin` should restore an authenticated local session. Existing Microsoft SSO may make that deliberately requested login quick; that is allowed.
8. Do not send generation requests. The Production generation block is verified offline, not by paid or Production requests. Leave the existing allowlist, grants, credentials and Azure settings unchanged.

The developer subsequently confirmed the real-browser retest passed, including local and optional Microsoft logout and protected access showing the explicit sign-in message. Offline tests establish the local session lifecycle independently; no real credentials or tokens are included in the checkpoint.

Sources: [Microsoft cookie authentication and SignOutAsync](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0), [Cookie session-store removal on sign-out](https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs), [OIDC and local session configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication).

## Git checkpoint preparation

The developer confirmed the real-browser authentication/logout checks passed. Before checkpointing, the local nonsecret identity defaults were moved into the dedicated operator-local launch profiles; both tracked Development settings are logging-only templates again. Existing User Secrets were neither inspected nor changed. Authentication remains disabled by default and Production generation remains hard-blocked.

The credential scan found a pre-existing signature-shaped string in the product image invalid-path fixture. Its one-character synthetic value, relative path, absence of SAS authorization fields, offline transport and no-storage-request assertion demonstrate that it cannot authorize Azure access. The fixture now uses the scanner's existing unmistakable `EXAMPLE_NOT_A_SECRET` marker; query/signature rejection and the no-storage-call assertion are preserved. No credential-scanner rule or exclusion was changed.

The entire d1cda55 tree was reviewed: 205 files, one specifically reviewed synthetic fixture finding and no unreviewed credential findings. Its 27 changed paths match the earlier API security, Reviewer, Android public-read and recovery-documentation checkpoint; no unintended generated/local-secret files were identified. The historical commit is retained unchanged. The pre-commit gate now also runs the API/Web authentication and product-security suites.

Excluded from this checkpoint: the existing backlog changes in `docs/ApprovedArtworkPublication.md` and the unrelated `FlyerMonkey.Api/dotnet-tools.json`. Ignored User Secrets, DATA, build outputs, logs and local credentials are not part of the checkpoint.
