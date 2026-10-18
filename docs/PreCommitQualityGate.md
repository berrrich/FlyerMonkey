# Mission 005C — pre-commit quality gate

From the repository root, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-PreCommitGate.ps1
```

The script returns 0 only when every required check passes; any failed check or initialization error returns 1. It does not commit, stage, push, merge, change branches, start the application, authenticate to Azure or call an image provider. It is an explicit local gate, not an automatically installed Git hook.

## Prerequisites and workflow

Use Windows with PowerShell 5.1 or later, Git, .NET 10, Windows MAUI/WPF build prerequisites and the Android workload/SDK/JDK. Projects must already be restored using the team's normal approved restore process. Builds/tests use --no-restore; a missing SDK, workload or assets file is a failure rather than an automatic install/network restore. Run sequentially, with no simultaneous IDE builds writing the same outputs.

Review changes, stage only intended files, then run the command immediately before committing. A dirty worktree is permitted. Both indexed content and the current worktree are scanned, including nonignored untracked files. An indexed secret is still detected when the worktree version was sanitized; an unstaged tracked secret is also detected when the index is safe. Deleted index entries are not scanned as proposed content. This workflow does not alter your real index.

## Required checks

1. Credential-pattern and forbidden-file scans of the index and working files.
2. Git ignore probes for environment files, secrets files, local/development settings, Azure/AWS caches, certificates, artwork/staging directories, local databases, build outputs and temporary logs/files.
3. Tracked and staged Git whitespace/conflict-marker checks.
4. PowerShell repository regression checks using synthetic values in isolated temporary Git repositories.
5. CraterIt.Checks: at least 100 checks (including Android read-only gallery behavior and the default-off Development fixture endpoint) and successful process exit.
6. Existing CraterArtwork.AzureChecks: at least 54 checks and successful process exit. These exercise rejection of the canonical product-images container, private-container enforcement, create-only writes and Development-only Azure selection.
7. Sequential API, Web, Windows MAUI, Windows Reviewer and Android Debug builds. Shared/SQL projects build through their references. The cross-platform solution is intentionally not built wholesale because its iOS/MacCatalyst targets require other hosts/toolchains.

Warnings are reported by unique compiler code/location and do not fail the gate. Compiler errors, native process failures and missing test-completion summaries do fail. The output suppresses raw native stdout/stderr and exception values, which could contain credentials. For compiler failures, inspect the reported code/location; for restore/workload failures, verify prerequisites; for a test failure, inspect the named suite locally. There is no raw log file containing captured secret values. The summary prints each check's PASS/FAIL, relevant process exit codes and an overall result.

## Credential and repository policy

Credential heuristics cover common API token formats, AWS access keys, JWTs, private-key headers, password/storage-key assignments, XML credentials, SQL/storage connection strings and SAS signatures. Findings print only source (index/worktree), path, line, rule and fixed remediation text. Never paste or print a suspected value when resolving a finding. If an actual credential has already been committed, removal alone is insufficient: coordinate revocation/rotation separately.

Unmistakable placeholders and dynamic configuration lookups are permitted. SQL strings using Windows-integrated authentication without embedded credentials are permitted; password/key checks still run independently. One exact dummy SAS URI in the existing Azure rejection test is reviewed as a fixture; no whole file is exempted. New exceptions require review of the scanner and its tests, rather than an unrestricted suppression file.

The two existing tracked appsettings.Development.json files are logging-only templates. Ignore rules do not untrack them: the gate separately validates that tracked Development settings contain only Logging/LogLevel with recognized levels. Machine-specific settings do not belong in these templates. Use existing environment variables or user secrets. Ignored appsettings.Local.json/launchSettings.local.json names are protected against accidental commits, but this gate does not add configuration loading behavior for them.

Forced staging cannot bypass the forbidden-file check: DATA, CraterArt/ProductImages directories, bin/obj, credential caches, databases, certificates/private keys, temporary files, compiled binaries and package outputs are rejected even when tracked. Canonical source-controlled images under wwwroot remain allowed. .gitignore protections were extended; no application configuration or credentials were modified.

## Regression checks and verification

The PowerShell suite tests credential recognition without value disclosure, placeholder/configuration lookup handling, safe integrated SQL handling, staged/worktree divergence, UTF-16 BOM text in both representations, forced staging of ignored artifacts, logging-template validation and broken ignore rules. Fixtures contain only synthetic data, remain in the system temporary directory and are not committed or deleted by the suite.

Run that suite alone when diagnosing a gate failure:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/PreCommitGate.Checks.ps1
```

The initial gate failed with exit 1 on scanner-fixture issues; after corrections the complete gate passed with exit 0. Final verification: 48 existing local checks, 54 existing offline Azure checks and 42 new repository assertions passed. API, Web, Windows MAUI and Windows Reviewer builds each exited 0. The final incremental builds reported zero compiler diagnostics; an earlier API build reported the two existing Offers.razor CS0169 unused-field warnings. Credential/artifact, ignore, whitespace and repository checks all passed. Existing application behavior and both .NET test projects are unchanged.

## Limits

- Offline tests do not establish live Azure connectivity, real RBAC/container/network settings or paid provider behavior.
- This scans current indexed/worktree text, not all Git history, remote branches, ignored untracked local secrets, archives or encoded/encrypted binary content. UTF-8 and BOM-marked Unicode text are supported; files with embedded NULs after decoding are treated as binary. Heuristics can miss credentials and can produce false positives.
- Ignore probes use the current working .gitignore rules. Review and stage those rules along with intended changes; do not rely on unstaged ignore edits for a commit. Indexed forbidden paths are checked independently.
- A gate cannot prove arbitrary future test/build code is offline or safe; code review remains necessary. This gate invokes the existing offline suites and does not provide a live Azure mode.
- Scans are not a snapshot/lock: edits made after verification invalidate the result. The index may intentionally differ from the worktree, while builds run current working files. Review staged diffs and rerun after changes.
- The gate compiles Android Debug and evaluates Release network-policy items; it does not run on a device or compile iOS/MacCatalyst. Warnings are visible but are not treated as errors.
