# Reviewer commit validation harness

These checks write only to the literal named LocalDB instance/database in Program.cs. They retain fixture rows and SQLite databases for inspection. They never reset production extractions or delete records.

Prepare the isolated instance once:

```powershell
sqllocaldb create ReviewerSafetyValidation -s
```

Run transaction, concurrency and process-crash checks:

```powershell
dotnet run --project tests/ReviewerCommitValidation.Checks -- --integration
```

Open the real WPF window with isolated SQLite and fake extraction, no paid calls:

```powershell
dotnet run --project tests/ReviewerCommitValidation.Checks -- --ui
```

The auxiliary fixture button prepares an unsaved fake page for Get Data/Cancel. It does not invoke a provider. Price starts blank for validation/recovery testing. SQL Commit writes only to the disposable LocalDB endpoint in that child process. Fixture products have no image paths, so no upload runs. Use --ui-status for read-only fixture status inspection.

`--inspect` uses the existing FLYERMONKEY_SQL_CONNECTION **only for SELECT queries**, with read-only application intent. It prints sanitized connection settings, current/previous counts for 5 November 2026, duplicate candidates and SQLite batches 21-24. No credentials are printed. Its business-key groupings identify candidates, not authorized data corrections.

See docs/reviewer-processing-safety.md for results, limitations and remaining live-use blockers.


## Mission 009A additions

The integration command now runs the original 22 regression checks, followed by 22 explicit-transaction/recovery checks using the same disposable LocalDB endpoint. The ambient-promotion failure control belongs to the old repository path; Reviewer now uses ReviewerSqlBatch with one explicit connection/transaction.

Run the 21 offline WPF recovery/layout checks against fresh disposable SQLite:

```powershell
dotnet run --project tests/ReviewerCommitValidation.Checks -- --recovery-ui-checks
```

`--ui-009a` opens the actual window with an additional synthetic Committing entry. Its auxiliary button selects the Saved fixture and exposes fake Get Data for cancellation checks. `--ui-009a-error` creates a test-only constraint in the named LocalDB database that rejects the second offer for ui-009a-error-page.pdf; it exercises the real Commit handler rollback and recovery display. The latter mode must only be used with the disposable endpoint built into this harness. Neither mode changes operator SQLite or production SQL settings; overrides are confined to the fixture process.

If sandbox compiler servers cannot be reached, build with `dotnet build tests/ReviewerCommitValidation.Checks --no-restore -m:1 -p:UseSharedCompilation=false`, then run with `--no-build`.
