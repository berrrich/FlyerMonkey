# Reviewer workflow checks

Build with `dotnet build tests/ReviewerWorkflow.Checks -m:1 -p:UseSharedCompilation=false`, then run `dotnet run --project tests/ReviewerWorkflow.Checks --no-build`.

The executable runs offline archive, cancellation, progress, extraction normalization and actual WPF handler checks using temporary SQLite, synthetic PDF/JPEG files and fake providers. Fixtures remain under `.artifacts/reviewer-workflow` for inspection. It does not use production SQL, Azure or paid providers.

Pass `--ui` for the interactive fake-provider Reviewer fixture. Image generation waits 15 seconds to allow per-card generation and cancellation inspection. Save writes only its disposable SQLite database. Close the window to exit.

Archive records and lifecycle events are append-only; rejected or late returned artwork remains available on disk. Cancelling a remote request cannot prove that remote processing stopped. Physical mixed-monitor DPI transfer requires separate manual verification.

Mission 009E finishing checks: pass --layout-only to run 27 focused actual-WPF window-bound and centered-thumbnail checks without invoking extraction, generation or image-action handlers. Uses synthetic files and disposable SQLite only. The test executable shares the Reviewer's PerMonitorV2 manifest. Physical mixed-monitor transitions remain operator verification.

Final polish layout-only coverage now includes responsive thumbnail inner-card width, preserved aspect ratio, unchanged card width, resize, centering and natural pixel-resolution cap (32 checks).


Mission 009F replaces the old global-progress Crater checks with concurrent per-card actual-WPF checks. Mock providers only: two-request cap and queue handoff, scrolling during generation, duplicate prevention, synchronous provider setup off the dispatcher, per-card failure/retry/cancellation, late-result archive, context/close safety and Save after jobs drain. Persistence and other global operations require pending card jobs to finish/cancel; they are not queued automatically. Test evidence: .artifacts/mission009f-checks.txt.
