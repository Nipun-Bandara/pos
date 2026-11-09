# Milestone 02 — products and quantity prices

Branch: `codex/products-and-pricing`, created from updated `development` at `4363844`
(the merged milestone 01). No remote push is authorized. Source implementation and portable
verification are delivered; Windows runtime acceptance is pending, so the local merge into
`development` is deliberately withheld.

## Reachable workflow

Sign in as owner, open Products and scroll to Category management. Enter Sinhala/English
category names and save. Enter a SKU, Sinhala/English product names, a Sinhala receipt name,
a primary text barcode, category, selling unit, cost and normal unit price. Enter up to
three quantity breaks in ascending order; leave unused trailing breaks blank. Save product.
Select a row to edit. Clear Active and save to deactivate a product or category; reselect
Active and save to reactivate it. Existing products can retain an inactive category; new
products and category changes require an active category. Inactive products remain searchable
and continue to reserve their SKU/barcode. Product changes require a signed-in owner at the
application command boundary. Cashiers can search, sort, select and preview saved products.

All editor labels, validation messages, unit names and preview headings use English/Sinhala
resources. Data preserves Unicode and barcode leading zeroes. Column headings sort the table;
search matches product names, SKU, barcode and category. The table includes cost, normal price,
quantity bands, active status and signed stock. No mutable stock field is added: the read
projection sums ledger thousandths with direction in SQL. A new product has zero stock.

Prices are entered in the configured shop currency. New products get an exempt, zero-rate
no-tax rule automatically; edits preserve an existing tax assignment. There is no tax setup
step. Money persists as integer minor units and quantity thresholds as integer thousandths.
A band chooses a final unit price for *every* unit, rather than marginal or stacked discounts.
Thresholds can be fractional for weighted units, including below one unit; Each/Pack require
whole quantities and thresholds. Prices must be positive and non-increasing; cost may be zero.

The preview reacts to edits and quantity changes. Example: normal 100, breaks 2:90, 3:85,
4:80. Quantities 1, 2, 3, 4, 5 produce totals 100, 180, 255, 320, 400; reducing to 2 gives
180. It shows the selected band, unit price, total and savings. Invalid bands and duplicate
SKUs/barcodes are rejected without saving a partial product or audit entry.

`QuantityPricing.Preview` is the reusable domain quote operation. `Sale.AddPricedLine` captures
the charged price, cost, tax, Sinhala/English/receipt names, band index and threshold. Band
index zero is normal price with threshold zero (normal applies to all positive quantities
below the first break). Existing `Sale.AddLine` remains compatible. Repeated-scan aggregation,
basket quantity repricing and real Billing integration remain milestone 04; the preview is
not a completed checkout. Sinhala receipt rendering and printing remain milestone 05.

## Migration and history

New forward migrations extend SQLite and the retained PostgreSQL compatibility snapshot;
historical migrations are untouched. Existing catalogue rows keep IDs, English names, money,
barcodes, category and tax assignments. No Sinhala translations or primary barcode choices
are invented. Existing products become inactive until an owner supplies the missing Sinhala
names, receipt name and primary barcode and saves them active. The table exposes legacy
barcodes as an editing suggestion, without silently writing a primary choice. Old sale
snapshot fields remain null; their existing description and financial values stay unchanged.

New primary barcodes have a unique database index. Application checks also reserve secondary
legacy barcodes and compare SKUs without case distinctions; new SKU text is trimmed and
uppercased. SQLite transactions serialize writers and atomically commit product, bands,
barcode, default tax and audit. A failed write discards its context and rolls everything back.
Stock and audit immutability triggers remain in force.

## Verification

The automated suites exercise the real domain/application code and file-backed SQLite,
including reopening a provider after saving. They cover tier boundaries and quantity reduction,
fractional thresholds, invalid bands, currency/precision, repeated tier edits, default no-tax,
identifier conflicts including inactive/secondary codes, owner/cashier/locked/revoked access,
rollback injected after SQL writes, signed negative fractional stock, legacy-schema upgrades
and completed sale snapshots after catalogue changes.

The Windows WPF workflow test has been extended to enter category/product data through actual
controls, preview 1–5–2, reject an invalid band, save/search/select, reopen persisted Sinhala
product data, switch languages and verify cashier editors are disabled. CI includes
`development` and runs this workflow on Windows. Cross-target compilation on Linux is not
runtime validation. Windows tests and human Sinhala/keyboard/display-scale review have not
run in this workspace. This is the required validation blocker before merge.

Physical printer/scanner checks were not run. No hardware is needed for this product/preview
milestone; Sinhala receipt paper output, actual template, printer model/driver and paper width
remain separate milestone 05 inputs. No production or hardware acceptance is claimed.

## Recorded checks in this workspace

- Final Release solution build with warnings-as-errors, including cross-targeted WPF and the
  Windows test assembly: zero warnings, zero errors.
- Domain: 137 passed. Architecture: 11 passed. Application/file-backed foundation: 20 passed.
  SQLite integration: 10 passed; 3 optional PostgreSQL-only tests explicitly skipped.
- SQLite and retained PostgreSQL migration snapshots: no pending model changes. Both empty
  database startup and populated legacy catalogue upgrade run in the file-backed tests.
  Historical migration files are unchanged.
- English/Sinhala resources: all 122 keys match; literal product view keys resolve.
  CI YAML parses; whitespace/diff checks pass.
- Not run: Windows WPF runtime suite, target-PC Sinhala shaping/keyboard/display-scale human
  review, optional PostgreSQL runtime checks or physical hardware acceptance.
- Merge result: not attempted because required Windows validation is unavailable. The working
  branch stays `codex/products-and-pricing`; `development` remains at the merged milestone 01.
  No push was performed. No additional product or hardware input blocks milestone 02 source.
