# Mission 009I — completed duplicate reconciliation

Final checkpoint report — 11 November 2026, Australia/Perth.

**Disposition: production cleanup successfully committed on 10 November 2026 after explicit
execution authorization, a fresh matching production preflight and a verified pre-cleanup BACPAC.**
The existing tested transaction removed exactly ten approved excess offers, ten associated
products and four excess category links from flyermonkey-azdb. Independent post-commit SELECTs
confirmed the persisted result:

| Measure | Before cleanup | Verified after cleanup |
| --- | ---: | ---: |
| Offers | 109 | **99** |
| Products | 112 | **102** |
| ProductCategories links | 57 | **53** |
| Current offers on 10 November 2026 | 48 | **38** |

COMMITTED was returned at 2026-11-10T15:19:22Z; independent verification followed at
15:19:33 UTC. The retained five OfferID/ProductID pairs, category dependencies, shared image
references and unrelated surviving records matched the expected fingerprints. No transaction
remained open. These are recorded execution-time results, not a fresh production query on
11 November.

Before cleanup, a full application-schema/data BACPAC was exported and successfully imported
into a new local verification database. Export/import verification completed at
2026-11-10T15:15:04Z, before the production transaction:

- Archive: `D:\FM_DATA\DatabaseBackups\flyermonkey-azdb_20261110T151441471Z.bacpac`
- SHA-256: `69308D14312E27069F864CEBF073FBFC5D7D29D3CCDBAC03C2E2BB58358F18FC`
- All eleven application tables matched production counts and full-row hashes; baseline
  Offers/Products/ProductCategories counts were 109/112/57.
- SqlPackage-excluded dbo.sysdiagrams metadata was preserved in the adjacent tooling companion.
- Mission 009J subsequently repeated the local restore, cleanup rehearsal and Reviewer
  concurrency validation; see [the local validation report](mission009j-local-validation.md).

Execution evidence remains local and is not part of the source checkpoint:
.artifacts/mission009i-production-commit-results.json and
.artifacts/mission009i-post-commit-production.json, plus the archive's verification manifest.
No backup files, credentials or database row exports are included in Git.

The remaining preparation sections below preserve the original recovery investigation,
script design and execution checklist. Their preparation-time observations must not be read
as the final production disposition. An Azure PITR restore drill and full management-plane
retention verification remain unverified; successful BACPAC import does not establish PITR
readiness or continued production-state stability.

## Deliverables and authorized scope

- Cleanup: [009I-reconcile-duplicates.sql](../database/maintenance/009I-reconcile-duplicates.sql).
- Repeatable local validation: [Validate-Dev.ps1](../tools/Mission009I/Validate-Dev.ps1).
- SELECT-only production inspection: [Inspect-Production.ps1](../tools/Mission009I/Inspect-Production.ps1).
- Azure management GET-only inspection: [Inspect-AzureRecovery.ps1](../tools/Mission009I/Inspect-AzureRecovery.ps1).
- Evidence: [.artifacts/mission009i-dev-results.json](../.artifacts/mission009i-dev-results.json)
  and [.artifacts/mission009i-production-readonly.json](../.artifacts/mission009i-production-readonly.json).

These are repository source files for the local checkpoint. During the original preparation,
only uniquely named local disposable DEV databases were created/written. The subsequently
authorized production row reconciliation is recorded above. No production schema changes,
blob changes or Reviewer extraction-history changes were made. No push or deployment is included.

## Phase 1 — original recovery evidence and remaining verification

Production SQL was accessed using the existing secure environment configuration with
ApplicationIntent=ReadOnly and SELECT statements only. Credentials and connection strings were
not displayed. EngineEdition=5 confirms Azure SQL Database. The service objective is
GeneralPurpose / GP_S_Gen5_2 (serverless), with no elastic pool. Allocated data-file space was
40 MB and used space approximately 10.62 MB; these are not configured maximum storage or billed
storage estimates.

The read-only capture at **2026-11-10 12:43:01 UTC / 20:43:01 AWST** recorded:

| Backup type | In-retention records | Oldest finish UTC | Latest finish UTC |
| --- | ---: | --- | --- |
| Full | 1 | 2026-11-07 14:45:21 | 2026-11-07 14:45:21 |
| Differential | 6 | 2026-11-04 15:37:17 | 2026-11-10 11:56:59 |
| Log | 52 | 2026-11-03 14:27:11 | 2026-11-10 12:30:18 |

Older out-of-retention metadata also exists. An in-retention flag verifies backup metadata
availability, **not an end-to-end usable restore chain, exact earliest restore point, latest
restorable timestamp or configured retention period**. Do not infer seven-day retention from
these timestamps. Gaps must not be interpreted as demonstrated data loss or restore coverage.

Azure CLI was unavailable; Az.Accounts was installed but Get-AzContext returned no authenticated
management context. Consequently these items remain unverified: subscription/resource identity,
region, configured STR days, differential interval, LTR policy/inventory, backup redundancy,
geo-restore eligibility, restore permissions, and an actual successful PITR restore.

An operator can authenticate separately and run the prepared GET-only management inspector
with the exact database resource ID from the portal. It reads the database properties, STR/LTR
policies, LTR backup inventory and restorable dropped-database inventory. Save its output in
an access-controlled location; resource identifiers are operational metadata. A 403, incomplete
inventory or failed request is a verification failure, never evidence that backups do not exist.
Do not run a setting-change or restore command as part of inspection.

Azure SQL provides PITR into a **new database on the same logical server** within the configured
retention window. Existing databases cannot be overwritten by restore. Deleted-database restore,
LTR restore and geo-restore are additional options subject to their actual retention,
backup inventory, redundancy and permissions. These are platform capabilities; their availability
for FlyerMonkey has not all been verified.
Sources: [automated backups](https://learn.microsoft.com/en-us/azure/azure-sql/database/automated-backups-overview?view=azuresql),
[restore options](https://learn.microsoft.com/en-us/azure/azure-sql/database/recovery-using-backups?view=azuresql),
[backup metadata DMV](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-database-backups-azure-sql-database?view=azuresqldb-current).

### Recommended DEV/TEST separation

Keep the local synthetic fixture as the default no-Azure-cost validation environment. Recommend,
subject to approval, a separate Azure database named FlyerMonkey-DEV-009I with separate application
credentials, no production writer or paid-provider jobs, no Reviewer Commit integration, and
read-only/no-access blob permissions. Do not point the production website at it.

For a recovery drill, PITR must first create a uniquely named database on the source logical
server. That is a separate database, not full server isolation. A persistent DEV environment can
use a separate logical server and sanitized representative data after separately approved setup.
Do not claim PITR supports arbitrary cross-server targets. A restored production database also
contains customer data: restrict access and sanitize before general DEV use.

### Recovery procedure — reference-checked, Azure restore drill still required

1. Before any approved production cleanup, pause ingestion/Reviewer commits and other catalogue
   writers. Obtain explicit approval for this maintenance action; it was not performed here.
   Keep the original database and all backups intact.
2. Record subscription, resource group, logical server, database, database resource ID,
   UTC time, resource region/SKU, STR policy, actual earliest selectable restore time and the
   intended pre-cleanup recovery timestamp. Re-run both read-only inspectors.
3. With separate approval to create a billable restore target, use Azure portal > the exact SQL
   database > Restore > point-in-time. Choose a **unique new database name** on that server.
   Confirm the chosen timestamp is eligible and covered; do not substitute the most recent
   backup timestamp without confirming PITR coverage. Record approved compute/storage choices.
4. Create the restored database only after that approval; wait for Online. Record the source ID,
   UTC restore timestamp, target ID, elapsed restore time and operation result. No such operation
   was attempted in this mission.
5. Connect through separate DEV credentials. Verify schema, integrity, the 15 exact target pairs,
   all target content/category/image references, total counts and representative unrelated
   records. Use the production inspector with a process-local DEV connection configuration,
   restoring/removing that override afterward. Do not expose credentials or retarget production.
6. Run the cleanup against that DEV target first in rollback mode, then with a DEV-only commit,
   then repeat. Check the results described below. DEV failure injection in the SQL is deliberately
   forbidden on EngineEdition=5: use the local harness for the after-delete failure test.
   Azure compatibility/schema validation remains a separate acceptance gate.
7. A successful drill, recorded evidence and an eligible fresh pre-cleanup timestamp are required
   before production execution approval. An old successful drill alone does not establish a
   current recovery point. Measure actual recovery time; this mission establishes no RTO/RPO SLA.
8. During an uncommitted cleanup, any assertion failure invokes ROLLBACK; a reviewer may also
   cancel and confirm transaction state from a new connection. Do not blindly retry an ambiguous
   disconnect at commit: inspect the exact target pairs and persisted counts first.
9. After a committed cleanup, SQL ROLLBACK cannot undo it. Restore the recorded pre-cleanup point
   into another new database. Validate it; compare/reconcile any legitimate writes made since
   that timestamp. Choose an explicitly authorized repair or application cutover, including
   credentials/network/authentication verification and website smoke checks. Never overwrite,
   delete, rename or swap production automatically. A whole-database cutover without reconciling
   later writes could lose unrelated customer/catalogue changes.
10. Preserve restore targets and backups pending an approved retention/disposal decision. No
    DROP DATABASE, backup deletion or backup overwriting is included in these deliverables.

This procedure was checked against Microsoft documentation; it is **not restore-tested**.
Calling it a verified production recovery procedure must wait for steps 2–7 to succeed.

### Cost estimate considerations

The local LocalDB tests incur no new Azure resource charges. Existing serverless SQL reads can
resume a paused database and incur normal compute charges. An Azure restore/DEV target incurs
additional database compute, storage and potentially backup-storage charges while retained.
Auto-pause eligibility and delay must be checked; backup storage and provisioned storage do not
become free simply because compute pauses. Budget for the restore target's selected/source tier,
not an assumed tiny DEV tier. Do not assume the free tier or a discount applies.

Use the actual region/currency/contract quote from
[Azure SQL Database pricing](https://azure.microsoft.com/en-us/pricing/details/azure-sql-database/single/)
and [serverless billing](https://learn.microsoft.com/en-us/azure/azure-sql/database/serverless-tier-overview?view=azuresql).
Estimate: billed vCore-hours × quoted vCore-hour rate + storage GB-months × rate + billable backup
GB-months × rate, with any network/other applicable charges. Memory-related serverless billing
can increase billed compute above a CPU-only estimate.

For arithmetic only, **if** the applicable rate were USD 0.60/vCore-hour, a 2-vCore target active
for two hours would cost about USD 2.40 compute; retaining it active for 24 hours would be about
USD 28.80 compute, plus storage/backups. These are hypothetical planning examples, **not a regional
Azure quote or spending authorization**. Exact cost remains unverified because region, selected
restore configuration and contract rates are unavailable. Set an agreed duration/spend cap before
approval; resource disposal itself remains separately authorized.

## Phase 2 — exact reconciliation scope

| Product | Retained OfferID / ProductID | Excess OfferID / ProductID |
| --- | --- | --- |
| Australian Navel Oranges | 153 / 161 | 159 / 167; 171 / 179 |
| Chicken Garlic Kyly | 154 / 162 | 160 / 168; 172 / 180 |
| Muscle Nation High Protein Bar | 155 / 163 | 161 / 169; 173 / 181 |
| Zooper Dooper Ice Blocks | 156 / 164 | 162 / 170; 174 / 182 |
| Vanish Gold Stain Remover Powder | 157 / 165 | 163 / 171; 175 / 183 |

Retention follows the approved earliest-equivalent policy. The SQL checks creation ordering;
no highest-ID assumption is used. All pairs are explicit. All five groups must match retailer 1,
location 3, Flyer/page-002.pdf/page 2, validity 2026-11-04 through 2026-11-11, expected product
identity, barcode, advertised/regular/saving amounts, pack, promo/unit-price text and image paths.
Null optional offer metadata and product UpdatedUtc must remain as audited.

Chicken Product 162 retains Category 4 Deli (primary) and Category 2 Meat & Poultry (secondary).
Only the four identical links on excess Products 168/180 are removed. Category master rows,
all five image blobs, retailer/location rows, Rokeby/Pepsi orphan products and all other data
remain outside scope. No OfferPromotionTypes or ProviderListings may exist at this baseline.

The SQL uses a dedicated connection, explicit server/database checks, XACT_ABORT, TRY/CATCH,
a single transaction, bounded lock wait and conservative table/range locks. It rejects schema
column-count drift, target triggers, changed/missing/disabled/untrusted/cascading/composite or new
inbound foreign keys, new references, partial states and changed counts/content. It snapshots
target/reference tables in temporary tables and checks surviving/unrelated rows and protected
relationships before the final decision. Any assertion rolls back. Outputs identify proposed
and affected IDs plus before, in-transaction after and persisted-after-decision counts.

Defaults are identity placeholders and @Commit=0. The placeholders prevent accidental use.
After explicitly confirming identity in a reviewed execution copy, @Commit=0 performs the
deletes **inside a transaction and rolls them back**. Therefore it is a write simulation and
must not be used on production under SELECT-only authorization. A commit additionally requires
@Commit=1 and the exact authorization string in the configuration block. This string is a
technical guard, not a substitute for human execution approval.

| Measure | Fresh baseline | Approved reconciliation result |
| --- | ---: | ---: |
| Offers, all history | 109 | 99 |
| Offers current on audit date 2026-11-10 | 48 | 38 |
| Products | 112 | 102 |
| ProductCategories | 57 | 53 |
| Categories | 19 | 19 |
| RetailerLocations | 2 | 2 |
| Distinct product image paths | 80 | 80 |
| ProviderListings / OfferPromotionTypes | 0 / 0 | 0 / 0 |

The fixed audit date makes the script repeatable. On a later execution date, actual current-offer
counts can differ due to expiry. Do not blindly expect the website to show 38 after 11 November.
Fresh counts and validity must be reviewed; if the audited baseline differs, the script fails
closed and needs a new reviewed baseline, not relaxed assertions.

## Phase 3 — repeatable local DEV procedure and results

Run from the repository root on Windows with SQL Server LocalDB installed:

```powershell
# Start the existing local instance only, if required.
& 'C:/Program Files/Microsoft SQL Server/170/Tools/Binn/SqlLocalDB.exe' start MSSQLLocalDB
powershell -NoProfile -File tools/Mission009I/Validate-Dev.ps1
```

The harness never reads production connection settings. It hard-codes (localdb)\MSSQLLocalDB,
checks the engine is not Azure and creates a unique Mission009I_Disposable_<12 hex characters>
database per case. It refuses other database names. Fixtures and failed-attempt databases are
retained, not dropped or overwritten.

Each fixture contains the exact 15 target pairs and audited business fields, six Chicken category
links, shared image-reference strings, and synthetic unrelated records giving the 109/112/57/48
baseline. Schema includes the ten relevant foreign keys and representative price/date checks.
It does not copy production customer data, SQLite or image files. Unrelated rows, creation
timestamps, some reference-table columns and identity/index configuration are synthetic:
this is representative transaction/dependency validation, not a full production clone.

The runner executes the same repository cleanup SQL after substituting only the explicit
configuration block values in memory. For commit tests it enables commit only on the local
disposable connection. Independent full-row JSON SHA-256 fingerprints compare fixture tables
before/after dry runs and expected failures; counts and transaction state are also checked.
All 20 cases passed:

- Default rollback leaves all fixture rows unchanged.
- Authorized local commit persists 99 offers, 102 products, 53 links and 38 audit-date current
  offers; retained and unrelated relationships pass preservation assertions.
- Repeat on the same connection returns ALREADY_CLEAN_VALIDATED_NO_CHANGES without row changes.
- Deliberate assertion failure after all three deletion stages restores all original rows.
- Wrong server identity is refused.
- Changed price, product identity, case-only identity, validity, pair, image and category-primary
  mapping are refused without further persistent changes.
- Unexpected listing, promotion link, inbound FK, target trigger and added target column are refused.
- Additional offer reference, missing excess offer, later-created retained record and unrelated
  count drift are refused.

The exact cleanup SHA-256, run UTC time, SQL error codes and retained fixture database names
are in the DEV JSON report. All successful/failed scripts leave @@TRANCOUNT=0. Initial local
startup required execution outside the sandbox. Development validation caught and corrected
an identity-placeholder guard issue and same-connection temporary-table reuse issue; the final
20-case run passed. No application build/regression suite was needed because application code
was not changed.

Final validation completed at 2026-11-10T12:46:38Z. Validated cleanup SHA-256:
`19D679F34FA3BDC8CE80ECE8276E88B76C7D70B7BC1454C445DCCE6907E0AA05`.

The prepared Azure recovery inspector was syntax-checked but could not be exercised against ARM
without an authenticated context. Azure restore compatibility, regional cost, backup policy and
restore permissions remain open gates.

## Phase 4 — original production readiness checklist (historical preparation)

- [ ] Obtain and record Azure database resource identity, STR/LTR/redundancy and restore permissions.
- [ ] Obtain approval for the billable isolated Azure restore target; perform and document the drill.
- [ ] Verify a fresh eligible pre-cleanup recovery timestamp and exact portal restore instructions.
- [ ] Obtain separate production cleanup execution authorization covering the reviewed SQL hash,
      exact pairs, server/database, counts, maintenance window and recovery plan.
- [ ] Quiesce catalogue writers through an approved operation; do not assume DEV success froze production.
- [ ] Run Inspect-Production.ps1 immediately beforehand using the secure production configuration.
      Review every candidate field, dependency, schema constraint and count against the SQL; the report
      alone does not grant execution permission.
- [ ] Confirm exact live server/database names independently from the Azure resource ID and a SQL
      identity SELECT, then populate both placeholders in the reviewed execution copy. Never copy
      credentials into the script. The stored identity hash is corroboration, not the sole identity check.
- [ ] Confirm 10 excess Offers, 10 excess Products and four excess category links, with every retained
      pair and both Chicken category links intact. Resolve any drift before execution.
- [ ] Review the default rollback run on DEV; do not treat a production dry run as read-only.
- [ ] Authorized operator runs the reviewed production execution copy once on a dedicated connection,
      captures results and resolves ambiguous completion by SELECT before any retry.
- [ ] Fresh post-execution SELECT verifies exact absent/present pairs, 99/102/53 totals, preserved
      images/categories/locations, no FK orphans and current-date counts. The script's assertions and
      output must also show the authorized outcome.
- [ ] Verify website/API Offers current and historical views, all five product details, images,
      Deli and Meat & Poultry filters, retailer/location display and unrelated offers. On 10 November
      with no other changes, expect 38 current offers and one Chicken Garlic Kyly promotion.
      Inspect raw offer IDs in API output as well as visual counts to distinguish cache/join issues.
- [ ] If anything fails before commit, confirm rollback. If it fails after commit, use the PITR
      procedure above; review later writes and obtain explicit repair/cutover approval.
- [ ] Resume writers only after approved verification; preserve evidence and restore targets pending
      a separately approved disposal decision.

**Final disposition:** the separately authorized production cleanup is complete and verified
at 99 offers, 102 products, 53 category links and 38 execution-date current offers. No further
database operation is authorized by this report. Management-plane/PITR verification remains
separate from the successful BACPAC export/import and completed reconciliation.
