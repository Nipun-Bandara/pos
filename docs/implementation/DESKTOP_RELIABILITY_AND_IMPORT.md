# Desktop reliability, appearance and catalogue import follow-up

Branch: `codex/desktop-reliability-and-product-import`, created from updated `development`
at `f1083e9` (the merged modern UI). No existing work was discarded. No remote push.

## Startup and appearance

The first window now fits and is positioned within the Windows work area, keeps the native
close/minimize/maximize frame, and prepares the header/language picker before it is shown.
The database startup runs once per window, after the first UI frame can paint. Repeated
WPF Loaded events cannot restart authentication. View changes are coalesced on the dispatcher
to avoid replacing a newer Billing view with an older login view during reentrant events.

The header theme button switches light/dark immediately without rebuilding the active form,
so an entered barcode, basket or partially edited product remains intact. The choice is
persisted atomically in `retailcore.db.ui-theme` beside the Windows profile's database.
Appearance is not financial/shop data and does not require owner approval. Receipt documents
explicitly retain black text on white paper, including previews and return documents.

The large installation folder is expected: this self-contained application includes the
.NET/WPF runtime, SQLite, resources and UI toolkit. Use the installed Start menu shortcut.
Deleting individual DLL/runtime files can prevent startup; shop data is outside that folder.

## Import workflow

In Products, expand **Import new products** (Sinhala/English). Download a CSV or Excel
example, choose an active category, choose/paste the file path, and preview. The preview
shows each record's number, SKU, Sinhala name and localized error. Correct every issue,
preview again, then import. Only owners can preview/import; the application checks current
authority again in the write transaction, including on a retry.

Imports create new products only, in one selected category per batch. Existing products,
stock balances, completed bills and historical financial snapshots are never edited.
Each created product has no-tax defaults and a ProductChanged audit entry identifying the
import and source row. All rows, tax setup, audit and retry identity commit together.
Changing the category/file invalidates the preview. Commit revalidates current duplicates
and category status; an unchanged retry of the same operation does not create duplicates.

Both formats use these exact stable column headings, in this order:

`Sku,SinhalaName,EnglishName,ReceiptName,Barcode,Unit,Cost,NormalPrice,Minimum1,Price1,Minimum2,Price2,Minimum3,Price3,Active`

- UTF-8 CSV, optionally with BOM; quoted commas/newlines and escaped quotes are supported.
- Single-sheet `.xlsx`, literal cells, with headings in row 1 or in the generated template's
  row 3. Excel shared strings, inline strings, sparse cell coordinates and typed booleans
  are supported. Formulas, macro workbooks and external worksheet links are rejected.
- Maximum 10 MB, 5,000 products; XLSX expanded content capped at 25 MB / 100 ZIP entries.
- SKU/barcodes must be text cells in Excel; numeric identifiers are rejected because leading
  zeroes may already have been lost. The templates store these identifiers as text.
- Use invariant decimal points (no thousands separators), up to two decimal places for
  money and three for fractional quantities. Units: `Each`, `Kilogram`, `Gram`, `Litre`,
  `Millilitre`, `Metre`, `Pack`. Active: `true`/`false`.
- Sinhala, English and Sinhala receipt names, barcode and SKU are required. Leave unused
  quantity-break pairs empty. Domain rules validate currency precision, bands and units;
  duplicates within a file and against active/inactive catalogue codes reject the batch.
- Example rows are real products if imported: replace them before importing shop data.

## Validation boundary

The Windows workflow now checks the initial header before resizing, native frame settings,
repeated Loaded events after sign-in, theme persistence without losing scan input, black/white
receipts under dark mode, and an import through the actual Products controls. These tests
retain the existing financial/security assertions and bounded timeouts.

This Linux environment can cross-build WPF but cannot execute Microsoft.WindowsDesktop.App.
Windows launch/minimum-size review in both languages and both themes, installed-upgrade
smoke, and native import/download dialogs remain required before merging this branch.
Physical Xprinter 80 mm/scanner checks, intended-PC performance and supervised pilot-week
acceptance remain separate outstanding shop checks; software tests do not establish them.

### Checks run (2026-11-13, Linux / .NET SDK 10.0.113)

- Full Release solution build with warnings as errors: zero warnings/errors. Final desktop
  rebuild after the last accessibility label: zero warnings/errors.
- Domain: 158 passed. File-backed application/foundation: 102 passed, one explicit performance
  benchmark not run. Architecture: 11 passed. SQLite integration: 10 passed, three optional
  retained PostgreSQL server tests skipped. Total executed/passed: **281**.
- Import tests cover CSV/XLSX persistence and pricing 100/180/255/320/400 then 180,
  exact retries across restart, leading-zero text identifiers, shared strings/sparse cells,
  typed booleans, fractional bands, quoted CSV names, monetary precision, invalid tiers,
  duplicate codes, stale previews, owner/locked-session boundaries and rollback after SQL.
- SQLite and retained PostgreSQL model/migration checks: no pending model changes. No new
  database migration is needed; existing migrations and shop data were not changed.
- 524 unique, non-empty English/Sinhala resource keys match; all XAML parses; diff whitespace
  checks pass. Validation output retained under ignored `artifacts/desktop-followup/`.
- Native WPF execution attempted and unavailable: Microsoft.WindowsDesktop.App is absent
  on Linux. No Windows runtime, installer, printer/scanner or physical-shop acceptance claim.

Do not treat the build as proof that the reported Windows visual defects are resolved on
hardware. The changes and Windows regression checks are committed for that verification.
No local merge to `development` was performed because required Windows validation remains
unavailable. No remote push was requested or performed.
