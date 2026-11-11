# Milestone 07 — Reconcile and export a trading day

Branch: `codex/daily-close-and-reports`, created from updated `development` at `41011ce`
(the merged milestone 06). No remote push is authorized. Source implementation is not
Windows or physical acceptance. Keep this branch unmerged until required Windows checks pass.

## Reachable workflow

Reports retains saved-bill history as its first tab. The Drawer cash tab accepts a counted
cash amount or pay-in/out amount and a reason. Count first, then review the expected balance
and approve with a real owner PIN. Cashiers may request these operations, but application
commands require a matching owner grant. Grants bind the complete request, shift and expected
balance. Stale balances, excessive pay-outs, negative counts, missing reasons and conflicting
retries fail. Closing freezes expected cash, count and variance. Cash movements and audit
commit together; exact authenticated retries return the saved result, including after restart.
SQLite guards reject cash movement edits/deletes, movements into closed shifts and changes to
closed shifts. There is no reopening or mutable closing report.

Owners get a Trading reports tab with inclusive shop-local date filters and tables for:

- Gross sales, discounts, returned turnover, net sales, tax and estimated historical-cost profit.
- Product/category turnover and quantities, separated by selling unit and sorted by net sales.
- Actual cash/card collections/refunds, applied non-tender exchange credit, cash in/out and net drawer movement.
- Whole-shift reconciliation for shifts opened or closed within the period, plus individual cash movements.
- As-of closing-date signed base stock/value, low stock and dated movement history.
- Dated privileged-change history with actor, approver, reason and record reference.

CSV, XLSX and PDF buttons share the same selected report and labels. Products provides all or
visible filtered CSV/XLSX exports and a filtered PDF price list (clear search for all products).
Exports are written to a temporary file and renamed after success; failures do not alter trading data.

## Accounting definitions

Gross uses each original normal-price snapshot, falling back to the original charged gross
for legacy lines. Discounts include tier/manual savings and explicit discounts. Returned
turnover reverses original discounted line value at cumulative actual-credit boundaries;
collected cash-rounding differences are reported separately. Tax follows those monetary boundaries; historical cost follows returned-quantity boundaries,
so tiny repeated returns reverse exactly once. Zero-collected lines use quantity boundaries. No kept
item is repriced. Profit is net sales less net tax and historical cost after returns, including
damaged returns; it is an estimate, not net business profit. Wastage remains a separate stock
movement rather than silently changing the sales margin.

Replacement turnover and return turnover each appear once. ExchangeCredit never enters
cash/card collections. New refund settlement scalars identify actual external payouts; legacy
refunds fall back to their recorded tender and total. New void reversals include the original
sale and its return once each. Historical legacy voids without explicit financial reversals
retain the existing repository exclusion policy. ShiftRepository already used applied
Payment.Amount without subtracting change again on the merged baseline; regression tests
preserve that behavior.

New sale lines snapshot category IDs. Existing completed lines remain untouched and are
unclassified when their historical category is unknown. Category labels are explicitly current
labels; changing a product's category does not move newly snapshotted historical turnover.
Historical profit uses sale-line costs, never current catalogue costs. Stock value is a clearly
labeled estimate using current recorded base-unit cost and the selected ending-day balance;
it is not FIFO or historical inventory valuation. Sealed pack aliases are excluded from balance
valuation so the underlying stock is counted once.

## Export contracts and limits

UTF-8 BOM CSV preserves Sinhala and quotes delimiters/newlines. Formula-like text and leading-zero
identifiers receive a protective apostrophe; consumers must treat those fields as text. Use XLSX
for exact literal barcode values and typed decimal/date cells: strings are inline literal text,
never formula elements. Dates are shop-local and the period/timezone/currency appear in exports.

PDF uses Windows WPF Unicode shaping, rendered at 192 dpi and embedded in an ordinary PDF file.
No PDF printer or printer character set is needed. Long rows continue onto subsequent pages;
wide tables repeat their first column on additional column bands. PDF text is rasterized and
is not searchable/selectable. The renderer requires Windows, and its Sinhala wrapping and
legibility require visual acceptance rather than a cross-target compilation claim.

Report reads currently materialize single-shop history to allocate repeated historical returns
consistently. Validate performance against realistic shop history before pilot. Date ranges
are limited to 366 days between endpoints. No cloud service, import, customer credit, backup,
installer or payment integration is added.

## Validation record

Validated on Linux with .NET SDK 10.0.113/runtime 10.0.13:

- Release warnings-as-errors solution build, including WPF cross-target compilation: zero warnings/errors.
- Domain: 158 passed. Application/file-backed SQLite: 80 passed. Architecture: 11 passed.
- Integration: 10 passed; 3 opt-in legacy PostgreSQL tests skipped (no PostgreSQL service requested).
- Both SQLite and retained PostgreSQL model snapshots: no pending model changes.
- Fresh database and existing-schema forward upgrades run through the file-backed suite.
- Injected cash posting failure after SQL writes rolls back movement, audit and command result.
- Cash change 150/200 adds 150 to drawer; pay-in 50/out 20 closes expected 1180 against counted
  1175 with persisted variance -5. Restart/retry preserves that close.
- Exchanges return 100 against 150/80/100 replacements with actual collect/refund/zero settlement;
  their report turnover and original-cost profit remain correct after catalogue cost changes.
- Three five-cent pieces, including separate-day refunds of .02/.02/.01, reverse exactly .05
  without inventing cash rounding. Tier discount, damaged return, wastage, current low-stock,
  snapshotted categories, owner authorization and shop-local midnight boundaries are checked.
- Shared writer tests verify Unicode/BOM, CSV escaping/protective prefixes, XLSX literal text,
  decimal cells and date serials. LibreOffice independently reopened and rewrote the actual
  generated workbook: exact `001234`/`00009`, Sinhala, literal formula-like text, typed negative
  numeric values and date serials survived; no formula elements appeared.
- 440 matching Sinhala/English resource keys; repository whitespace checks pass.
- Actual WPF execution was attempted and is blocked by missing Microsoft.WindowsDesktop.App
  on Linux. No Windows UI/PDF or physical printer/cash acceptance is claimed.

No merged-result verification is claimed: the required Windows gate is unavailable, so the
committed feature branch is intentionally left unmerged. `development` remains at `41011ce`.
No remote push was performed.

## Required Windows and physical acceptance

Run the Windows CI job or equivalent on a real Windows computer. It builds WPF and runs the
real dispatcher workflow, including reporting controls and generated Sinhala CSV/XLSX/PDF
artifacts. Download the artifacts and visually inspect the PDF pages (Sinhala shaping,
wrapping, continuation headers, totals and column bands). Open CSV/XLSX in Windows Excel and
verify Sinhala, protected formula-like text, typed numbers/dates and leading-zero barcodes.
These files must not be signed off from a mock renderer.

With the owner, trade a test day containing cash change, card sales, all exchange directions,
pure/partial/damaged returns, wastage and reasoned pay-in/out. Count the actual drawer before
seeing expectation, close, compare variance and restart. Physical cash counting and printer
checks remain separate from the file-backed financial tests; no physical acceptance was
performed in this Linux environment. No additional printer model input is required for the
report implementation. Existing Xprinter/Sinhala receipt hardware acceptance remains pending.
