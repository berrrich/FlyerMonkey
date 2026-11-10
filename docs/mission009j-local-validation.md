# Mission 009J — local restore and Reviewer commit protection

Current Mission Report — 10 November 2026, Australia/Perth.

**Result: PASS.** Restored and validated all eleven application tables from the verified
pre-cleanup BACPAC, rehearsed the 009I cleanup locally, and passed 27 Reviewer protection checks.
All databases and synthetic SQLite fixtures were retained. No production connection, Azure
operation, paid-provider request, deployment or Git operation occurred in this mission.

## Reusable restore and evidence

Script: [Restore-VerifiedBacpac.ps1](../tools/Mission009J/Restore-VerifiedBacpac.ps1).
It uses Windows PowerShell and the installed SqlPackage DacFx library. It never reads production
credentials and hard-codes the local endpoint (localdb)\MSSQLLocalDB. It requires a disposable
database name, verifies SERVERPROPERTY('IsLocalDB'), refuses a pre-existing name, and atomically
reserves a new database with CREATE DATABASE before importing into that invocation's empty
database. A concurrent name collision therefore fails before import. No database is overwritten,
dropped or automatically removed after failure.

Input archive:
`D:\FM_DATA\DatabaseBackups\flyermonkey-azdb_20261110T151441471Z.bacpac`

Verified SHA-256:
`69308D14312E27069F864CEBF073FBFC5D7D29D3CCDBAC03C2E2BB58358F18FC`.

The archive and original verification manifest are read-only inputs. Every restored table's
count and deterministic full-row JSON SHA-256 must match the archive verification manifest.
The eleven tables and baseline counts were:

| Table | Restored rows |
| --- | ---: |
| Categories | 19 |
| Customers | 2 |
| DeliveryProviders | 2 |
| OfferPromotionTypes | 0 |
| Offers | 109 |
| ProductCategories | 57 |
| Products | 112 |
| PromotionTypes | 9 |
| ProviderListings | 0 |
| RetailerLocations | 2 |
| Retailers | 2 |

SqlPackage's omitted dbo.sysdiagrams support table remains preserved in the archive's original
tooling companion; it is not one of these eleven application tables and was not imported.
Database customer data was not printed in test output.

Run from the repository root:

```powershell
# Restore and validate only; a unique database name is generated automatically.
powershell -NoProfile -File tools/Mission009J/Restore-VerifiedBacpac.ps1

# Fresh restore plus rollback/commit/repeat rehearsal of the unchanged 009I cleanup.
powershell -NoProfile -File tools/Mission009J/Restore-VerifiedBacpac.ps1 -RehearseCleanup
```

The script records the chosen database name and writes a JSON report under
.artifacts/mission009j/<database>.restore.json. A custom name, if supplied, must match
Mission009J_Disposable_<12 lowercase hex characters>. Existing LocalDB must be available;
the script never creates an Azure resource.

## Local cleanup rehearsal

Primary retained result:
**(localdb)\MSSQLLocalDB / Mission009J_Disposable_4161c334e0df**.

The final restore script was exercised end-to-end with its atomic database reservation.
It ran the existing 009I SQL, checked its previously tested source SHA-256, and substituted only
local server/database identities and the local commit configuration. Production settings were
not consulted.

| Stage | Offers | Products | ProductCategories |
| --- | ---: | ---: | ---: |
| Validated restore | 109 | 112 | 57 |
| After default rollback rehearsal | 109 | 112 | 57 |
| After local committed reconciliation | 99 | 102 | 53 |
| Repeat execution | 99 | 102 | 53 |

The rollback rehearsal preserved all eleven table hashes. The committed rehearsal preserved
exact surviving rows and relationships, including the five retained pairs and their image
references. All unrelated tables matched their baseline hashes. Repeat execution returned
ALREADY_CLEAN_VALIDATED_NO_CHANGES. The fixed audit-date current-offer assertion also passed
at 38 after cleanup.

Retained pairs: 153/161, 154/162, 155/163, 156/164 and 157/165.
Only the ten approved excess pairs and four category links were removed in the local rehearsal.
No image files or blobs were opened for writes.

An explicit attempt to restore over an existing rehearsal database was refused. Independent
checks confirmed all eleven existing table hashes remained unchanged.

Evidence:

- [.artifacts/mission009j/Mission009J_Disposable_4161c334e0df.restore.json](../.artifacts/mission009j/Mission009J_Disposable_4161c334e0df.restore.json)
- [.artifacts/mission009j/restore-refusal.json](../.artifacts/mission009j/restore-refusal.json)

## Reviewer protection checks

Harness: [ReviewerLocalRestore.Checks](../tests/ReviewerLocalRestore.Checks/Program.cs).
It references the existing Reviewer/WPF and SQL repositories; application implementation was not
changed. It accepts only a Mission009J disposable name on the hard-coded local endpoint and checks
the engine/database identity and archive baseline before writes. Production connection settings
are overridden only inside the isolated test process. Extraction/image delegates throw if called;
synthetic products have no images, so no blob upload occurs.

A separate restored database kept the cleanup result above undisturbed:
**(localdb)\MSSQLLocalDB / Mission009J_Disposable_0a998b28ece8**.

All **27 checks passed**, using actual WPF Commit routed events and handlers, the real processing
gate, SQLite SqlCommitService claims/acknowledgement, and ReviewerSqlBatch transactions:

- Twenty-five rapid reentrant Commit events during the first confirmation produced exactly one
  confirmation, one committed/acknowledged batch and two offers/products/category links.
- Committed selection disabled Commit. A forced programmatic repeat wrote nothing.
- A local-only CHECK constraint rejected the second offer after the first product/offer/category
  set was written inside the transaction. All batch rows rolled back across the three tables.
- The failed SQLite batch remained Committing, displayed Recovery Required, and disabled Commit.
  Both a retry click and a new SqlCommitService claim were refused. **This tests safe refusal of
  uncertain retries, not automatic recovery or a successful retry after resetting state.**
- Twelve overlapping service requests for one SQLite batch produced exactly one claim winner
  and one complete, acknowledged two-offer SQL batch. Later submission was refused.
- Exactly four successful synthetic products persisted; each has exactly one offer. No partial,
  orphan or duplicate fixture records remained. The failed fixture persisted no SQL rows.
- Original rows across all eleven restored archive tables remained byte-equivalent under
  deterministic JSON hashing.

The Reviewer test database ends at **113 offers, 116 products and 61 category links**:
its restored 109/112/57 baseline plus two deliberate successful two-record test batches.
These are isolated fixture additions, not duplicate-cleanup results. The primary cleanup
rehearsal database remains at 99/102/53.

Build and run with a **fresh restore-only database**:

```powershell
dotnet build tests/ReviewerLocalRestore.Checks --artifacts-path .artifacts/mission009j-build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false -p:RuntimeIdentifier=win-x64 -p:CopyDebugSymbolFilesFromPackages=false
& '.artifacts/mission009j-build/bin/ReviewerLocalRestore.Checks/debug_win-x64/ReviewerLocalRestore.Checks.exe' '<newly restored database name>'
```

The harness refuses a database whose baseline is no longer 109/112/57. Repeat testing uses
another fresh database; no existing records or extraction statuses are reset.

Final unattended evidence:

- [.artifacts/mission009j/reviewer-checks-final.txt](../.artifacts/mission009j/reviewer-checks-final.txt)
- [.artifacts/mission009j/reviewer-eaf4c0db297042418288f568f0c382cc/results.json](../.artifacts/mission009j/reviewer-eaf4c0db297042418288f568f0c382cc/results.json)

The SQLite fixtures are in that report's Root directory. Databases from preliminary runs are
also retained: Mission009J_Disposable_151b034d4406 (earlier cleanup rehearsal) and
Mission009J_Disposable_024a7013fe2b (initial Reviewer run).

## Validation limits and activity

The first UI harness run needed test-only dialog dismissal correction for this Windows version's
OK button control ID. After correction, a fresh restore and fully unattended 27-check run passed.
A build attempted while that initial test executable was running encountered a file lock; rebuilding
after its exit succeeded. The clean initial build showed only the two existing Offers.razor
unused-field warnings; the final incremental build completed with zero warnings/errors.

These are automated WPF control/handler tests, not mouse-driven verification or a production
concurrency certification. Process-crash/connection-loss ambiguity was not retested in this
mission. A logical BACPAC import is not an Azure PITR drill. Nothing here establishes current
production counts or changes production recovery settings.

No production writes or reads, Azure changes, paid calls, deployment, Git operations, backup
modifications or operator extraction-history modifications occurred. All local test databases,
constraints, synthetic SQLite batches and reports are retained for inspection.
