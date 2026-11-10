# WPF Reviewer - Cumulative Mission Report

## Current Mission Report - 10 November 2026: Mission 009J - Local Restore and Commit Test

**Disposition:** Completed a reusable local-only BACPAC restore script, validated all eleven application tables against the verified archive's counts and full-row hashes, and rehearsed the unchanged 009I cleanup. The retained primary LocalDB database Mission009J_Disposable_4161c334e0df restored at 109 offers / 112 products / 57 category links, passed rollback preservation, committed locally to **99 / 102 / 53**, and passed already-clean repeat execution. Restore refuses existing database names and reserves new names atomically; an explicit overwrite-refusal check preserved every table.

**Reviewer validation:** **27 checks passed** against a separate restored LocalDB database, Mission009J_Disposable_0a998b28ece8. Actual WPF handlers processed 25 rapid reentrant clicks as one confirmation/one batch; 12 overlapping durable-claim requests produced exactly one complete batch. A second-offer SQL failure rolled back all products/offers/category links. Retry after that failure was blocked as Recovery Required, and repeat submission of a committed batch wrote nothing. Four intentional successful fixture records leave this separate test database at 113 offers / 116 products / 61 links; the original eleven-table archive rows remain unchanged. No mouse-driven UI verification is claimed.

Reusable script, detailed procedure, retained database names, complete results and limits: [Mission 009J report](mission009j-local-validation.md). Evidence: .artifacts/mission009j/Mission009J_Disposable_4161c334e0df.restore.json, restore-refusal.json, reviewer-checks-final.txt and reviewer-eaf4c0db297042418288f568f0c382cc/results.json. Application implementation was unchanged. No production connections/writes, Azure operations, paid calls, deployments, Git operations, backup changes or operator extraction-history changes occurred. All local databases and synthetic fixtures are retained.

---

## Previous Mission Report - 10 November 2026: Mission 009I - Duplicate Reconciliation Preparation

**Disposition:** Prepared the guarded SQL reconciliation and recovery/production-readiness runbook; **20 local disposable DEV scenarios passed**. Production cleanup was not executed. The exact retained pairs are 153/161, 154/162, 155/163, 156/164 and 157/165; scope is only the ten approved excess Offer/Product pairs and four dependent category links.

Full self-contained recovery plan, reconciliation table, cost assumptions, repeatable DEV procedure, results, limitations and production checklist: [Mission 009I report](mission009i-reconciliation.md). Cleanup source: [009I-reconcile-duplicates.sql](../database/maintenance/009I-reconcile-duplicates.sql). It defaults to rollback, requires explicit server/database identity, validates content/dependencies/counts, rejects partial states and checks retained/unrelated rows. A dry run performs transactional deletes before rollback and is therefore not SELECT-only.

**Production evidence:** SELECT-only inspection at 2026-11-10 12:43:01 UTC confirmed 109 offers, 48 current offers, 112 products, 57 category links and the audited five groups. Azure SQL is General Purpose serverless GP_S_Gen5_2. Automated backup metadata includes in-retention full/differential/log backups; latest captured log backup finished at 12:30:18 UTC. Configured STR retention, LTR inventory, redundancy, earliest selectable restore timestamp, restore permission and an actual Azure PITR drill remain unverified because no authenticated Azure management context was available. Backup metadata alone does not verify recovery readiness.

**Validation/activity:** The local-only harness validated default rollback, committed DEV counts 99/102/53 with 38 audit-date current offers, same-connection already-clean behavior, rollback after all deletes, and refusal of identity/price/date/pair/image/category drift, unexpected dependencies/triggers/schema, partial state and count drift. Evidence: .artifacts/mission009i-dev-results.json (exact SQL SHA-256 and retained fixture database names), .artifacts/mission009i-production-readonly.json. No Azure resource/settings changes, production writes, backup deletion/overwrite, image/blob changes, Reviewer history changes, application changes, Git operations or deployment. Local fixture databases are retained.

**Stop/approval gates:** Obtain Azure management recovery evidence and separate approval for a billable restore drill; then obtain fresh production preflight and separate cleanup execution authorization. DEV success does not establish that production remains unchanged. No production cleanup or Azure restore is authorized by this report.

---

## Previous Mission Report - 6 November 2026: Mission 009G - Flyer Status

**Disposition:** Flyer cards are neutral by default, blue when at least one completed, readable single-page `page-NNN.pdf` is discovered, and green when manually marked complete. Green overrides blue. An independent blue selection outline remains visible over either status colour. Mark Complete / Reopen operates on the selected flyer and participates in the existing processing gate, including exclusion while image jobs remain active.

Completion is stored locally in the Reviewer SQLite `ReviewerFlyerStatus` table. Identity is the case-normalized retailer plus complete source filename, independent of workspace path. Reopen persists false without altering extraction records, offer drafts or split files. Status is rediscovered on startup, flyer refresh/selection and successful split; completion actions update the bound card immediately. Completion neither saves offers nor commits them. The confirmation conservatively warns whenever editable offers or an editable page/batch are open, including a page whose offers were all deleted; precise dirty tracking is not available.

Split discovery uses the existing filename-based folder convention and validates page PDFs. Empty folders, corrupt/unexpected PDFs, numbering gaps, source/page-count mismatch and abandoned staging folders produce non-destructive warnings. A folder containing at least one valid page can still be blue while displaying an incomplete warning. Existing output is never overwritten or deleted; no automatic repair occurs. Multiple incoming source paths sharing a filename stem, including different retailers, block page association and splitting. Saved PDF resolution uses the same guard; completion changes also refuse an ambiguous identity. Warning text is visible for the selected flyer and in the card tooltip.

**Validation:** 26 disposable status checks passed, including absent/empty/corrupt/staged splits, valid discovery, incomplete trailing pages, cross-retailer and same-retailer filename collisions, SQLite completion/reopen, path-independent identity, new-window reload, actual WPF completion handlers, unsaved-warning guards, selected/deselected templates and preservation of split bytes. The existing 71 Saved/PDF checks also passed. Build succeeded; the clean build showed only the two existing unused-field warnings in Offers.razor. No mouse-driven desktop verification is claimed. Evidence: `.artifacts/mission009g-status-checks.txt`, `.artifacts/mission009g-saved-checks.txt`. Reproduce with `dotnet run --project tests/ReviewerFlyerStatus.Checks --artifacts-path .artifacts/mission009g-build -p:NuGetAudit=false -p:UseSharedCompilation=false -p:RuntimeIdentifier=win-x64 -p:CopyDebugSymbolFilesFromPackages=false`.

**Limits/activity:** Legacy split folders have no ownership manifest; collisions are detected against the currently discovered incoming sources. Historical ownership cannot be established if a prior source has been removed or replaced. Changing retailer or source filename changes completion identity. Initial isolated builds exhausted disk space copying native symbols; only this mission's generated build output was removed before retrying with x64 output and package debug symbols disabled. Operator PDFs, artwork and database were not opened for writes by validation. Tests used synthetic PDFs and disposable SQLite files. No SQL Server calls/writes, paid calls, Azure, deployment or Git operations.

---


## Previous Mission Report - 5 November 2026: Mission 009F - Non-blocking Crater Generation

**Disposition:** Crater It now runs per product without acquiring the global processing gate. Offers remain scrollable, only that product's Crater button is disabled, independent products can queue, and at most two provider requests run concurrently per Reviewer process. Per-card Queued / Generating / Complete / Failed / Cancelled feedback and a per-card Cancel image action replace global one-click Crater progress. The activity panel was not moved. No automatic Crater during Get Data was added.

### Findings and implementation

The old handler called BeginProcessing, which explicitly disabled ProductList along with all selection/action controls. This was the direct cause of scrolling being blocked during generation. Async generation alone did not avoid dispatcher work: synchronous provider setup/response validation and archive/legacy-image preparation could run on WPF continuations.

A reference-keyed active-job registry now prevents duplicate requests for the same displayed product. A process-wide SemaphoreSlim limits image provider calls to two, including the separate Edit Image provider path. Waiting jobs are cancellable before dispatch. Provider setup, generation, validation, archive writes and acceptance preparation run off the UI thread. Final working-image assignment is dispatched to WPF and rechecks closed/changed state. The existing archive workflow retains one-click provenance, immutable files, stable artwork identity and explicit Save persistence; optional deferred assignment avoids publishing a working reference from a worker thread. Runtime card status/busy fields are excluded from JSON.

Selection/product removal cancels jobs for product objects that leave the displayed collection. Close cancels the lifetime token. Results target the captured product object, never a selected row or list index. Cancellation closes the workflow so in-flight acceptance cannot publish a late result. Completed output returned after local cancellation is archived without assignment; no collateral cleanup or overwriting was added. A failed product can retry; previous working images survive failure/cancellation. Remote provider cancellation remains uncertain. An active provider that ignores cancellation retains its request slot and duplicate guard until it returns; cancelled queued jobs do not contact the provider.

### Global safety and Save semantics

Split, Get Data, Save and Commit keep the existing ProcessingGate and their existing disabled-control/transaction behavior. Crater cannot start while that gate is active. Global operations refuse to start while card jobs remain pending, with a visible instruction to finish/cancel those jobs first. These operations are not automatically queued. This prevents SQLite serialization or SQL Commit from racing a subsequent image assignment. Completed working images are still unsaved until explicit Save; actual Save/reload checks preserve image path and ArtworkId. Image archive/acceptance operations using the global gate retain the same exclusion. No SQL or SQLite transaction/repository implementation changed.

### Focused validation

**154 checks passed:** 76 mocked workflow/progress/data-quality checks, 60 existing offline image-management checks and 14 transaction-safety checks. Builds succeeded. A clean build reported only the two pre-existing unused-field warnings in Shared/Pages/Offers.razor.

Actual WPF controls, bound templates, dispatcher and handlers verified list scrolling while requests were pending; other cards enabled; duplicate same-card invocation rejected; two simultaneous requests; a third queued request starting when a slot frees; peak provider concurrency exactly two; dispatcher heartbeat during deliberately synchronous provider setup; per-card bound status; independent failure/retry; queued cancellation with no provider call; active cancellation/late collateral retention; Save excluded while jobs run and successful exact-draft Save/reload afterward; stale selection/replacement safety; and window-close late-response suppression. Existing image archive/accept/reject/removal and uncertain-commit protection checks remained green. All generators were mocks; SQLite data was disposable fixture data only.

Evidence: `.artifacts/mission009f-checks.txt`, `.artifacts/mission009f-image-checks.txt`, `.artifacts/mission009f-safety.txt`, initial clean-build `.artifacts/mission009f-build.txt`. Final workflow fixture: `.artifacts/reviewer-workflow/ab6619334da44008831b5a435182f9ff`; image-management fixture: `.artifacts/reviewer-image-management/774b8a4b8d1e42f192fe6f9d330b2cc9`. Reproduce with `dotnet run --project tests/ReviewerWorkflow.Checks --artifacts-path .artifacts/mission009f-build -p:NuGetAudit=false -p:UseSharedCompilation=false`.

### Limits, decisions and activity

No paid-provider latency/remote-cancellation behavior or mouse-driven desktop interaction is claimed; WPF interaction was automated through real controls and dispatcher events. Queue and card feedback are session-only; changing selection cancels the prior cards rather than retaining an unseen editable batch. No further operator approval is needed for the completed local implementation. Any real provider smoke test remains separately authorized work.

No SQL Server connections/writes, paid API calls, Azure changes or Git operations. Existing operator extractions and artwork were not modified; tests wrote only synthetic PDF/JPEGs and disposable SQLite/archive fixtures. PDF fitting, responsive thumbnail sizing, extraction rules and database transaction implementation were unchanged.

---

## Previous Mission Report - 5 November 2026: Mission 009E - PDF Viewer Responsive Sizing

**Disposition:** Implemented in the WPF Page Review viewer. Reviewer column layout, product cards, extraction and Crater workflows were not changed.

### Final visual polish - 5 November 2026

**Result:** Replaced the 140x140 thumbnail with a responsive ProductThumbnail control. It measures against the offer card's available inner space, centers with a five-DIP margin, preserves aspect ratio, and caps each dimension at the source bitmap's pixel count divided by the current display scale. Small images therefore stay at natural display resolution; sufficiently large images fill the card width. No column/card widening, source-image rewriting, generation or image-action changes.

**Fit Width correction:** The earlier PDF-point/DPI calculation did not establish the width actually rendered by Syncfusion for every document and is now removed. Fit Width explicitly locates the viewer's native PART_ScrollViewer and derives the next zoom from current zoom * (visible viewport width - 16 DIP margin) / rendered extent width. The two measured widths share a coordinate system, including the SDK's raster scaling and scrollbar space. Extent changes as well as viewport/layout/DPI changes trigger fitting while Fit Width mode is active. There is no hard-coded 34% value or DPI multiplier. A small integer-zoom tolerance avoids layout-rounding oscillation.

The SDK kept a horizontal scrollbar visible independently of page overflow. Fit Width mode now suppresses that scrollbar; manual zoom restores it. Tests independently verify the rendered extent fits with margin, so hiding the scrollbar is not used as evidence that clipping was fixed. Manual zoom cancels queued fitting and display refresh preserves manual zoom/scrolling. Initial document fitting and existing active Fit Width resizing remain automatic.

**Focused WPF validation: 103 checks passed** (71 Saved/PDF and 32 window/thumbnail). Actual Fit Width button events after 59%, 34% and 250% verify both rendered width <= viewport minus margin and no horizontal scrollbar. Portrait/landscape PDFs nested inside an outer ScrollViewer pass at layout scales 1/1.25/2, with resize, manual zoom preservation, actual host DPI 150%, and existing simulated DPI notifications. One landscape result: 27% zoom, 648.00 rendered width, 670.67 viewport width; the target percentage is derived from geometry, not fixed. Thumbnail checks verify full inner-card width for a 1200x600 source, 2:1 aspect ratio, resize responsiveness, unchanged card width, centering, natural-size cap for a 16x16 source, and available existing image actions. Startup/maximize/restore checks remain green.

Evidence: `.artifacts/mission009e-polish-pdf.txt` (fixture `.artifacts/reviewer-saved-preview/6aa9895234fc4f92a6049733d6705f21`) and `.artifacts/mission009e-polish-thumbnails.txt` (fixture `.artifacts/reviewer-workflow/33e0a54e3e5849258b59a77ab61f6922`). Builds succeeded; only the existing two unused-field warnings in Offers.razor appeared during the clean build. This section supersedes the earlier fixed-thumbnail and PDF-point/DPI fitting descriptions below.

**Limits/activity:** Actual WPF dispatcher/control/button tests were run, not mouse-driven desktop inspection. Layout scales and synthetic DPI notifications do not replace physical monitor/DPI transfers. Operator verification remains needed on the real catalogue and the 1080p/4K monitors with a newly built Reviewer. No SQL connections/writes, paid calls, Azure or Git operations; only synthetic PDF/image and disposable SQLite fixture data. No extraction, database, Crater generation or startup-bounds implementation changes in this polish pass.

### Finishing corrections - 5 November 2026

**Result:** Startup now clamps both the native window size and position to the selected monitor's working area, keeping the entire window and title bar visible. The same guard runs after returning to Normal state and remains in the existing DPI-change path. Maximized windows are left maximized. Native monitor/window rectangles are compared in physical pixels, preserving negative monitor origins and avoiding mixed-monitor global-coordinate DPI conversion. The previous guard only reduced width/height and did not run at startup.

Product thumbnails now occupy a centered 140x140 logical-pixel area with Stretch=Uniform, restoring a slightly larger presentation while preserving aspect ratio. Existing preview click, Edit Image and Remove / Archive actions remain unchanged. No PDF fitting, extraction, database or Crater generation implementation was modified.

**Focused validation:** Build and 27 checks passed using the actual WPF MainWindow and the same PerMonitorV2 manifest as the Reviewer. Tests cover simulated 1920/3840 pixel work areas at 100/150/200% scales, negative monitor origins, oversized/off-screen bounds, actual off-screen startup recovery, maximize/restore with normal size preserved, display-change bounds correction, actual thumbnail centering, aspect-preserving presentation and available image actions. No generation calls occurred. Evidence: `.artifacts/mission009e-finishing-checks.txt`; fixture `.artifacts/reviewer-workflow/b62e0e25d01e4d94a08a7ab7076aedab`. Reproduce with `dotnet run --project tests/ReviewerWorkflow.Checks --artifacts-path .artifacts/mission009e-finishing-build -p:NuGetAudit=false -p:UseSharedCompilation=false -- --layout-only`.

**Limits and activity:** Physical monitor transfer between the operator's 1080p and 4K/DPI configurations still requires manual verification. No new blockers or operator approval required for these local corrections. No production records, SQL connections/writes, paid calls, Azure or Git operations. Tests created only synthetic images/PDFs and disposable SQLite fixture data; generation, extraction and image-action handlers were not invoked by these focused checks.

### Changes and findings

The previous Syncfusion FitWidth mode retained a stale scale after resizing in the real WPF fixture. Fit width now calculates an integer zoom from the native PDF ScrollViewer's current visible viewport, excluding the scrollbar and reserving a small page gutter. It uses the loaded split page's width and compensates once for Syncfusion 35.1.37's DPI-scaled raster coordinates; at the desktop's actual 150% scaling, ignoring this factor reproduced horizontal overflow. No screen-resolution constants are used.

Fit requests are deferred until after WPF layout and superseded requests are discarded. Viewer load, document load, control resize, scrollbar viewport changes and DPI notifications trigger fitting. Existing MainWindow resize/DPI handling remains in place for maximize/restore and monitor transitions. The Windows executable now explicitly declares PerMonitorV2 awareness, with PerMonitor fallback. Manual +/- zoom cancels queued fitting; display refresh no longer reapplies ZoomTo in manual mode, preserving zoom and scroll position. Fit width explicitly resumes automatic fitting. No document reload or source-PDF write is required.

### Verification

Build and **40 offline actual-WPF checks passed**, including small (1100x650) and large (1900x1000) logical windows, grow/shrink fitting, maximize/restore, manual 175% zoom through resize/display refresh, vertical scrolling preserved, Fit width after manual zoom, and no horizontal overflow after fitting. DPI notification handlers were simulated at 100% -> 200% and 200% -> 125%; these exercise notification handling, not physical DPI transitions. The host desktop was actually 150% DPI. Existing Saved/Committed/Recovery guards, exact draft round trips, fake extraction, splitter, thumbnail rendering, missing/corrupt PDF recovery and unchanged PDF bytes also passed.

Evidence: `.artifacts/mission009e-final.txt`; final disposable fixture `.artifacts/reviewer-saved-preview/f06248b9c92843b7ad3eca23be23b09b`. Two pre-existing unused-field warnings in Shared/Pages/Offers.razor remain. Initial default-output build was blocked by the operator's running Reviewer; validation used isolated artifact build output without closing that application.

Desktop computer-use launch approval timed out, and separately launched UI fixtures were not exposed as targetable windows. Therefore no mouse-driven desktop verification is claimed. The fixtures were stopped afterward; the operator's Reviewer was left running. Actual WPF controls, layout, events and handlers were exercised by the automated dispatcher host.

### Remaining testing and operator decisions

Operator testing remains necessary on the physical 1920x1080 and 3840x2160 monitors: move the newly built app both ways at their configured Windows scaling, maximize/restore, resize, scroll at manual zoom, then select Fit width. Confirm page edges remain visible and scale adapts without disrupting cards. Simulated notifications cannot establish native raster refresh or Windows transition behavior on those monitors. No additional approval is needed for the completed local changes.

**Activity:** No SQL reads/writes, paid calls, Azure operations or Git operations. Only synthetic PDF/disposable SQLite fixtures and local build artifacts were written; existing operator extraction data and artwork were not modified.

---

## Previous Mission - 5 November 2026: Mission 009D - Crater Workflow, Progress & Data Quality

**Disposition:** Implemented in the WPF Reviewer only. One-click Crater It generates, archives and assigns a working image directly; Save persists the path and stable ArtworkId to SQLite. Edit Image remains a separate current/proposed workflow with optional instructions and explicit acceptance. Reject/close never accepts. Remove / Archive detaches only the working reference; Save persists that detachment. Customer-facing Crater It was not modified.

**Repeat-request verification - 5 November 2026:** Confirmed the completed implementation and reran all 52 focused offline workflow checks successfully. No implementation changes or external-service calls. Evidence: .artifacts/mission009d-recheck.txt; fixture .artifacts/reviewer-workflow/31428217267c43c4bf05f171c0c6ec85. The mixed-monitor physical verification limitation below remains open.

### Artwork and cancellation

ArtworkArchive uses friendly product/variant/size/UTC-date/GUID filenames, stable artwork IDs, SHA256, immutable JSON provenance and append-only lifecycle events. Existing artwork is never overwritten or deleted. Legacy files retain their original paths and acquire provenance when needed. Rejected proposals and completed late results are archived. This intentionally supersedes the earlier memory-only proposal policy. If cancellation prevents receipt of provider bytes, the application cannot retrieve that remote collateral. Local cancellation cannot guarantee remote provider cancellation or avoid provider charges. No provider calls were made to verify remote behavior.

Generation uses the shared gate, linked cancellation and captured operation tickets. Late callbacks cannot assign after close/cancel or overwrite a subsequent operation's status. Save, Split, Commit and acceptance remain non-cancellable. Working image assignment is reported separately from SQLite persistence. A small thumbnail opens the existing preview.

### Progress, layout and normalization

The existing information space now shows a compact state-colored operation name, animated indeterminate indicator, elapsed time, readable status, terminal timestamp and summary. Recent activity is bounded to eight completed operations for the current session. Split rasterization/extraction preparation run off the dispatcher. Success follows actual persistence; known persisted work with a subsequent refresh failure is explicitly reported as saved with a refresh issue. SQL completion with failed SQLite acknowledgement stays Recovery Required and cannot retry automatically.

Responsive column widths preserve the product-card and PDF viewports. DPI changes constrain oversized windows to the current monitor work area and refresh the embedded viewer/Fit Width without replacing the document. Actual WPF resizing and logical widths 640/800/1024/1400 were tested. Physical transfer between monitors of different DPI/resolution remains unverified and requires operator testing before declaring that scenario resolved.

Fresh extraction normalization requires explicit package-size evidence for recognized fresh meat/poultry/deli products. Price, unit price and saving fields cannot supply package evidence. A printed size can coexist with a price in the same evidence. Saved/reloaded drafts are never normalized, and manual inline pack-size corrections are marked and preserved. AI output cannot set that manual marker.

### Verification and evidence

Build succeeded with zero warnings/errors. **430 checks passed:** 52 new workflow/progress/data-quality checks, 114 Classic, 96 editing, 14 safety, 60 image management, 29 Saved/PDF, 21 recovery/layout, and 44 SQL integration checks (22 legacy plus 22 explicit transaction/recovery). SQL tests used only the hard-coded disposable LocalDB database ReviewerSafety_Disposable_20261105 on (localdb)\ReviewerSafetyValidation. Logs are .artifacts/mission009d-*.txt; latest new fixture is .artifacts/reviewer-workflow/726ecdaa3fc7427b98a12d5a9c1fc113.

The new checks cover required meat examples, genuine weight alongside price, per-unit denominators, manual edit reload, direct assignment, SQLite artwork identity, immutable archive metadata, accept/reject, legacy preservation, provider failure, cancellation/close after output, stale status callbacks, concurrent/repeated gate entry, real WPF Save/Cancel/Remove handlers, thumbnail binding and resize. Fake providers and temporary SQLite/PDF/image fixtures only. Existing image-action label expectation changed to Crater It! to reflect the separate Edit Image action.

Desktop interaction with MISSION 009D OFFLINE FIXTURE confirmed compact running progress/elapsed time, gated controls, successful automatic assignment and thumbnail without a preliminary dialog, current/proposed editor, closing without acceptance, and Cancel preserving the current image with the provider-uncertainty explanation. Fixture was closed. Automated actual-WPF checks verified persistence/reload and archive removal. No real generated-image quality claim is made from synthetic JPEGs.

### Remaining decisions and safety

Operator: verify physical mixed-monitor PDF behavior; separately authorize any desired paid-provider smoke test or live SQL use. No approval is needed for these completed local changes. Remote cancellation remains uncertain; recent activity is session-only. Production SQL/SQLite records were not changed or reset. No paid image/extraction calls, Azure operations, Git operations, migrations, data corrections, destructive artwork cleanup or automatic uncertain retries occurred.



## Previous Mission - 5 November 2026: Mission 009C - Saved Extraction Editing & PDF Preview

**Disposition:** Saved SQLite drafts can be edited and saved by exact extraction ID. Committed SQL correction remains unsupported; Committed and Recovery views are now explicitly inspection-only. The selected split PDF is embedded beside the product cards with zoom, Fit width and scrolling. No recommit-by-insert, SQL updates, migrations or recovery retries were added.

### Part A - Audit performed before implementation

The audit inspected actual selection handlers, product-card bindings, SQLite persistence, the Commit handler and the SQL repositories. Existing operator records and production SQL were not opened; verification used synthetic Saved, Committed and Committing fixtures through the actual WPF handlers.

Before this mission, selecting either Saved or Committed restored that exact extraction ID's JSON into the same editable cards. Saved Save updated its selected ID. Committed cards appeared editable and Save was enabled, but SQLite update APIs accept only Status=Saved. The Save handler could fall through to a fresh-page save using a previously selected flyer/page. The delete handler also depended on _selectedPage.ExtractionRunId rather than the selected saved batch, making a stale context unsafe. Page selection changed only the WebView PDF source, leaving unrelated saved/card context present.

| Fields or actions | Saved SQLite draft | Committed SQL record |
| --- | --- | --- |
| Name, brand, variant, pack size | Inline editable; persisted by Save | Inspection only; no supported update |
| Advertised price, regular price, saving, unit price, offer quantity, promotion text | Inline editable; persisted by Save | Inspection only; no supported update |
| Category / primary category / category list | Category dialog; persisted by Save | Inspection only |
| Promotion classifications | Promotion Types dialog; persisted by Save | Inspection only |
| Accepted/removed image reference | Classic image actions, then Save | Preview only; no correction or upload action |
| Calculated regular price | Derived display, recalculated by sanitizer | Not an edit/update control |
| Barcode / existing metadata | Preserved by JSON round trip; no barcode editor | No correction control |
| Flyer/retailer/page identity, dates, SQL IDs | No editable controls | No correction controls |

Saved drafts may remain incomplete; existing pre-Commit validation still applies. The draft's JSON is serialized as a complete batch, not individual SQL updates. Delete remains a confirmed immediate SQLite draft update and now uses the selected Saved extraction ID; it is not a SQL delete.

**SQL identity trace:** ProductRepository.AddProductAsync returns OUTPUT INSERTED.ID. Commit uses that temporary ProductID for category links and Offer.ProductID. OfferRepository.AddOfferAsync also returns OUTPUT INSERTED.ID, but the Commit handler discards the returned OfferID. Neither SQL ID is assigned back to ExtractedProduct or persisted in ExtractionRuns JSON. SavedExtraction.Id is a SQLite ExtractionRuns.ID, not a SQL ProductID/OfferID. SQL SourceDescription contains only a page filename, so it cannot replace durable per-record linkage. Matching by names/pages/prices would not be a safe correction mechanism.

Safe committed updates are therefore **not supported**. They would require a separately designed durable SQL-ID mapping, review/concurrency rules and a correction workflow. No migration, matching heuristic, SQL UPDATE or insert-based recommit was implemented. The audit also found that OfferQuantity round-trips in SQLite but the existing Commit does not assign it to SQL MultiBuyQuantity; validity dates are generated by the existing Commit rather than edited in these cards. Those existing SQL mapping limitations were documented, not changed.

### Editing/context safety changes

- Committed and Committing selections disable Save, Commit and Add Offer, display an inspection-only explanation, and block edit/category/promotion/delete/image-mutation handlers. Existing image previews remain available. The Save handler also guards against direct/programmatic invocation falling into fresh insertion.
- Saved selection clears unrelated page context and loads its own split PDF using the saved flyer/page filenames. Save and Add Offer stay disabled until its exact JSON is restored. Existing draft inline editing, categories, promotions, image management and validation remain available.
- Selecting a new unsaved page clears stale saved selection and product cards. Selecting a page with an existing extraction selects that exact batch and retains page selection. Get Data stays available for unsaved pages and is hidden when viewing persisted batches; there is no automatic provider call or retry.
- Saved-list refresh restores selection by extraction ID. Draft deletion takes the selected Saved ID before considering any page ID. The split-root constructor dependency allows fixture PDF paths without accessing operator data; production defaults are unchanged.

### Part B - Embedded PDF review

The local NuGet cache contains Syncfusion.PdfViewer.WPF **35.1.37**. Its configured workstation WPF licence validated successfully using the SDK's local validation API; no licence value was printed or changed. The existing project had Syncfusion PDF processing and WebView2, but had not referenced the WPF viewer.

Added an embedded PdfPageReview control using Syncfusion PdfDocumentView with a compact +/- zoom toolbar, Fit width, page filename, status/errors and native vertical/horizontal scrollbars. It occupies the existing Page Review grid column; product cards remain visible. PDFs load from read-only in-memory copies, with no PDF save UI or annotation toolbar exposed. Text entry and the viewer context menu are suppressed. Missing/corrupt files clear the prior preview and report the problem while preserving product cards.

The viewer's WPF PDF assembly conflicts with the older portable PDF types used by splitting. Its package is referenced under the ViewerPdf assembly alias; the existing Syncfusion.Pdf.Net.Core **34.2.5** remains in place for splitting and existing document handling. This avoids rewriting the splitter or changing customer-facing package references. The packages were already cached. New Syncfusion telemetry is disabled before licence/control initialization. No SDK telemetry/cloud resource setting outside this process was changed.

Initial fit and refitting after document changes are applied when the control/document is loaded or resized. Explicit zoom overrides automatic width fitting until Fit width is selected again. Source PDFs are never saved or overwritten. The old WebView placeholder was replaced; the WebView2 dependency itself remains untouched.

Reference: [Syncfusion WPF viewer setup and dependencies](https://help.syncfusion.com/document-processing/pdf/pdf-viewer/wpf/getting-started).

### Part C - Verification

| Suite | Result |
| --- | --- |
| New ReviewerSavedPreview.Checks | 29 passed |
| Existing ReviewerEditing.Checks | 96 passed |
| Existing ReviewerSafety.Checks | 14 passed |
| Existing ReviewerImageManagement.Checks | 60 passed |
| Existing ReviewerClassic.Checks | 114 passed, fake transports only |
| Existing recovery/layout checks | 21 passed |
| Total automated checks | 334 passed |

New checks cover exact-ID Saved selection; identity/offer/classification metadata Save/reload; Committed and Recovery inspection guards; zero provider calls while reviewing persisted batches; stale context clearing; explicit fake extraction on a fresh page; reopening a saved page without losing selection; PDF load, initial Fit width, refitting another document, 200% zoom and fit; cards beside review; missing/corrupt file recovery; unchanged source bytes; and existing splitter plus PDFtoImage thumbnail rendering after the native viewer has loaded. The latter checks specifically guard the mixed PDF-library/native dependency integration.

Builds succeeded. The initial headless test host lacked WPF's DispatcherSynchronizationContext, exposing the viewer's asynchronous collection requirement; the host was corrected to run with the same UI synchronization context as a normal WPF application. Subsequent runs passed without suppressing dispatcher exceptions. A timing-sensitive fit assertion also led to explicit load/resize fitting and bounded dispatcher-aware waits in the tests.

Actual WPF desktop verification used a synthetic three-page PDF and isolated SQLite. The fixture clears SQL, blob and OpenAI credentials in its own process and injects a fake extraction provider. At the default 1400-by-800 window size the selected PDF rendered in Page Review beside editable cards. Two zoom clicks changed 21% to 71%; scrolling moved the PDF from its heading into lower content while cards stayed visible. Selecting Committed switched to page-002.pdf and displayed inspection-only status with Save/Commit disabled. Clicking its price did not enter edit mode. The fixture window was closed afterward. No real catalogue, paid generation/extraction or SQL connection was used.

Evidence: .artifacts/mission009c-checks.txt, mission009c-ui-output.txt, mission009c-ui-error.txt and mission009c-Reviewer*.txt. PDF/SQLite fixtures are retained under .artifacts/reviewer-saved-preview. Reproduction details are in tests/ReviewerSavedPreview.Checks/README.md. SQL integration/write suites were not rerun because no SQL repository/transaction implementation changed in this mission.

### Remaining unsupported work and approvals

- Durable ProductID/OfferID linkage and safe committed corrections remain unsupported. No recommit/reset/retry action is provided. This requires a separately authorised design; a schema migration or production correction is outside this mission.
- Saved PDF review requires the original split page to remain in the local split folder. A missing page produces a visible unavailable message; the app does not reconstruct it using paid extraction or change the saved batch.
- The viewer is for review, not PDF editing/saving. Text-only/local fixture rendering and integration were verified; exhaustive testing of every real catalogue, encrypted PDF or unusual PDF feature was not performed.
- No operator approval is needed for the completed local changes. Any future SQL mapping migration, live SQL write or committed correction needs separate operator authorisation. No new viewer installation/purchase or licence change was needed here.

**Activity:** no production SQL reads/writes, migrations, existing-record corrections, uncertain/committed retries, paid API calls, Azure changes/deployments or Git operations. Only synthetic PDF/image and disposable SQLite fixtures were created/updated. Existing operator PDFs, images and extractions were not modified. Customer-facing Crater It was not changed.

---


## Previous Mission - 5 November 2026: Mission 009B - Classic Crater It Image Management

**Scope:** WPF Reviewer Classic image management only. Customer-facing Crater It was not modified. Earlier commit-safety and offer-count investigations were not repeated. This mission opened no production SQL connection and did not alter existing operator SQLite drafts or image-library files.

### What changed

- Product cards now offer Remove / Reject Image. It clears only ImageFilePath on the displayed product. Product/offer fields and collection membership remain intact. It does not delete local images or Azure blobs. Existing Save persists the cleared reference; there is no automatic database write.
- The existing Crater It action becomes Re-Crater when an image reference is present. It opens a dedicated image-review window with current and proposed previews, optional multiline instructions, Generate Proposal, Cancel Generation, Accept Image, and Reject / Keep Current. Opening the window does not contact a provider. Only the explicit Generate action invokes the existing Classic service.
- ClassicImageProposal holds generated/validated JPEG bytes in memory. Successful generation does not change the approved ImageFilePath or write a proposed image file. Accept Image explicitly saves a new uniquely named JPEG and then changes the reference. Save persists this accepted reference using the existing SQLite workflow. The original image file is kept.
- Reject, window close, cancellation, generation failure and invalid output preserve the original reference. A stale proposal cannot overwrite a reference changed while it was open. Duplicate generation/acceptance is blocked. A cancellation or close while acceptance is writing leaves the approved reference unchanged; an already written unattached new file is retained, not deleted. No cancellation check can turn an already completed acceptance into a reported cancellation.
- Product image-reference changes notify the preview, Remove-button eligibility and Crater/Re-Crater label. Existing click-to-preview code and inline editor bindings remain intact.
- ProductImageService retains the existing product facts and appends optional operator instructions. Fresh-produce prompts name the actual produce/variety and natural form. Loose produce omits supplied brand/weight text and forbids invented packaging, branding, weights, stickers, labels or printed text. Explicit packaging, such as a named punnet, is grounded in the supplied pack data. Processed products and non-produce categories are excluded from produce-specific classification. A final conditional fresh-produce/loose-produce accuracy guard also covers uncommon names not recognised by the classifier. Produce constraints follow operator instructions so contradictory packaging instructions do not override them.
- The existing Classic test project needed its linked ProductValidationService dependency added because its linked ExtractedProduct model now depends on that service. Its product-facts assertion was updated for the intentional accuracy guard; transport/model/error-handling checks remain intact.

### Tests and results

| Suite | Result |
| --- | --- |
| New ReviewerImageManagement.Checks | 60 passed |
| Existing ReviewerClassic.Checks | 114 passed, fake HTTP transports only |
| Existing ReviewerEditing.Checks | 96 passed |
| Existing ReviewerSafety.Checks | 14 passed |
| Existing WPF recovery/layout checks | 21 passed |
| Total automated checks | 305 passed |

Focused coverage: instructions appended/blank handling; seven fresh-produce examples and explicit punnet data; uncommon produce safeguard; processed-food and meat false positives; removal without product/offer deletion; reference notifications; removed/accepted references through actual SQLite Save/reload; unchanged original while generating; rejection; duplicate requests; cancelled/pre-cancelled generation; late provider results after cancellation/close; provider failure without retry; invalid JPEG; explicit acceptance and preserved original bytes; repeat acceptance; stale reference protection; disk-save failure; cancellation/close while acceptance is pending; retained unattached new files. SQLite and image fixtures are disposable synthetic data and are retained under .artifacts/reviewer-image-management.

Builds succeeded. One stale WPF generated-file build failure was resolved by rebuilding generated artifacts; no source workaround was required. A dependency rebuild reported two existing unused-field warnings in shared Offers.razor; no customer-facing source was edited. Existing safety-project NuGet vulnerability lookup remained unavailable under the restricted network. Classic negative JPEG tests emitted expected codec diagnostics and passed.

### Actual WPF window verification

The computer-use skill inspected the real ClassicImageEditorWindow using an injected fake generator and synthetic JPEG samples. No real provider client was used by these windows.

- Both current and proposed previews, instructions and all action controls fit at the default 900-by-700 size. Accept was disabled before a valid proposal existed.
- Generation displayed a proposed sample alongside the unchanged original and then enabled Accept. Optional instructions accepted text input.
- Reject / Keep Current closed the first fixture with ImageFilePath still original.jpg. Its folder contained one JPEG: the original.
- Accept Image closed the second fixture with a new JPEG reference. Its folder contained two JPEGs, including the unchanged original. Both fixture windows were confirmed closed.

Evidence: .artifacts/mission009b-image-checks.txt, .artifacts/mission009b-ui-output.txt, .artifacts/mission009b-accept-ui-output.txt. Synthetic UI fixtures: ui-3b3129594fd5468db64f765c4c576860 (rejected), ui-e704333beb2946939c7b49d7090c4c69 (accepted). Reproduction instructions are in tests/ReviewerImageManagement.Checks/README.md.

### Problems, limits and operator decisions

No implementation blocker remains from these offline checks. Actual paid-provider image quality was intentionally not evaluated. Prompt constraints and approval mechanics were verified; they do not constitute a guarantee that a real model follows every visual constraint. Explicit operator acceptance remains required for every generated image, including first images.

Acceptance/removal changes the current draft model; the operator still uses Save to persist it. Rejected proposals are discarded from memory. A cancellation after a new file was written but before acceptance can leave an unattached file for later inspection; this feature never cleans up existing images automatically.

No operator approval is needed for the completed local implementation/tests. No paid generation test, production correction or cloud action is requested or authorised here. Existing commit-recovery restrictions from Mission 009A remain unchanged.

**Activity:** no production SQL writes or connections, production record corrections, existing operator extraction changes, existing image-file deletions, real paid API calls, Azure blob/resource changes, deployments or Git operations. Only disposable SQLite and synthetic local test images were created/updated. The customer-facing Crater It implementation was not modified.

---


## Previous Mission - 5 November 2026: Mission 009A - Reviewer Recovery & UI Safety

**Disposition:** the recovery-visibility and action-layout blockers are addressed. Reviewer batch writes now use one explicit SQL connection and local transaction, removing reliance on ambient enlistment and connection reuse. No production use, corrections, or retries were performed. Earlier offer-count investigations below are retained as history and were not repeated.

### Changes

- ExtractionReadService.GetAllAsync now includes Saved, Committing and Committed. SavedExtraction exposes a notifying StatusDisplay/RecoveryRequired state. Inactive Committing entries show **Recovery Required** in amber with an explanation that SQL outcome must be reviewed. The raw durable status remains Committing.
- A successful local claim marks that entry active in this window; it displays Committing until the operation finishes. On failure, durable states are refreshed and the entry becomes Recovery Required after the operation/error dialog ends. Process-local activity does not prove whether another Reviewer instance is still working; the tooltip explicitly warns about that possibility. There is no heartbeat, status reset, automatic retry or reconciliation.
- Recovery selection loads the existing products for inspection and disables Save and Commit. A handler-level Save guard also prevents a recovery entry falling through to the fresh-draft path. Normal Saved eligibility, incomplete draft saving, validation, inline editing and extraction cancellation are retained.
- New ReviewerSqlBatch owns exactly one SqlConnection and SqlTransaction for all product, product-category and offer writes. Repository overloads accept the shared connection/transaction; existing repository methods remain available and retain their ambient behavior for other callers.
- ReviewerSqlBatch uses a local ReadCommitted transaction, disables ambient enlistment on its own connection and rejects an unexpected ambient transaction before opening it. It performs no automatic open/write/commit retry. A write failure faults the batch, blocking a later partial Commit. Commit is attempted once; subsequent writes/Commit are refused. Uncommitted disposal rolls back the transaction. MainWindow acknowledges SQLite only after SQL Commit succeeds.
- Retailer/location lookups remain preflight reads before the SQLite claim. Blob upload behavior is unchanged: it is outside SQL atomicity and was not exercised. No schema change, distributed-transaction switch or machine setting was required.
- Cancel / Split / Get Data are vertically stacked, stretched to the page-review column and remain in their original panel. Their default-size clipping is removed; visibility, handlers and cancellation eligibility are unchanged.

The explicit transaction follows [Microsoft's local SQL transaction guidance](https://learn.microsoft.com/en-us/sql/connect/ado-net/local-transactions): commands share a connection/transaction, and uncommitted connection disposal rolls back. Network ambiguity during the Commit acknowledgement still requires recovery review.

### Tests and exact results

| Suite | Result |
| --- | --- |
| Existing integration regression suite | 22 passed |
| Existing ReviewerSafety.Checks | 14 passed |
| Existing ReviewerEditing.Checks | 96 passed, including actual XAML binding/SQLite round trips |
| New explicit SQL/recovery checks | 22 passed |
| New WPF recovery/layout checks | 21 passed |
| Total automated assertions/checks | 175 passed |

The existing 22-check suite still intentionally tests the older ambient repository path, including its forced-promotion rollback control. That expected control failure is not an unresolved promotion dependency in the new Reviewer path. The new explicit checks exercise the actual ReviewerSqlBatch used by MainWindow.

New SQL checks demonstrated: two-product/two-offer/two-category success; halfway application failure rolling back all three tables; a real SQL foreign-key error followed by refusal to commit a faulted batch; refusal of repeat Commit and post-Commit writes; rejection of ambient nesting; success with Pooling=False and Enlist=False; 12 competing commits producing one two-offer batch; and abrupt child-process exits before/after SQL completion, with the correct three-table outcome and durable recovery state. Every uncertain claim remained blocked after reopening. No retry or reset was used to make a test pass.

New WPF checks exercised actual MainWindow/XAML against a fresh SQLite fixture: all three durable statuses visible; dynamic active/recovery labels and property notifications; recovery selection loading exact products while disabling Save/Commit; service-level refusal of recovery draft update/replacement; reopened recovery remaining unchanged; normal Saved/Committed eligibility; missing-price validation retained; and all three action buttons fully inside the page-review border at the default 1400-by-800 window size without overlap.

Build succeeded with zero compilation errors. Single-node builds with shared compilation disabled avoided sandbox compiler-server access failures. The existing NuGet vulnerability lookup warning remains a network-environment limitation, not a test failure.

### Actual WPF window validation

Only isolated fixture SQLite and the named disposable LocalDB endpoint were used. Fake extraction was injected; no paid provider or image upload ran.

- Recovery entry visibly displayed in amber as Recovery Required; Save and Commit disabled on selection. The product cards remained inspectable.
- At the default window size all three page-action buttons were fully visible. Starting fake Get Data and using the now-unclipped Cancel control preserved both original cards and restored controls.
- The existing blank-price inline editor accepted $2.50 and cleared invalid state. Double-clicking Commit produced one confirmation and a successful explicit-transaction write of two offers. SQL readback for ui-009a-page.pdf was exactly two offers; the isolated SQLite batch became Committed.
- A CHECK constraint **only in the disposable database** rejected the second offer for ui-009a-error-page.pdf. The actual WPF handler displayed the SQL failure. The batch appeared as Committing while the operation/error dialog was active, then as Recovery Required after dismissal. Selecting it disabled Save/Commit. SQL readback found zero offers for the failed source and zero orphan Fixture milk/Fixture bread products. SQLite remained Committing; it was not reset or retried.
- Both fixture windows were closed afterward. Successful UI fixture: ui-b5d565820f234840a673f0d5b6a80815.db, batch 1 Committed. Failure fixture: ui-9d2e4f3baf76479b880d1e805db1ffc4.db, batch 1 Committing. Extra synthetic recovery entries in those files are deliberate test setup, not operator records.

Evidence: .artifacts/reviewer-commit-validation/mission009a-integration-results.txt, mission009a-recovery-layout-results.txt, mission009a-ui-status.txt, mission009a-ui-output.txt and mission009a-failure-ui-output.txt. Disposable fixture databases and their test-only constraint were retained for inspection. Reproduction commands are in tests/ReviewerCommitValidation.Checks/README.md.

### Remaining limits and operator decisions

- **No automatic recovery:** SQL completion and SQLite acknowledgement remain separate. Recovery Required means inspect the durable outcome, including possible work in another instance, before any manual action. This mission does not authorize resetting status, merging/deleting records or retrying an uncertain batch.
- **Validation boundary:** LocalDB fixture schema was used, not a full production-schema clone. Paid/blob workflows, connection loss specifically during SQL Commit acknowledgement and host/SQL-server power loss were not exercised. Process-crash boundaries and actual WPF SQL failure were exercised.
- **Operator approval:** no additional approval is required for the completed local changes/tests. Any live SQL write, production correction or recovery action remains a separate operator decision; none was attempted. No new UI or transaction-promotion blocker remains in the tested Reviewer path.
- **Activity:** no production SQL connection or production records were touched in Mission 009A. Existing operator SQLite extractions were not opened or changed. Only disposable SQL/SQLite fixtures were written. No paid API calls, Azure changes/deployments, Git operations or automatic uncertain-commit retries.

---

## Previous Mission - 5 November 2026: Commit safety validation

What changed: added a disposable LocalDB integration harness and WPF fixture; added explicit SQLite/extraction dependencies to MainWindow for testing while preserving its normal constructor behavior. Maintained this cumulative technical report. Reporting convention: every mission ends with a self-contained chat report covering changes, tests, problems, blockers, operator decisions and production/paid API/Azure/Git activity.

Tests: 22 SQL integration checks, 14 safety checks and 96 editing checks passed. Actual WPF checks confirmed missing-price highlighting, validation summary, price recovery, cancellation preserving cards, and a successful two-offer LocalDB Commit following a double-click. Twelve concurrent commit attempts produced one writer. Halfway failure rolled back products, offers and category links. Abrupt process exits before/after SQL completion preserved the correct SQL outcome and blocked uncertain retries.

New findings affecting the next decision: read-only SQL audit explains 33 current offers versus 23 products in the four current SQLite batches. Five Woolworths products each have three matching SQL entries, accounting for 10 extra current offers. The sequence supports repeated partial writes; the historical failure trigger remains unproven. No correction was made.

Remaining blockers: Committing batches are hidden from the saved list; forced ambient transaction promotion fails (with rollback verified); the default WPF action row clips Cancel/Get Data; uncertain commits have no automatic reconciliation. Full production-schema and network-loss-during-completion tests remain outside this validation.

Operator decisions: live SQL writes should remain on hold. The next implementation scope is recovery visibility, transaction handling and action-row layout. Any production duplicate correction is a separate decision after reviewing the evidence; no reset, deletion, merging or retry is authorized by this report.

Activity: production SQL was read using SELECT-only queries, and existing SQLite was read-only. No production records or existing extractions changed. Disposable LocalDB and fixture SQLite data were created and retained. No paid APIs, Azure deployment/resource changes or Git operations. The configured production SQL endpoint was queried read-only; its hosting provider was not established by this audit.


Disposition: stop before live SQL writes. LocalDB transaction and WPF checks passed, but recovery visibility and transaction-promotion handling remain unresolved. No production rows or existing SQLite batches were modified, reset, retried, deleted or merged. No paid calls, Azure deployment or Git operations were performed.

## Production read-only investigation: the 33 offers

The successful SELECT-only audit used the configured SQL endpoint, with application intent ReadOnly. Credentials and endpoint identifiers were not printed. A 5-second attempt timed out during login; a subsequent 30-second attempt succeeded. Counts use 5 November 2026 at midnight, matching the repository's date-only current-offer predicate.

| Count | Result |
| --- | ---: |
| All SQL offers | 94 |
| Current SQL offers | 33 |
| Previous SQL offers | 61 |
| Current Woolworths offers | 22 |
| Current Coles offers | 11 |
| Current products in SQLite batches 21-24 | 23 |
| SQL offers with missing advertised price | 0 |
| SQL products without an offer, across all history | 3 |

SQLite batch 22 is committed and contains six products. Five have three matching SQL entries each, using the same name, brand, variant, pack size, retailer, source page, price and validity dates (4-11 November 2026). The sixth, Rokeby Protein Drink, appears once. These five groups account for 10 additional current offers.

| Product | Candidate SQL offer IDs |
| --- | --- |
| Australian Navel Oranges | 153, 159, 171 |
| Chicken Garlic Kyly | 154, 160, 172 |
| Muscle Nation High Protein Bar | 155, 161, 173 |
| Zooper Dooper Ice Blocks | 156, 162, 174 |
| Vanish Gold Stain Remover Powder | 157, 163, 175 |

The pattern is consistent with two partial five-product writes followed by one successful six-product write. The old code independently inserted products and offers before acknowledging SQLite; missing price parsed to null, while the actual SQL Offers.AdvertisedPrice column is NOT NULL. A missing-price failure followed by retry is therefore a supported explanation, but historical error logs are required to prove that trigger. The SQL source description stores only page-002.pdf, not the full flyer identity or extraction-run ID, limiting definitive provenance. No correction has been made. Page/price counts alone were not treated as evidence of duplication.

## Connection and transaction inspection

Actual configured settings: Enlist=True; Pooling=True; MultipleActiveResultSets=False; Encrypt=True; TrustServerCertificate=False; SQL authentication (IntegratedSecurity=False). Provider: Microsoft.Data.SqlClient 6.1.1; target .NET 10 Windows.

MainWindow validates current cards, saves the exact SQLite batch, resolves retailer/location, atomically claims Saved -> Committing, then creates TransactionScope with async flow enabled. ProductRepository and OfferRepository open/close separate connections per call inside the scope. SQL completion occurs when the scope is completed and disposed; only afterward does SQLite become Committed. The UI processing lease prevents reentry and disables editing/selection during the operation.

SqlClient automatically enlists connections opened inside the ambient transaction when Enlist is enabled. Sequential pooled connections remained in a lightweight transaction during the successful fixture runs (distributed transaction ID was empty). A forced second, unpooled connection triggered promotion and failed with NotSupportedException: implicit distributed transactions have not been enabled. All SQL fixture writes rolled back in this failure test. No distributed-transaction switches or machine settings were changed. See [Microsoft's SqlClient transaction documentation](https://learn.microsoft.com/en-us/sql/connect/ado-net/system-transactions-integration-with-sql-server).

Blob upload is outside SQL atomicity. No upload occurred in these tests because fixture products have no image paths. SQL rollback cannot remove previously uploaded blobs. There is no SQL ingestion ledger keyed by SQLite extraction ID; uncertain outcomes must remain blocked until reviewed.

## Disposable SQL integration results

Isolated instance: (localdb)\ReviewerSafetyValidation, SQL Server LocalDB 17.0.4025.3. Database: ReviewerSafety_Disposable_20261105. The fixture schema supplies the columns, foreign keys and category mapping used by the real repositories; it is not a full production schema clone. SQL fixtures and unique SQLite files are retained for inspection under .artifacts/reviewer-commit-validation. Existing production connection settings are never used as the write destination by the integration harness.

ReviewerCommitValidation.Checks --integration: **22 checks passed**.

| Scenario | Exact result |
| --- | --- |
| Successful repository transaction | +2 products, +2 offers, +2 category links; Saved -> Committing -> Committed |
| Failure after first complete product/offer/category set | Zero net product, offer or category inserts; SQLite remains Committing |
| Repeat completed claim | Rejected before SQL writing |
| 12 concurrent commit attempts, separate SQLite connections | Exactly one writer; +2 offers; winner acknowledged Committed; other attempts rejected |
| SQL success with missing SQLite acknowledgement | +2 offers preserved; status Committing; fresh service rejected retry |
| Abrupt child-process exit before SQL completion | Exit code 23; zero net offers; status Committing; new service rejected retry |
| Abrupt child-process exit after SQL completion, before SQLite acknowledgement | Exit code 23; +2 offers preserved; status Committing; new service rejected retry |
| Forced transaction promotion failure | NotSupportedException; zero net offers |

Crash children terminate via Environment.Exit without disposing/completing their active transaction in the before-completion case. The parent process verifies durable SQLite status and SQL row counts through new connections. This tests application-process loss; it does not simulate host power failure or SQL Server crash recovery. The injected halfway failure exercises the actual repositories and the same ambient transaction pattern; it is not an injected failure inside the WPF handler itself.

Existing regression suites were rerun: ReviewerSafety.Checks **14 passed**; ReviewerEditing.Checks **96 passed**. Build succeeded. NuGet vulnerability lookup was unavailable under the restricted network; this did not prevent compilation or these checks.

## Actual WPF interface results

The actual MainWindow and handlers ran in an isolated fixture. A constructor overload permits explicitly supplied SQLite path and extraction delegate; the normal parameterless constructor retains the original paths and real extraction provider. This dependency injection was the only production-code change made for this validation. The fixture selected a separate SQLite database, set a process-local SQL connection to LocalDB, supplied two synthetic products, and used an in-process fake extraction delay. No paid extraction/image provider or blob upload was called.

- Missing price: invalid card had a red border/pale red background. Opening the existing blank-price click target showed a red price-editor border.
- Commit while price was blank: displayed "Product 1 (Fixture milk): Advertised price is required." before SQL confirmation/writing.
- Cancellation: started Get Data using the fake provider; competing actions were disabled; Cancel showed "Extraction cancelled. Previous products were preserved." Both original product cards remained and the controls recovered afterward.
- Missing-price recovery: entered $2.50 in the existing inline editor; invalid state cleared and the value persisted.
- Full WPF Commit: double-clicked Commit, received one confirmation, confirmed the disposable write, and received "SQL write succeeded. Products added: 2". Readback found exactly two ui-page.pdf offers: Fixture milk at 2.50 and Fixture bread at 3.00. SQLite UI fixture ui-1d3f575864b044bc838f0cc5e5f3f2d6.db is Committed with two products and a commit timestamp.
- Reopened the committed fixture: Commit stayed disabled. The durable claim tests independently rejected replay after completion and after uncertain outcomes.
- The fixture window was closed after validation. The initial computer-use launch approval timed out; launching the isolated fixture on the desktop through the approved execution path subsequently succeeded.

## Remaining blockers and limits

1. **Recovery visibility:** ExtractionReadService.GetAllAsync filters to Saved/Committed. Committing batches are hidden from the saved list. They are durably protected from retry, but the operator cannot inspect their uncertain state there. No extraction status was reset to work around this.
2. **Transaction promotion:** the sequential pooled path passed, but the forced promotion path is unsupported with current runtime settings. A single explicit SQL connection/transaction for the whole batch would remove dependence on ambient connection reuse. Do not enable distributed transactions blindly.
3. **Action-row layout:** at the default 1400-by-800 WPF size, the horizontal Cancel/Split/Get Data row is clipped. Cancellation succeeded through the visible Cancel region, but accessibility and discoverability need layout correction.
4. **Recovery procedure:** SQL and SQLite do not share an atomic transaction. A successful SQL write followed by a missing SQLite acknowledgement remains Committing, as designed. There is no automatic retry or automatic reconciliation; review SQL evidence before any manual status/data correction.
5. **Coverage boundaries:** no real paid extraction, image generation/upload, network-loss ambiguity during SQL completion, host power failure, or full production-schema integration test was performed. Normal WPF success was tested end to end; halfway failure and crashes were tested at the repository/transaction boundary.

Production SQLite SHA-256 at final inspection: D4E2EDD0EA5540ABFAF8142EF3C45AF84A4DD547240D1F66FA7DF2D96FA6AD87. The operator database was used only by read-only audit connections in this mission; the WPF fixture never opened it.
