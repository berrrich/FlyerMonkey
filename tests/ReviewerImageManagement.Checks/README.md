# Reviewer image-management offline checks

This project references the WPF Reviewer. It injects in-memory generators, creates synthetic JPEGs and uses only disposable SQLite. It never instantiates a real provider client and opens no SQL Server or Azure connection. Fixtures are retained under .artifacts/reviewer-image-management.

```powershell
dotnet build tests/ReviewerImageManagement.Checks -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false
dotnet run --project tests/ReviewerImageManagement.Checks --no-build
```

Open the actual image-review window with a fake generator and synthetic samples:

```powershell
dotnet run --project tests/ReviewerImageManagement.Checks --no-build -- --ui
```

Generate in this fixture is always fake. Accept writes a new synthetic JPEG; Reject keeps original.jpg. Existing images and Azure blobs are never removed. No original operator SQLite or image-library paths are used.

Relevant regression suites: ReviewerClassic.Checks, ReviewerEditing.Checks, ReviewerSafety.Checks, and ReviewerCommitValidation.Checks with --recovery-ui-checks. The Classic suite's provider and Azure HTTP requests are intercepted by fake transports; it makes no network requests. SQL integration and production audit modes are unnecessary for this mission.
