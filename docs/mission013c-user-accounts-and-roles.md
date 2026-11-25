# Mission 013C — user accounts and roles

Prepared locally; no Azure SQL connection or writes, deployment, paid generation, commit or push.

## Mission 013C review - 17 November 2026

**PASS: offline review and checks. Live application/deployment remains on hold.**
013B is deployed and verified in Azure SQL per operator confirmation; do not rerun it.
Removed the extra Operator assignment and role creation. The exact assignments are
Richard Gmail: Customer, Crater Tester, Developer, Administrator; berrimar999:
Reader only; fm.operator: Administrator only. Both migration files keep `@Apply=0`.

Current validation: 89 CustomerCrater checks and 16 disposable LocalDB SQL checks
passed. Tests cover Reader denial before reservation/provider access, existing owner
preview access (including unavailable role storage), repeat application, excess role
removal, disabled User refusal, unchanged Customer values and fixture artwork bytes/
ownership. The SSMS verification script was executed before/after local assignment.
LocalDB startup required execution outside the sandbox. No Azure SQL connection,
Azure writes, API deployment, paid calls, commit or push occurred. Pre-existing
application changes were reviewed and left unchanged. Live API behavior and live
identity pairs were not revalidated in this offline mission.

Next SSMS step: explicitly select the intended server/database and run
[013C verification](../database/migrations/013C_002_Verification.readonly.sql)
with `@PostMigration=0`. Save the returned Customer baseline and retain that same
SSMS connection. Review the hardcoded tenant/object identities against trusted
identity evidence. Stop before applying 013C; keep its `@Apply=0` until separately
authorized. After a later authorized application, rerun verification in the same
connection with `@PostMigration=1`; it checks exact role sets, Active Users, exact
Customer links and unchanged Customer values. Concurrent Customer changes require
investigation rather than accepting a changed baseline. SQL cannot verify artwork
file/blob readability; live endpoint checks require a separately authorized stage.

## Schema and assignments

Apply order for a future separately authorized database change is the
[013B schema proposal](../database/migrations/013B_001_UserRoles.review.sql), then
[013C assignments](../database/migrations/013C_001_AccountAssignments.review.sql).
Both refuse execution with `@Apply=0`. No migration runner was added.

013B provides Users, Roles, UserRoles and nullable Customers.UserId with a unique
filtered index and non-cascading foreign key. Users are unique by external tenant
and object ID. Email is profile information and never an authentication/link key.
013C uses the existing 013B roles and prepares these exact assignments:

| Account | Roles | Customer link |
| --- | --- | --- |
| richard.berriman@gmail.com | Developer, Administrator, Crater Tester, Customer | Existing exact tenant/object identity |
| berrimar999@gmail.com | Reader | Existing exact tenant/object identity |
| fm.operator@richardberriman.onmicrosoft.com | Administrator | User only |

Identity pairs come from 013B's recorded 013A verification. They have not been
revalidated against Azure in this offline mission; revalidate before application.
The script requires exactly one existing Customer for each Gmail identity, refuses
conflicting links and disabled existing Users, inserts missing Users, links only
unlinked Customers and sets exact role memberships only for these three identities.
Its local assignment table uses email labels to associate requested roles with the
hardcoded identity pairs; no persisted account is selected by email.
Existing User profile details are preserved on rerun. Unexpected pre-existing
Customer/User relationships fail through explicit guards or unique constraints and
roll back the transaction. The script never reactivates accounts.

CustomerIds, external identities, Customer status, CraterEnabled and Customer
timestamps remain unchanged. No artwork rows, blobs, files or ownership keys are
modified. Users may exist without Customers, and other Customers may remain unlinked.

## Generation authorization

CustomerCraterController resolves generation access from SQL on each attempt GET
and POST using the authenticated tenant/object pair. The repository joins Customer
to User by UserId **and both external identity columns**, then reads memberships.
An Active Customer with CraterEnabled=true also needs a linked Active User with
both Customer and Crater Tester roles. All existing HTTPS, local Development,
feature gates and CustomerId allowlist checks remain required.

Reader alone, either required role alone, and Administrator alone grant no customer
generation. There is no role hierarchy. Missing or mismatched links deny; role
lookup errors return 503 before reservation/provider resolution. Implementations
of ICustomerRepository without role lookup default to denial. POST reads roles
again rather than trusting a previous GET or token role claims. A role change after
that POST authorization has completed does not cancel an already authorized attempt.

Existing private image reads retain their original owner checks and do not depend
on User/role availability. The operator endpoint retains its separate explicit
tenant/object/client/scope and permission policy; SQL Administrator labels
do not confer operator API permissions. No automatic User provisioning, account
administration interface or role claims were added.

Until both SQL proposals are applied, an enabled customer attempt route will fail
closed with 503 if the role schema is absent. Apply the reviewed schema/assignments
before enabling that route. Customer profile and existing image queries retain
their previous schema requirements.

## Earlier offline validation (prior preparation)

- Initial read-only Git status found only the unrelated untracked
  `docs/BattleOfCabbageRidge.md`; it was left untouched. No Git mutations were run.
- Initial Web build passed with no warnings/errors. SDK: 10.0.401.
- CustomerCrater.Checks: 89 passed, including Reader with CraterEnabled=true,
  missing/disabled/mismatched links, individual missing roles, lookup failure,
  revocation between GET/POST, zero reservation/provider calls on denial, valid
  local eligibility, and preservation of existing owner previews.
- Authentication.Checks: 53 passed. CustomerAuthentication.Checks: 103 passed.
  Customer and operator bearer/cookie identities remain separated; invalid tokens
  fail closed. All 245 checks passed. Rebuilds reported the existing two unused
  Offers fields and three test-fixture BuildServiceProvider warnings; no errors.
- Package restores used only the local NuGet package directory with audit disabled.
  Tests use loopback hosts, static identity metadata, signed fixture tokens,
  in-memory repositories and fake provider transports. Durable artwork tests use
  isolated temporary fixtures.

The follow-up 013B compilation fix was validated with 11 disposable LocalDB checks
in `tests/SqlAccountMigrations.Checks.ps1`, including 013C first/repeat application,
rollback and fixture artwork preservation; the 245 application checks passed again.
Both tracked scripts retain `@Apply=0`. No Azure migrations have been executed by
this work; Azure application remains separately authorized. Live identity-provider
login and Android/device interaction were not performed. No games, credits, guest
architecture or dashboard work is included.
