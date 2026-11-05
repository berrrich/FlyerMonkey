# Reviewer inline field display correction — November 4, 2026

The offer card in `FlyerMonkey.Reviewer.Windows/MainWindow.xaml` uses separate
TextBox editors and TextBlock displays. Variant, PackSizeText, ProductName, Brand,
Price, RegularPrice, Saving, UnitPrice, OfferQuantity, Promotion and Category each
bind to the same property on the same ExtractedProduct. Editors are TwoWay with
UpdateSourceTrigger=PropertyChanged. There are no converters, StringFormat,
TargetNullValue or FallbackValue on these pairs. CalculatedRegularPrice is display
only, assigned by the existing ProductValidationService sanitizer.

Root cause: ExtractedProduct implemented INotifyPropertyChanged but only raised
notifications for ImageFilePath. Inline properties were auto-properties. WPF sent
typing to the actual model, but the independently bound TextBlock did not receive
notification. FinishEditing only collapsed the editor and revealed that stale
TextBlock. The text was retained in the model, not erased by LostFocus, hidden by
formatting, or stored in a temporary editing value.

Before the correction an offline WPF regression using the actual production field
grids reproduced Half Leg Ham / Variant: initial model null and display blank;
typing set the model to `Half leg ham`; the display remained blank after LostFocus.
The runner failed at `Variant display updates after LostFocus`.

The only production change is in Models/ExtractedProduct.cs: inline-bound values,
PrimaryCategory and CalculatedRegularPrice now raise PropertyChanged when their
values change, through a shared equality-checked setter. The public property names,
types and JSON representation are unchanged. No XAML, focus handlers, category
dialog, validation/calculation rules, persistence service or image-generation code
changed. Calculations still occur when the existing sanitizer runs, rather than
introducing live recalculation on every keystroke.

Tests added in tests/ReviewerEditing.Checks/ReviewerEditing.Checks.csproj and
Program.cs load production field grids into WPF, remove application event hookups
and simulate the unchanged LostFocus visibility transition. They verify actual
model writes, display updates, all inline fields, a second card, selection/rebinding,
Items.Refresh, recreated cards, category-dialog-style model assignments, existing
regular-price sanitation/calculation and WPF binding diagnostics. Production Save,
UpdateSavedExtractionAsync, read and SqlCommitService.LoadProductsAsync are exercised
against a unique temporary SQLite database. No SQL commit method is called. New
and updated batch values survive JSON/SQLite reload and display correctly. Only
the temporary fixture directory is removed; existing saved extractions are untouched.

Validation commands:

```powershell
dotnet run --project tests/ReviewerEditing.Checks/ReviewerEditing.Checks.csproj --no-restore --no-launch-profile -p:NuGetAudit=false -p:OutputPath=bin/EditingVerification -nodeReuse:false
dotnet run --project tests/ReviewerClassic.Checks/ReviewerClassic.Checks.csproj --no-restore --no-launch-profile -p:NuGetAudit=false -p:OutputPath=bin/ClassicCompatibility -nodeReuse:false
dotnet build FlyerMonkey.Reviewer.Windows/FlyerMonkey.Reviewer.Windows.csproj --no-restore -o FlyerMonkey.Reviewer.Windows/bin/InlineEditingVerification -p:NuGetAudit=false -nodeReuse:false
```

Results: 96 editing checks and 114 Classic checks passed. No WPF binding warnings
or errors in the editing runner. Reviewer build: zero warnings/errors. Classic
runner emits its existing decoder warnings for deliberately invalid JPEG fixtures.

Limits: tests simulate LostFocus and card selection with production bindings; the
running owner's Reviewer, real keyboard/mouse focus, category modal interaction
and existing extraction database were not exercised. Switching saved extraction
batches still intentionally loads that batch from SQLite; this correction does not
introduce cross-batch unsaved-draft storage. Normal card refresh retains the same
edited models. No paid calls, image-generation changes, Azure/cloud configuration,
production SQL mutations, saved-extraction resets or Git operations were performed.
