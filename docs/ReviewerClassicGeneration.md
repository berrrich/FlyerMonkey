# Windows Reviewer Classic image generation

Stage 2 is implemented. Classic generation uses the workstation's OpenAI SDK
client directly. It does not call the published FlyerMonkey generation API.
No paid generation, deployment, SQL schema, production configuration or Crater
security changes were made. Existing Mission 010 working changes were preserved.

## Implementation and files

- Added FlyerMonkey.Reviewer.Windows/Services/ClassicImageGenerationService.cs.
  Uses the existing OpenAI 2.13.0 package, gpt-image-1, 1024x1024 JPEG and compression
  80. Reads FLYERMONKEY_OPENAI_KEY from the process environment on construction.
  Production uses one shared HttpClient with redirects disabled, SDK retry policy
  set to zero, SDK message logging and tracing disabled, and a three-minute timeout.
  Missing credentials, HTTP status, transport failures, timeout, cancellation and
  malformed responses have safe messages without raw provider exceptions/bodies.
- Removed Services/ImageGenerationApiService.cs, including its obsolete public
  Azure generation route. Extracted Stage 1 validation into
  Services/ClassicImageValidation.cs. It checks JPEG media type, signature, decoder,
  pixel decoding and dimensions. The SDK's HTTP JSON/base64 envelope is expected;
  the extracted bytes must actually decode as JPEG. JSON bytes cannot become a JPEG.
- Services/ClassicImageFileService.cs now calls the independent validator. It retains
  the Stage 1 GUID .jpg naming, folder convention, safe write errors and partial-file
  cleanup. Existing files and paths are not renamed or migrated.
- MainWindow.xaml.cs manually constructs the service inside its try block, consistent
  with this application's existing service composition (there is no DI container).
  The handler captures the selected ExtractedProduct, preserves the prompt builder,
  disables its button, awaits generation and local saving, then assigns only that
  product's ImageFilePath. Credentials need not exist until generation is requested.
  Window closure cancels in-flight generation without reopening an error dialog.
- ProductImageUploadService.cs retains Stage 1 safe Blob status/error-code reporting
  and the existing product-images upload behavior. Stage 2 does not alter this file,
  SQL commit, extraction, artwork storage, approval or API access checks.
- Updated tests/ReviewerClassic.Checks/{ReviewerClassic.Checks.csproj,Program.cs} to
  link the SDK adapter, validator, existing prompt builder/model and save/upload
  services. Its internal injection seam replaces credential lookup and HTTP transport
  only; it exercises the same production SDK settings and retry policy.

The provider request is POST https://api.openai.com/v1/images/generations, with the
existing Classic prompt and JPEG settings. No SQL ID or Crater preset/metadata is
needed. The SDK JSON response contains image bytes; validated JPEG bytes enter the
unchanged local association and later Blob/SQL commit workflow.

## Validation

86 offline checks passed. Checks cover exact original prompt construction and SDK
forwarding, fixed provider route, model/JPEG settings, successful bytes, missing
credentials, malformed/base64/empty provider responses, JSON rejection, local saving,
Blob status/code redaction, caller cancellation, pre-cancellation, timeout, and one
SDK attempt on 302/401/404/429/500/503 and transport errors. HTTP handlers are entirely
in memory; no live mode or paid requests exist. Native JPEG decoder warnings may
appear for deliberately corrupt synthetic bytes. The runner catches test failures
and exits nonzero instead of causing an unhandled dotnet exception dialog.

Commands (from repository root):

```powershell
dotnet run --project tests/ReviewerClassic.Checks/ReviewerClassic.Checks.csproj --no-restore --no-launch-profile -p:NuGetAudit=false -p:OutputPath=bin/Stage2 -nodeReuse:false
dotnet build FlyerMonkey.Reviewer.Windows/FlyerMonkey.Reviewer.Windows.csproj --no-restore -o FlyerMonkey.Reviewer.Windows/bin/Stage2Verification -p:NuGetAudit=false -nodeReuse:false
```

Reviewer build passed with zero warnings/errors. Output is isolated from the
operator's running normal build. No obsolete Azure generation route/client remains
in Reviewer .cs/.xaml source. Prompt/extraction and Blob/SQL code are unchanged by
Stage 2. UI association and composition were inspected in source; interactive
catalogue extraction was not exercised, as it invokes a paid provider.

## Process/exception investigation

Read-only process inspection initially found the Reviewer running and a dotnet
process. The previous normal build explicitly failed copying the running Reviewer's
locked executable. An isolated output build succeeds without stopping it. A later
process query classified two MSBuild workers and one other dotnet process whose
role was not established; there is no evidence that those workers crashed. No
process was terminated. Application event-log inspection was unavailable.

The earlier Stage 1 test attempt did have an unhandled assertion after a malformed
JPEG decoder exception. That could account for a dotnet.exe exception dialog, but
without the operator's exact stack/event it cannot be conclusively matched. The
runner now handles failures and all current tests pass. Node reuse is disabled in
the validation commands; do not kill unrelated dotnet processes to clear a lock.

## One operator-controlled live generation smoke test

This test makes ONE paid generation request, initiated by the operator. It was NOT
run by automated validation. It does not include SQL commit or Blob upload.

1. Save work and close any older Reviewer instance normally. Ensure the workstation
   environment already contains FLYERMONKEY_OPENAI_KEY and the existing Reviewer
   prerequisites (including FLYERMONKEY_SYNCFUSION_KEY). Restart the terminal or IDE
   after environment changes. Do not print or paste the credential. A presence-only
   check in PowerShell is:

   ```powershell
   if ([string]::IsNullOrWhiteSpace($env:FLYERMONKEY_OPENAI_KEY)) { throw 'FLYERMONKEY_OPENAI_KEY is missing' }
   ```

2. Build using the command above, then launch the freshly built executable:

   ```powershell
   & '.\FlyerMonkey.Reviewer.Windows\bin\Stage2Verification\FlyerMonkey.Reviewer.Windows.exe'
   ```

3. Load an existing saved Woolworths extraction, avoiding a new paid extraction.
   Choose exactly one product. Note its name and any current image/path. Leave
   other products unchanged. Confirm DATA/ProductImages is writable.
4. Click that product's existing Crater It button ONCE (this Reviewer button now
   invokes Classic generation). Wait for completion, at most the three-minute
   generation timeout plus local decoding/saving. Do not press another generation
   button during the test.
5. On success, verify that product's thumbnail/preview and recognizable product
   identity. Confirm one new GUID-named .jpg in the existing DATA/ProductImages
   folder opens as a JPEG; confirm other products and previous files are unchanged.
   Use the existing extraction Save action if the operator wants to persist this
   local association. Do not SQL-commit merely to test generation.
6. If it fails or times out, record only the safe operation/status message. Do not
   repeat the paid call during this smoke test. Cancellation/timeout can occur after
   the provider has already generated an image or incurred a charge. A retry is a
   separate operator decision. Blob/SQL commit verification is separate and was not
   performed by this mission.

Remaining risks: actual account/model access and live provider quality are untested;
workstation environment credentials are available to that user's processes; storage
is still tied to the existing absolute workstation folder. The three-minute timeout
can lose receipt of an already-paid result, with no automatic retry. App closure
can cancel an in-flight call with the same ambiguity. A key-bearing Reviewer must
not be distributed to public users. No new authentication system is introduced.
