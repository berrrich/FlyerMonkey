# Reviewer Saved editing and PDF preview checks

Use only synthetic PDFs and disposable SQLite. No SQL Server connection is opened. The fixture process clears SQL/blob/OpenAI credentials and injects an offline extraction provider. Existing FLYERMONKEY_SYNCFUSION_KEY is used only for local SDK licence registration/validation, without printing it. Syncfusion telemetry is disabled.

```powershell
dotnet build tests/ReviewerSavedPreview.Checks -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false
dotnet run --project tests/ReviewerSavedPreview.Checks --no-build
```

Open the real Reviewer with the same synthetic fixtures after the checks:

```powershell
dotnet run --project tests/ReviewerSavedPreview.Checks --no-build -- --ui
```

The viewer is Syncfusion PdfDocumentView 35.1.37. Its Pdf.Wpf dependency is aliased to avoid conflicting with the existing portable PDF processing library. Checks exercise both the splitter and PDFtoImage after viewer loading, UI synchronization, fit/zoom, selection context and SQLite persistence.

Committed/Committing statuses are synthetic fixture setup, not claims or acknowledgements against SQL. Committed updates and recommit-by-insert are deliberately unsupported. Fixture files remain under .artifacts/reviewer-saved-preview for inspection.

Mission 009E adds responsive Fit width, native scroll preservation, maximize/restore and simulated DPI notification checks. Build with --artifacts-path .artifacts/mission009e-final when the normal Reviewer output is locked by a running app. The fixture shares the Reviewer's PerMonitorV2 manifest. Physical monitor transitions and mouse-driven desktop verification remain separate operator checks; simulated notifications do not change Windows display scaling.

Final polish adds Fit Width button checks from 59/34/250%, native rendered extent and scrollbar assertions, portrait/landscape PDFs and nested ScrollViewer cases at layout scales 1/1.25/2. Layout-scale tests do not simulate physical monitor transfers. Evidence is in .artifacts/mission009e-polish-pdf.txt.
