# Milestone 05 — print and find a saved bill

Branch: `codex/receipts-and-history`, created from updated `development` at `34113f7`,
which contains the merged checkout milestone. Existing work was preserved. Implementation
is provisional until the owner's actual template and printer details arrive. Windows runtime
and physical acceptance have not been performed here; the milestone is not accepted and the
local merge is withheld. No push was performed.

## Reachable workflow

Complete a bill in Billing, then choose “Find / print saved bill”. Alternatively open Reports,
which now contains Saved bills. Select an inclusive shop-local date range and filter, select
a row and open it, or scan/type the full bill number and press Enter. Exact lookup searches
all dates independently of the current filter. Results are newest first, show original total,
currency and current completion/void status, and can be sorted. Narrow the range if more than
500 bills match; ranges are limited to 367 days. There is no silent truncation.

The detail view shows original receipt content, quantities, charged/normal/qualified tier
prices, historical unit cost, tax code/category/rate/amount and manual price reason. The receipt
also shows original discounts, net/tax/gross, total, separate rounding, amount due, all stored
payments, cash tender and change. No current product lookup recalculates a bill. Current void
status is shown separately and a voided receipt carries a Sinhala void banner.

Choose Preview Sinhala receipt to see the same WPF layout/pagination and vector Code 39
barcode used for printing. Receipts always use fixed Sinhala resource labels, `si-LK` shaping
and installed Nirmala UI/Iskoola Pota font fallback, independently of interface language.
Original proper names and other owner-entered text are preserved, not machine-translated.
Long content wraps and paginates. The bill number is also human-readable for manual lookup.

An owner configures the exact Windows printer queue name and **printable** width in Settings
through the existing owner/PIN-approved command. The stored `PaperWidthMillimeters` setting
is used as printable content width; it is not a claim that an 80 mm roll has 80 mm imageable
width. The adapter checks the driver's current imageable area, offsets the document into it,
uses its media height for pagination, requests one copy at 100% scale and never substitutes
the default printer. Unsupported width/barcode combinations fail with a localized message.

Printing is explicit and only available for saved bills. On failure, the saved bill remains
available. Inspect the queue and retry/reprint from its detail screen after restart. Each new
attempt after the first is visibly marked a copy, including a retry after a failed attempt.
The detail view lists attempt UTC time, printer, configured width, original/copy and outcome.
“Windows spooler accepted” means submission was accepted, not proof of physical paper output.
Failure messages also cover uncertain submission; drivers' raw exception messages are not saved.

## Provisional template contract and missing inputs

The fixed `provisional-si-v1` layout is intentionally stamped provisional in both preview and
paper content. It is not the owner's approved custom template. It consumes version 1 frozen
receipt documents and has no editable template language, scripts or template editor. A future
approved template must receive a new version rather than changing the rendering rules of
already saved version 1 documents. The snapshot includes shop header/contact/tax registration,
footer, cashier, original local timestamp/zone, currency, lines and payments. Print metadata
and current void/copy banners are separate from original financial content.

Specific unresolved inputs: the owner's custom Sinhala template (field order, headings,
branding and footer), printer model and exact Windows queue/driver, actual imageable width,
paper width/media configuration, and access to the intended Windows PC and physical printer.
Use a scanner configured for ordinary Code 39 without mandatory checksum/extended decoding
for these bill codes; verify its actual configuration during physical acceptance. Unsupported
historical terminal characters/overlong codes produce an actionable barcode error; manual
bill lookup remains available. These inputs do not block preview/contracts or local history.

Code 39 character encodings were checked against the primary
[ZXing reader implementation](https://github.com/zxing/zxing/blob/master/core/src/main/java/com/google/zxing/oned/Code39Reader.java).
The local encoder uses start/stop bars, 3:1 wide/narrow ratio, inter-character spaces and
10-module quiet zones. This source check does not establish physical scan reliability.

## Persistence and failure boundary

New checkout completion saves an immutable receipt JSON document in the **same SQLite
transaction** as sale/payment/receipt counter/stock/audit and the completion command. The
receipt retains original names, costs, tax, quantity and tier/final price values plus the
shop header and timestamp. Catalogue or shop profile changes cannot alter its content.
Existing command retries do not create or overwrite receipts.

Bills predating milestone 05 retain their stored line/financial snapshots. Their old shop
header was never recorded; on first detail view, freeze the current header with an explicit
legacy warning instead of inventing historical header data or changing the original sale.
The forward migration adds only local receipt/print tables and indexes; it does not backfill
or rewrite old completed records. Foreign keys link receipt to sale, attempts to receipt and
outcomes to attempts. Database triggers prevent updates/deletes of all three new tables.
Existing stock/audit immutability remains intact. Retained PostgreSQL has no new runtime path.

Printing runs outside the financial transaction. First commit an authenticated, immutable
attempt and audit with UUID, actor, printer/width, document hash and copy flag. Then call the
Windows adapter once. Finally append the outcome and audit. A cancellation, crash or write
failure after submission may leave a requested attempt with no outcome: the UI labels it
unknown/interrupted, never success. Reusing the same attempt identity does not call the
printer again, even after restart. A deliberate retry uses a new identity and marks a copy.
Concurrent duplicate identities submit at most once; conflicting bill/actor reuse fails.

No print path calls checkout completion, changes payment, allocates a receipt number or posts
stock. Reads and print requests reload the active signed-in user; locked/revoked users cannot
read or print cached documents. Cashiers may view/print bills, but changing printer settings
still requires owner role and an exact owner approval at the application boundary. Outcomes
can be persisted after sign-out using the already authenticated attempt's frozen requester;
this cannot perform any financial operation.

## Demonstrations and checks

Portable tests use real file-backed SQLite transactions; an injected printer contract failure
is explicitly not physical hardware acceptance. They demonstrate:

- Complete a 150 bill with 200 cash, fail a print attempt, restart, find `t1-1`, change product
  price/name/cost and shop header/footer, and retry the same original content as a marked copy.
  There remains one sale, payment, receipt allocation and sale stock effect.
- Concurrent duplicate print IDs call the adapter once. An outcome write failure leaves an
  unknown attempt; replay does not silently resubmit it. A deliberate copy can be requested.
- Receipt snapshot insertion rolls back with a failed sale commit. Receipt/print records
  reject update and delete SQL. The existing checkout rollback tests remain passing.
- Shop-local 23:59:59 and next-day 00:00:00 fall into the correct inclusive date filters.
  Exact bill lookup bypasses those date filters. Invalid ranges/numbers and missing bills
  give actionable errors.
- Cashiers can print but cannot change settings even with a valid owner approval; lock and
  revocation block further access. Unconfigured printers create no attempted print job.
- Legacy bill detail keeps original line names/prices and multiple cash/card payment amounts,
  marks the missing original header and freezes its first-view header. Sinhala layout labels
  do not change when UI culture switches between English and Sinhala.
- Existing tier regression remains: 1–5 units yield 100/180/255/320/400, then 2 yields 180;
  invalid bands and duplicate SKU/barcode rejection and restart persistence remain covered.

Recorded automated gates: Release solution build with warnings-as-errors has zero warnings
and errors; domain 151 passed; architecture 11 passed; SQLite integration 10 passed with
3 optional PostgreSQL runtime checks skipped. Application/file-backed foundation: 57 passed. Both provider migration snapshots report no pending model changes.
Old migration files are unchanged. All 293 Sinhala/English interface keys match, including
saved-bill labels and status/tax enums; fixed Sinhala receipt resources are separate. CI YAML
parses and whitespace checks pass. Restore used the existing local package cache; no new
package dependency was introduced.

## Windows and physical gates — outstanding

The real Windows WPF dispatcher test was extended to open a saved bill, invoke the real
Windows adapter against a deliberately nonexistent queue, observe failure, preview the
Sinhala/barcode document, test long-name pagination at 48 mm, reopen and find the same saved
bill/failed attempt. Existing checkout/product/stock tests are retained. This assembly builds
on Linux but **was not executed here**. Its missing-queue scenario is not a disconnected
physical printer test. Windows CI is configured; no remote execution/result is claimed.

Before merge, run the Release Windows build and Desktop executable test, review real
previews/keyboard flow/Sinhala translations, then apply the supplied template and verify it
on the actual printer. Physically disconnect/reconnect, restart/find/retry without another
sale; inspect Sinhala shaping/wrapping, totals, paper edges, page/feed behavior and barcode
scanning. Confirm configured queue/media/scaling and scan focus on the target PC. Spooler
acceptance alone cannot satisfy these checks. No physical printer or scanner test was run.

Merge is withheld because the required Windows runtime and physical/template validation are
unavailable. Work stays on `codex/receipts-and-history`; `development` remains at `34113f7`.
