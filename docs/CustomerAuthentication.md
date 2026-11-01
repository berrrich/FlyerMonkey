# Mission 011A: customer authentication foundation

## Final customer verification — November 1, 2026

Owner-reported live results: customer sign-in succeeded, the status endpoint returned Active, generation remained disabled, and sign-out followed by sign-in completed without re-entering credentials.

Agent read-only SQL verification against the configured API database returned: **1 customer, 1 Active, 0 Disabled, 0 CraterEnabled, 0 duplicate tenant/object identity groups**. The named external-identity unique constraint exists. Only aggregate results were displayed; no customer identifiers, PII or connection credentials were emitted. The queries used SELECT only. The initial sandbox connection failed; the aggregate query succeeded outside the network sandbox.

There is no duplicate customer in the current database. Repository provisioning looks up the same tenant/object identity and returns its existing CustomerId without updating or replacing it. These findings support reuse on repeat login. An independently captured pre-logout CustomerId was not available, so a direct before/after CustomerId comparison remains a test gap; a current snapshot alone cannot establish historical identity continuity.

Local logout is an authenticated, antiforgery-protected POST to `/customer/signout`. It signs out only `CustomerCookie`, removes its server-side ticket and redirects to `/customer/signed-out`. It does not invoke `CustomerOidc` sign-out or the Entra end-session endpoint. The signed-out page explicitly says the identity-provider session may remain active. Re-entry without credentials is consistent with that surviving Entra SSO session; it is not evidence that the local customer ticket survived logout. Operator logout is separate. No authentication behavior was changed during this verification.

Both operator-local launch profiles retain `CraterIt__Enabled=false` and `CraterIt__LocalDevelopmentGenerationEnabled=false`. The customer API also always returns CraterEnabled=false, regardless of the stored eligibility flag. No generation call was made.

Remaining live gaps: capture and privately compare CustomerId before/after logout; confirm `/customer/me` rejects the logged-out browser before re-entry; verify operator/customer isolation with both cookies present; exercise disabled-account rejection only under separate write authorization; test concurrent first provisioning against SQL, expiry/replay in the live browser, and provider/storage failure paths. Provider-wide logout is not implemented or tested. Android remains outstanding. Earlier offline results below are historical and were not rerun for this read-only verification. `git diff --check` passed.

Staging recommendations: stage the exact Mission 011A implementation/test files listed at the end of this document and this updated document. Also include the reviewed API/Web `Properties/launchSettings.json` changes if the intended checkpoint includes local customer opt-in: those diffs add only customer enablement/API-root settings and retain disabled generation flags. Exclude `docs/ApprovedArtworkPublication.md`, `.vscode/settings.json`, `FlyerMonkey.Api/dotnet-tools.json`, all User Secrets, generated build artifacts and DATA. Inspect individual paths and the complete staged diff rather than using `git add .`; verify no PII/secrets and run the listed checks before a later commit. No files were staged, committed, pushed or deployed by this verification.

The historical setup and verification notes below describe the earlier implementation checkpoint. Their statements that the schema was not applied and live sign-in was unverified are superseded by the evidence above; schema application itself was not performed or observed by the agent.

Implemented for Web/API, opt-in and disabled when `CustomerAuthentication:Enabled` is absent. Customer generation is always disabled, including when the database's future eligibility flag is true. Existing operator registrations, schemes, permissions, local-generation flags and production hard-blocks are unchanged. Google federation, branding, phone/SMS and notification preferences remain backlog.

## Tenant and protocol

Tenant: FlyerMonkey Customers (`bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab`), domain `flyermonkeycustomers.onmicrosoft.com`, user flow `FlyerMonkeySignUpSignIn`.

The following public discovery document was fetched on November 1, 2026:

`https://flyermonkeycustomers.ciamlogin.com/bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab/v2.0/.well-known/openid-configuration`

Its issuer is **`https://bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab.ciamlogin.com/bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab/v2.0`**. Authorization, token and JWKS URLs instead use the `flyermonkeycustomers.ciamlogin.com` hostname. This difference is intentional. The code pins the discovered issuer. No B2C policy URL or `p=` parameter is used.

Web registration: `75ed8832-9bd7-45f3-8701-499ad59cdcb7`. API registration: `c47581be-9d3d-4ed3-b0ce-7518fb3094c6`. Scope: `api://c47581be-9d3d-4ed3-b0ce-7518fb3094c6/Customer.Access`. The API accepts only its two explicitly registered resource identifiers (GUID or `api://` GUID); v2 access tokens normally use the GUID audience. Web ID tokens require the Web client GUID audience and are never accepted as API access tokens.

The standard OIDC middleware performs authorization-code, PKCE, state/correlation, nonce, signature, issuer, audience and lifetime validation. Signing keys refresh through HTTPS discovery. No provider discovery runs just to browse the catalogue.

## Isolation and storage

- Web schemes: `CustomerOidc` and `CustomerCookie`; separate `__Host-FlyerMonkey.Customer` cookie. Existing operator defaults are retained. With operator auth disabled, a separate anonymous default scheme prevents the customer cookie becoming an operator principal.
- API scheme: `CustomerBearer`. Customer endpoints deliberately bypass the operator fallback and explicitly authenticate/authorize this scheme before resolving account storage. They validate tenant, nonempty object ID, v2 token version, Web client `azp`, and exact `Customer.Access` scope. Disabled customer deployments return 404.
- No email-based linking. `dbo.Customers` maps tenant + object ID to one internal CustomerId. Concurrent first logins use a unique constraint and serializable range locking. Repeat login never reactivates disabled accounts.
- The SQL script is review-only: `database/migrations/011A_001_Customers.sql`. **It has not been applied.** It creates only Customers, with the requested identifiers, status, default-false eligibility and UTC dates. It does not change catalogue or operator tables. Review/select the intended development database and apply manually only after explicit approval. Runtime needs SELECT/INSERT; disable accounts with a separate administrative identity. No startup migration exists.
- GET `/api/customers/me` reads an existing profile; bearer-authenticated POST `/api/customers/session` provisions one. Both reject disabled accounts. A schema/storage failure returns a safe 503. Web GET `/customer/me` proxies the API status; a 401/403 revokes the local customer ticket.
- One-hour absolute, Secure, HttpOnly, SameSite=Lax customer cookie. Customer tickets reuse the existing in-memory ticket-store implementation in a separate instance. Tokens remain in Microsoft's server-side memory cache, not browser local storage or cookies. Logout revokes the local ticket, including old-cookie replay; it does not sign out of the provider or revoke every device's access token.
- Account disable is enforced at every protected API operation. UI signed-in state is presentation only and can remain visible until the next status check. No customer generation endpoint exists.

## Required local configuration

No real credential was added or read into output. The Web secret is accepted **only** from Web User Secrets (`CustomerAuthentication:ClientSecret`) or the environment variable `CustomerAuthentication__ClientSecret`. Appsettings and command-line secret values are deliberately not credential sources.

In Visual Studio, right-click **FlyerMonkey.Web → Manage User Secrets**. Add the customer Web registration credential privately under the key above. Do not put its value in chat, source, documentation, launch profiles or logs. The API needs no customer client secret. Google credentials are not part of this mission.

Add these nonsecret settings to **Web User Secrets**:

```json
"CustomerAuthentication:Enabled": true,
"CustomerAuthentication:ApiBaseUrl": "https://localhost:7255/"
```

Add `"CustomerAuthentication:Enabled": true` to **API User Secrets**. Existing operator credentials and SQL connection configuration are still required. Missing flags mean disabled. Enabling Web customer auth without its credential or a valid HTTPS API root fails startup clearly, without discovery. Development confines customer bearer forwarding to HTTPS loopback, disables redirects and has a 15-second timeout.

Use the existing **operator-local** HTTPS profiles (Web 7094/API 7255), so customer and operator sessions can be tested independently. Leave `CraterIt__Enabled=false` and `CraterIt__LocalDevelopmentGenerationEnabled=false`. No launch profiles or production settings were changed.

## Portal verification — manual only

The user supplied the tenant, consent and localhost callback configuration; no portal resources or settings were changed by the agent.

1. In the **customer** tenant, open App registrations → Web registration → Authentication. Confirm the Web-platform redirect URI is exactly `https://localhost:7094/signin-customer-oidc`. Keep the existing operator registration untouched.
2. External Identities → User flows → `FlyerMonkeySignUpSignIn` → Applications: confirm the customer Web registration is associated. The flow controls the available sign-in methods. No Google button is promised before federation is configured.
3. API registration → Expose an API: confirm the resource identifier and delegated `Customer.Access` scope. Web registration → API permissions: confirm that delegated scope and the supplied admin consent.
4. Web registration → Certificates & secrets: use its existing valid credential in Web User Secrets. If absent/expired, stop for the owner to create/rotate it; do not create it automatically.
5. If any redirect, flow association or permission differs, stop and report the exact mismatch. Do not work around validation or change operator configuration.

## Manual localhost tests

1. Stop F5. Review the SQL script and obtain explicit approval before manually applying it to the development database. Configure the nonsecret flags and Web credential privately, then rebuild/restart Web/API.
2. In a private browser window, browse offers and `/products/139` anonymously. There should be no customer challenge or customer-profile request. Click Crater It: its nonmodal, keyboard-accessible right-side sign-in panel opens. It becomes full-width on mobile; Close returns to the product.
3. Select **Sign in or create an account**. The customer flow starts. Complete first sign-in using a method already enabled in the flow. Callback → `/customer/complete` obtains a customer API token server-side, provisions through the API, and returns to `/products/139?crater=true`. Confirm customer signed-in status, generation disabled and no image call.
4. Visit `/customer/me` to verify CustomerId, Active status and CraterEnabled=false. Sign out from `/customer/account`, sign in again and confirm the same CustomerId. Logout is an antiforgery-protected POST and must not remove an operator cookie.
5. Confirm `/customer/signin?returnUrl=https://example.invalid/` returns to `/` after successful login; `/products/102` is preserved. Arbitrary paths, encoded controls and nested redirect parameters are rejected. No prompt is stored in a URL.
6. Explicitly sign in at `/operator/signin` and verify its existing access page and private-artwork behavior. A customer-only browser must not authenticate to `/operator` or operator API routes. An operator-only browser must not authenticate to customer routes. Check both cookies together as well.
7. With a separate administrative database identity, disable the test customer and update UpdatedUtc; use the already-reviewed administrative procedure. `/customer/me` must return 403 and revoke its Web ticket. Repeat sign-in must not reactivate the account. Do not perform this account mutation without authorization.
8. Check logout replay/expiry through offline tests. A running circuit may show stale signed-in text; no protected operation trusts that text. Sign-in/out links force a full reload.
9. If storage is unavailable, the account page explains setup and no profile privileges are granted. If provider validation fails, `/customer/error` shows generic guidance, never raw token/provider details.
10. Disable the customer flags and restart after testing if the integration should remain off. Never enable generation as part of this test.

Live first/repeat login, callback, actual API token claims, account persistence and provider-side session behavior remain **unverified** until the owner supplies the local credential and approves/applies the development schema. No authenticated identity-provider call or paid image call was made by the agent.

## Offline checks and performance

Final validation: Web/API builds passed; Web's shared build reports the two pre-existing unused Offers fields. All **321** offline checks passed: CustomerAuthentication 58, WebAuthentication 76, Authentication 53, CraterIt 119, ImageGeneration 15. Test-only service-provider analyzer warnings remain. `git diff --check` passed; Git reports existing LF-to-CRLF normalization notices.

Real Web-process smoke checks also passed: anonymous `/not-found` rendered the application layout with customer auth disabled, and with customer auth enabled using an offline dummy credential. The latter displayed the sign-in entry without contacting the API/provider and returned 401 for anonymous `/customer/account`. These smoke checks are not successful live sign-in tests. The first sandboxed smoke attempt could not use Windows DPAPI/EventLog; repeating outside that sandbox passed without application changes. Temporary Web processes were stopped.

Run each project with `dotnet run --project tests/PROJECT --no-launch-profile --artifacts-path .artifacts/mission011a -p:NuGetAudit=false`:

- `CustomerAuthentication.Checks`: real cookie/OIDC challenge and invalid-state processing, signed fake API JWTs, fake customer store and server-side token acquisition/transport. No successful external code exchange is represented as tested.
- `WebAuthentication.Checks`, `Authentication.Checks`, `CraterIt.Checks`, `ImageGeneration.Checks`: existing operator/catalogue/generation regression coverage.

Startup and ordinary navigation perform only local cookie/session work for customer presentation; no `/me` or database request is made by components. Customer presentation is cached per scoped adapter and its two booleans are persisted from prerender to the interactive server circuit using .NET's persistent-service support; interactive navigation does not depend on an available HttpContext. This state never authorizes an operation. Explicit login/logout reloads the page. Account/profile and token calls occur only when customer endpoints are used. Provider/SQL outages do not block anonymous catalogue startup; existing public catalogue API dependencies are unchanged.

Memory ticket/token caches are appropriate only for this single-process local prototype: restart signs users out, and multi-instance deployment requires persistent protected Data Protection keys plus shared ticket/token caches. Do not deploy this prototype as an autoscaled customer service unchanged. Bound SQL provisioning to explicit customer operations; retry/circuit-breaker optimization can follow measured needs. Database concurrency behavior was reviewed but not tested against a real SQL database.

Android remains a required subsequent delivery target, not a completed feature here. Shared session/profile models and internal CustomerId are platform-neutral. Android needs a separate public-client registration, package/signing identity and system-browser MSAL callback; then explicitly allow its client ID at the customer API. Do not embed a secret, reuse the Web confidential-client registration, or reinterpret operator IDs as customers. Overall Web+Android authentication is not complete until both platforms pass live tests. Full offline sync and customer generation remain out of scope.

## Exact changed files

```text
FlyerMonkey.Api/Program.cs
FlyerMonkey.Api/Controllers/CustomersController.cs
FlyerMonkey.Api/Services/CustomerAuthentication.cs
FlyerMonkey/FlyerMonkey.Web/Program.cs
FlyerMonkey/FlyerMonkey.Web/Services/CustomerAuthentication.cs
FlyerMonkey/FlyerMonkey.Web/Services/WebsiteCraterSession.cs
FlyerMonkey/FlyerMonkey.Shared/Components/CustomerAccountEntry.razor
FlyerMonkey/FlyerMonkey.Shared/Components/CustomerSignInDrawer.razor
FlyerMonkey/FlyerMonkey.Shared/Components/CustomerSignInDrawer.razor.css
FlyerMonkey/FlyerMonkey.Shared/Components/ProductCraterEntry.razor
FlyerMonkey/FlyerMonkey.Shared/Layout/MainLayout.razor
FlyerMonkey/FlyerMonkey.Shared/Model/CustomerAccount.cs
FlyerMonkey/FlyerMonkey.Shared/Services/CustomerIdentity.cs
FlyerMonkey/FlyerMonkey.Shared/Services/ICustomerSession.cs
FlyerMonkey/FlyerMonkey.Shared/Services/IWebsiteCraterSession.cs
SQLServerConnection/Data/CustomerRepository.cs
database/migrations/011A_001_Customers.sql
docs/CustomerAuthentication.md
tests/CustomerAuthentication.Checks/CustomerAuthentication.Checks.csproj
tests/CustomerAuthentication.Checks/Program.cs
```

Unrelated existing changes in `docs/ApprovedArtworkPublication.md`, `.vscode/settings.json` and `FlyerMonkey.Api/dotnet-tools.json` were left untouched. Nothing was staged, committed, pushed, merged or deployed. No production configuration, real credential, saved artwork or operator registration was changed.
