# Mission 010B Stage 3 — authentication foundation

Status: local implementation only. No registrations, directory changes, production configuration, paid generation, deployment, commit or push. Authentication defaults off when its configuration is absent. Existing unrelated changes to ApprovedArtworkPublication.md and FlyerMonkey.Api/dotnet-tools.json are preserved.

## Identity and architecture

Owner-confirmed tenant: richardberriman.com workforce Entra tenant. Dedicated member: fm.operator@richardberriman.onmicrosoft.com; first sign-in/password change/Authenticator registration completed. Owner confirms no administrative roles. The operator object ID remains private to the owner. Confirmed tenant ID: `4112a2a6-70d8-47f0-a0fe-352edd57cc04`; API client ID: `52691bc6-97d4-4abc-bc11-19b3d198a288`; Web client ID: `fcadb3a5-2941-4b27-946a-785608758ff1`. API Crater.Access/v2 tokens and Web administrator consent are confirmed. Android registration is deferred. Follow [the current local Web/API integration procedure](LocalEntraSignIn.md); the broader registration worksheet below is historical planning, not authorization to create more resources.

API: ASP.NET Core net10.0, Microsoft JwtBearer, Microsoft IdentityModel signature/issuer/audience/lifetime validation. Web: Blazor Interactive Server, Microsoft.Identity.Web 4.14.2, OIDC authorization code + PKCE, server-side delegated token acquisition. Android: MAUI Blazor Hybrid, MSAL.NET 4.84.2, system-browser code + PKCE, no broker or embedded login. Shared: token/session abstractions and a separate GET-only private client. Existing catalogue clients never receive tokens.

Use three NEW registrations. Three are not a protocol requirement: an API and confidential Web application can technically share a registration. Separate API/Web/Android registrations give distinct audiences, confidential/public settings, consent and lifecycle boundaries. This is the recommended smallest clear security boundary for these three components. Do not reuse or alter the eight historical Power Platform/SharePoint registrations.

## Exact registration worksheet — instructions only, execute after approval

Replace T, A, W and M below with the tenant GUID, API application/client GUID, Web application/client GUID and Android application/client GUID. O is the dedicated operator's object GUID in T. These are identifiers, not secrets. Record application IDs separately from application-object and enterprise-application-object IDs.

For every registration: Entra admin center > Identity > Applications > App registrations > New registration. Supported account types: **Accounts in this organizational directory only (single tenant)** in richardberriman.com. No personal Microsoft account or multitenant audience. Leave implicit ID/access-token grants disabled. Set no SPA platform. No application permissions or Microsoft Graph permissions are needed; remove the default Graph User.Read request if present. OIDC openid/profile/offline_access protocol scopes are distinct from Graph API permissions.

### FlyerMonkey API

1. Name: `FlyerMonkey API`. No redirect URI; API resource only, no client credential.
2. Expose an API > Application ID URI: `api://A` (substitute the real GUID).
3. Add scope: name `Crater.Access`; who can consent: **Admins only**; admin consent display name `Access private Crater features`; description `Allow this application to call FlyerMonkey private Crater APIs on behalf of the signed-in user.`; state **Enabled**.
4. Manifest: set `api.requestedAccessTokenVersion` to `2`. Keep other manifest settings intact.
5. No app roles are needed for this one-operator increment. The four Crater permissions are server policies, not Entra directory roles or app-only permissions.
6. Expected API token: `ver=2.0`, `aud=A` (bare API client GUID), `iss=https://login.microsoftonline.com/T/v2.0`, `tid=T`, `oid=O`, `scp` includes `Crater.Access`, `azp` equals W or M. The requested scope is `api://A/Crater.Access`; that URI is not the configured v2 token audience.

### FlyerMonkey Web

1. Name: `FlyerMonkey Web`. Authentication > Add platform > **Web**. Confidential client.
2. Local redirect URIs, exactly:
   - `https://localhost:7094/signin-oidc`
   - `https://localhost:7094/signout-callback-oidc`
3. Front-channel logout URL: `https://localhost:7094/signout-oidc`.
4. API permissions > My APIs > FlyerMonkey API > Delegated permissions > `Crater.Access`; authorized administrator grants consent. No Graph or application permissions. Keep “Allow public client flows” **No**.
5. Later approved local setup requires a short-lived Web client secret in .NET user-secrets or an approved secret manager, never source/appsettings/Git/SQL. This implementation reads `Authentication:WebClientSecret`. Do not paste it into chat, commands with literal values, screenshots or documentation. Certificate support is a recommended separate production hardening step; this implementation does not load a certificate.
6. Enterprise application > Properties: `Assignment required? Yes`; Users and groups: assign only the dedicated operator to the Web application. This is an additional gate; the API allowlist remains authoritative. If the tenant cannot support the desired assignment controls, inspect the licensing/policy limitation before activation.
7. Web ID-token audience is W; Web obtains a separate API access token with audience A. API never accepts the Web ID token.

The publish profile currently names this Web origin:
`https://flyermonkey-fmaqaub3gqhhfzez.australiaeast-01.azurewebsites.net`

After confirming the actual hostname and separately approving production rollout, the proposed exact production callback values are that origin plus `/signin-oidc` and `/signout-callback-oidc`, with front-channel logout ending `/signout-oidc`. Do not register or activate production URLs/settings during this stage. Only one front-channel logout URL is configured per registration; choose the active pilot environment. Prefer separate environment registrations before simultaneous production/local testing.

### FlyerMonkey Android

1. Name: `FlyerMonkey Android`. Public client, **no secret/certificate**.
2. For the implemented MSAL.NET system-browser path: Authentication > Add platform > **Mobile and desktop applications** > custom redirect URI `msalM://auth`, replacing M with the real lowercase client GUID. This is not the Android-platform tile's package/signature-derived broker URI. Do not mix the two redirect schemes.
3. Leave the advanced “Allow public client flows” switch **No**; the configured native redirect supports authorization code with PKCE. Do not enable ROPC, device-code or implicit flows.
4. API permissions: only delegated `api://A/Crater.Access`; administrator grants consent. Enterprise application assignment: dedicated operator only where supported. Do not enable user self-sign-up.
5. Later local build setup: supply real nonsecret values to the `AndroidOperatorOptions` instance in MauiProgram; set Enabled=true, TenantId=T, ClientId=M, ApiClientId=A, RedirectScheme=`msalM`, and an explicitly trusted HTTPS API root. Build with `-p:FlyerMonkeyMsalScheme=msalM` so the manifest callback matches exactly. Default options are disabled and the default scheme is intentionally unusable for Entra login.
6. Current package ID is `com.companyname.flyermonkey`; approve the final package and Release signing arrangements before distribution. This system-browser path does not require a signing-hash redirect. Adding a broker later requires separate platform/package/signature setup and device testing.
7. Android ID-token audience is M; API access-token audience is A. Authenticator registration for MFA does not mean this app uses the Authenticator broker.

## Local configuration — owner-supplied values only

API configuration (user-secrets or process environment, not committed settings):

```text
Authentication:Enabled = true
Authentication:TenantId = T
Authentication:ApiClientId = A
Authentication:OperatorObjectId = O
Authentication:AllowedClientIds:0 = W
Authentication:AllowedClientIds:1 = M
Authentication:OperatorPermissions:0 = Crater.ViewOwn
CraterIt:PrivateAccessEnabled = true
CraterIt:Enabled = false
```

Use `__` instead of `:` for environment variables, including array indexes. Existing SQL startup configuration is still required; do not print or copy its value. Keep the existing public approved-artwork flags unchanged. No credentials are newly required for public catalogue access.

Web configuration: Authentication:Enabled=true; TenantId=T; WebClientId=W; ApiClientId=A; OperatorObjectId=O; WebClientSecret from the external secret store; ApiBaseUrl set to the intended HTTPS API root. The Web HTTPS launch profile uses port 7094; bind local identity testing to localhost, not the existing profile's optional all-interface HTTP listener. Cookies require HTTPS. Do not authenticate through the HTTP-only launch profile.

The IDs in tests/Authentication.Checks are local fixtures and must never be copied into runtime configuration.

## Authorization and ownership

Every operator policy requires a validated authenticated v2 principal, exact tenant/object IDs, delegated Crater.Access scope, an approved client ID, and an explicit configuration grant. Email has no authorization role. Unknown users and missing/empty grants are denied. Removing the operator/grant affects subsequent API requests through the configuration provider; use a controlled restart when the provider does not reload.

| Permission | Meaning |
| --- | --- |
| Crater.ViewOwn | List/read only metadata/JPEG belonging to caller `(tid, oid)`. |
| Crater.Generate | Permission check plus the existing Development/loopback/header/enabled guards. No Production generation. |
| Crater.Approve | Reserved explicit policy; no publication-write API introduced. Existing manifest approval remains deployment controlled. |
| Crater.Admin | Reserved explicit policy; no account/grant-management API or ownership bypass introduced. |

No permission implies another. Admin cannot bypass generation restrictions. The recommended initial real grant is ViewOwn only. Tests temporarily grant all policies strictly to prove independence and denial behavior.

Public product/offer/image/status and approved-artwork controllers remain explicitly anonymous. API fallback policy denies unannotated routes. Missing bearer token returns 401; authenticated wrong user/client/scope/grant returns 403. Disabled private feature and inaccessible artwork return 404. A removed route may receive the fallback authentication challenge before its 405 response; authenticated product POST remains 405 and has no action to invoke.

With authentication enabled, the local header cannot authorize a user. Existing explicitly opted-in local development workflow remains available only with authentication disabled. Development fixture import is disabled when authentication is enabled, even for Admin.

New generation metadata carries nullable OwnerTenantId/OwnerObjectId stamped from validated claims. Existing schema-1 manifests deserialize without owners and are hidden from authenticated private reads. Owned image metadata is checked before bytes are read. Cross-owner or ownerless replay returns conflict before provider resolution. Request IDs remain globally reserved; do not reuse another owner's request ID. No legacy owner assignment/migration occurs. Approved public artwork continues through the independent manifest/hash reader.

## Kill switch, secrets and lifecycle

Production generation remains hard-denied by the existing `CraterItLocalAccess`: Development AND enabled AND loopback caller/Host AND local header are all required, in addition to the new policy. No production-enabling code path or setting is added. The existing generation/gallery controls are not rewired to authenticated cloud generation. No paid key or provider is needed for this stage or its smoke test.

API validates HTTPS metadata, RS256 signatures against issuer signing keys, issuer, API audience, expiry/not-before (30-second skew), tenant, version, scope, actor and operator. Signing-key rollover is handled by the standard metadata manager. Never accept a decoded-but-unvalidated JWT or ID token as authorization. Unconfigured authentication rejects bearer tokens without metadata requests.

Web tokens remain server-side. Secure/HttpOnly cookie expires after one hour without sliding renewal; OIDC state/nonce and PKCE are library managed. Sign-out is POST plus antiforgery validation, cookie/OIDC sign-out, using Microsoft.Identity.Web lifecycle hooks. In-memory token cache is deliberately a local/single-instance pilot choice; process restart can require fresh login. Production needs a reviewed token cache/Data Protection/session deployment design outside FlyerMonkey SQL, and certificate/secret rotation planning. No tokens are returned to browser JavaScript/local storage.

MSAL owns the mobile cache and silent refresh. Interactive login occurs only through the explicit sign-in button; a background API request cannot open login UI. Sign-out removes cached app accounts and clears displayed private metadata; the system-browser SSO session may remain. Android backups are disabled. Verify MSAL cache-at-rest behavior, reinstall/restore and Release trimming on the real device before distribution. Never copy tokens into preferences, logs, SQLite, WebView storage or ordinary files.

Logout does not revoke every already-issued access token. API allowlist/grants and feature gates remain the immediate application denial boundary. Lost-device response requires owner-controlled account/session revocation and application access removal; do not invent a fixed Entra token lifetime.

## First real sign-in smoke test — not yet executed

1. Obtain approval to create the three registrations and configure **local/isolated** identity testing. Confirm T/O privately; inspect the dedicated account's current effective roles/groups/IAM and MFA policy. Keep historical registrations untouched.
2. Apply the worksheet through an existing authorized administrator, record only nonsecret identifiers/consent outcomes, assign only the operator. Configure local API/Web IDs and external Web secret. Do not change Production settings or deploy this branch.
3. Start the API locally with authentication enabled, PrivateAccessEnabled=true, only ViewOwn granted, CraterIt:Enabled=false and the existing Local storage provider. Use a trusted local HTTPS listener for API/Web; note the actual API HTTPS port in ApiBaseUrl. Public catalogue still needs its existing database connectivity; no database mutation is part of this test.
4. Before login, GET catalogue and known canonical/approved public images. Expect existing public behavior, with no challenge. GET private list without a token: 401. Do not send any generation POST.
5. Visit `https://localhost:7094/operator/signin`; sign in as the dedicated operator and complete MFA when required. Expect `/operator` with no token visible. An unassigned/wrong account must fail assignment or operator validation. Record actual outcome, not just a redirect.
6. Use the product-ID form on `/operator` to call the API with the server-held token. Expect 200 with only owned records; an empty array is expected for a new operator. Existing ownerless artwork must not appear. Verify forbidden reads separately using a private HTTP client without logging/exporting tokens. Do not assign existing artwork merely to make this test nonempty.
7. Sign out using the form; `/operator/artwork?productId=1` should require login again. Repeat in a fresh browser profile; verify cookie expiry/restart behavior. An existing SSO session can shorten subsequent sign-in; this is not proof of token revocation.
8. For Android, first choose an approved isolated HTTPS API reachable from the device and trusted by it. A device cannot reach host localhost through an ordinary cloud URL. Do not disable TLS certificate validation, enable cleartext traffic or point the new build at an unchanged Production API and expect authentication support.
9. Configure Android options and matching manifest scheme, build Release, install only on the approved pilot device. Open Operator in the navigation menu, sign in through the system browser, complete callback, then View my artwork. Confirm catalogue remains usable while signed out. Check wrong-user denial, silent renewal after expiry, restart, sign-out, uninstall/reinstall and backup behavior. No generation action exists on this operator page.
10. Record real tenant/client IDs, app versions, time, HTTP outcomes and redacted diagnostics. Never record password, client secret, authorization code, bearer/refresh token or full callback URL. Production remains unchanged and generation disabled throughout.

## Blockers and operator decisions

- Approve registrations and supply T/A/W/M/O; no actual Entra login/consent/callback/MFA integration has been tested.
- Approve initial grants (recommend ViewOwn only), Web secret handling for local testing, final Android package/signing and the isolated HTTPS device-test target.
- Production private galleries are **not ready for deployment**: the existing Azure private store is Development-only, and Local requires repository layout. A future approved runtime-storage design is required; this stage does not weaken those guards or silently switch storage providers.
- Approve a separate legacy ownership policy before any migration. No automatic email-based matching or ownerless access.
- Complete production Web cache/Data Protection/credential planning, device token-cache/Release tests, current dependency audit and later deployment review.

## Future External ID

Keep the operator in the workforce tenant. Consumer identities can later use a separate Entra External ID external tenant with Google/Facebook/email sign-in through its configured providers and user flows. No provider is configured here. Keep immutable identity/ownership scoped by verified issuer/tenant and object identifier; customer migration needs an explicit identity-linking design, never matching solely on email. Current API deliberately rejects all other tenants, including future customer tokens, until separately reviewed consumer authorization is implemented. Consumer membership must never imply Generate/Approve/Admin privileges.

## Validation

All JWTs used by Authentication.Checks are signed with ephemeral local RSA keys; identity discovery is replaced with static configuration and a network-rejecting backchannel. Loopback HTTP, fake repositories/storage and fake Blob transports are used. These are offline authorization tests, **not successful real Entra sign-ins**. WebAuthentication.Checks uses static offline OIDC metadata and test-only cookie minting; the production application has no cookie-minting endpoint. Its key protection is ephemeral and host key-store warm-up is removed.

| Verification | Final result |
| --- | --- |
| Authentication.Checks | 39 passed: real bearer middleware/routing, issuer/audience/signature/expiry, operator/client/scope/version, four independent grants, ownership, public access, fallback denial, replay isolation and Production generation denial before provider construction. |
| WebAuthentication.Checks | 11 passed: offline code/PKCE challenge, callback, anonymous denial, secure cookie, no-store, antiforgery logout, cookie removal and fixed cookie expiry. |
| CraterIt.Checks | 118 passed, including server-stamped owner fields surviving actual Local storage and ownerless replay/read denial, using its fake generation class. |
| ProductSecurity.Checks | 47 passed; fake SQL/offline Blob transport. |
| ApprovedArtwork.Checks | 118 passed; anonymous publication validation, public clients and existing UI behavior. |
| CraterArtwork.AzureChecks | 54 passed; in-memory Blob SDK transport, no Azure traffic. |
| API and Web builds | Passed. Two pre-existing unused Offers.razor fields can warn when Shared recompiles. |
| Android Debug / Release | Both built successfully. No device installation or real callback tested. Release merged manifest confirms allowBackup=false, usesCleartextTraffic=false and the disabled default callback scheme. |
| git diff --check | Passed; only repository LF/CRLF conversion notices. |

Commands run from repository root:

```powershell
dotnet run --project tests/Authentication.Checks --no-restore --no-launch-profile
dotnet run --project tests/WebAuthentication.Checks --no-restore --no-launch-profile
dotnet run --project tests/CraterIt.Checks --no-restore --no-launch-profile -p:UseAppHost=false
dotnet run --project tests/ProductSecurity.Checks --no-restore --no-launch-profile
dotnet run --project tests/ApprovedArtwork.Checks --no-restore --no-launch-profile
dotnet run --project tests/CraterArtwork.AzureChecks --no-restore --no-launch-profile
dotnet build FlyerMonkey.Api/FlyerMonkey.Api.csproj --no-restore -v minimal
dotnet build FlyerMonkey/FlyerMonkey.Web/FlyerMonkey.Web.csproj --no-restore -v minimal
dotnet build FlyerMonkey/FlyerMonkey/FlyerMonkey.csproj -f net10.0-android -p:TargetFrameworks=net10.0-android --no-restore -v minimal
dotnet build FlyerMonkey/FlyerMonkey/FlyerMonkey.csproj -c Release -f net10.0-android -p:TargetFrameworks=net10.0-android --no-restore -m:1 -v minimal
git diff --check
```

Restored the affected projects before checks. The first approved-artwork run encountered stale transitive IdentityModel asset-version warnings; restoring that test/importer graph resolved them and its rerun passed. An initial Android Release attempt was blocked by sandbox writes of generated Java-object cache files alongside NuGet assemblies; it passed with the required filesystem access and no source workaround. ProductSecurity.Checks reported cached NU1900 vulnerability-source unavailability: a complete current dependency vulnerability audit is not established.

Sources:
- [Microsoft protected API registration](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-protected-web-api-app-registration)
- [Access-token claims and v2 audience](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference)
- [Microsoft.Identity.Web setup](https://learn.microsoft.com/en-us/entra/msidweb/getting-started/quickstart-webapp)
- [MAUI MSAL and callback setup](https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/authentication?view=net-maui-10.0)
- [Token caching](https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization)
- [External ID tenant separation](https://learn.microsoft.com/en-us/entra/external-id/external-identities-overview)
