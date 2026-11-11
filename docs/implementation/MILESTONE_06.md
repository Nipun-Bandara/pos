# Milestone 06 — return or exchange goods

Branch: `codex/returns-and-exchanges`, created from updated `development` at `dfafb15`, the
merged receipt milestone. Existing work was preserved. Source workflows are implemented;
required Windows runtime and physical acceptance remain pending. No push or local merge is
performed while those gates are unavailable. This is not production acceptance.

## Reachable workflow

Open Returns and scan/type the original bill number, or choose “Return / exchange this bill”
from saved-bill detail. The table shows the original Sinhala receipt name, remaining quantity,
remaining original value and selling unit. Enter selected quantities (three decimal places for
weighted units, whole quantities for Each/Pack), and choose saleable/restock or damaged/no
restock per line. Enter a mandatory reason. Original prices, quantity tiers, discounts, tax
and stock/cost/conversion snapshots determine the return; today's catalogue never reprices
items kept from the old bill. Deactivating or renaming a product does not change the old bill.

For an exchange, build the replacement goods in the existing Billing workflow, return to
Returns and select the current Billing basket. Replacement goods use current prices, tiers,
tax and inventory mappings. Quantity/price edits and owner discount proposals stay in Billing;
there is no second independent pricing engine. Keep one tender method for any additional
collection, while historical mixed cash/card payments remain supported for outgoing refunds.

Preview shows original return value, replacement total, credit applied implicitly to that
replacement, additional collection, separate cash rounding, cash/card refunds and expected
cash change. It also shows base stock before, saleable stock returned, replacement consumption
and final balance. Insufficient stock needs explicit consent, a reason and owner approval.
Returns can supply stock for their own replacement basket within the same transaction;
damaged goods supply none. The owner sees selected quantities/dispositions, replacement
prices/reasons, settlement and stock effects in the real PIN dialog. Any change to the
request, prior returns, basket/current prices, stock revision or open shift invalidates it.

Every return/exchange requires a fresh, exact owner PIN grant at the application write
boundary, including when the signed-in user is an owner. Cashiers can prepare the operation
and obtain that grant. Locked/revoked users cannot complete or replay commands. Approval binds
request/preview hashes, not caller-supplied roles; long baskets do not exceed the grant payload
limit. No PIN, approval ticket or card credential is stored in financial records.

A positive difference collects additional cash or externally approved card. A negative
difference pays the remaining original entitlement back to its original cash/card sources.
Card refunds require a separate explicit confirmation that the external terminal approved the
exact previewed refund. No gateway is called. References use the existing restricted safe
identifier rules; card numbers/security codes/PINs are rejected. A zero difference posts no
external tender. An unused input tender is not recorded as received cash. Cash refunds need
sufficient expected drawer cash; check opening float before proceeding. Closing/pay-in/out
screens remain milestone 07.

On success, the original bill's history contains the linked return/exchange document and
replacement bill number. Preview or print/retry the Sinhala linked document, or inspect its
print-attempt history before retrying. Restart and find the same original bill to recover
those documents. Printing operates only on saved documents, outside financial writes.

## Exact refund limits and rounding policy

`ReturnAmounts` allocates the original **collected** minor units, including original cash
rounding, over immutable original lines using cumulative proportional boundaries. It uses
integer/BigInteger arithmetic so the line entitlements sum exactly to the collected amount.
For each line, new returns use original quantity and its entitlement; the final remaining
quantity gets the entitlement less **actual prior line refund amounts**. Earlier quantities
alone are insufficient and fail closed when recorded amounts are required. Whole-unit and
positive/fractional-precision restrictions apply to return quantities too.

Rs. 0.05 returned as quantities 0.333, 0.333 and 0.334 yields 0.02, 0.02 and 0.01, rather than
reconstructing prior refunds from aggregate quantity and accidentally giving 0.06. Repeated
partial returns cannot exceed either sold quantity or original collected money. Previously
completed records remain unchanged. Full returns can differ from an old unrounded line total
by their allocated original cash rounding; the original bill keeps its own total/rounding.

Cash refund amounts are exact minor-unit entitlements and are not rounded a second time.
Additional cash collection uses the configured increment on the **remaining difference** and
stores that settlement adjustment on the replacement sale and linked result/document. Card
collection is exact. Review these cash refund/coin-handling semantics on the target PC during
acceptance; no deferred customer balance or store credit is created.

Original cash/card source capacities are debited for the entire return value, including value
used for an exchange. Allocation uses cash capacity first, then card; applied exchange credit
uses those allocated cash components first, then card. Any remainder is paid out to the
corresponding original source. This is deterministic for multiple payment rows. Replacement
sales record `PaymentMethod.ExchangeCredit`, linked refund identity and its cash/card origin
components, separately from real cash/card payments. A later return of replacement goods
therefore follows the same funding sources rather than turning card value into invented cash.
Unsupported legacy QR/voucher/customer-credit sources fail closed with a localized message;
the milestone introduces none of those payment workflows.

New refund settlement fields distinguish original source credit, exchange credit applied and
actual cash/card payout. Nullable fields retain the prior Method/Total interpretation for
legacy refunds. The shift projection excludes exchange credit from external money, uses actual
new refund payouts and retains the actual tender of a newly reversed void before subtracting
its explicit payout. This avoids subtracting full return credit and counting replacement
credit as cash/card. Broader turnover/profit/close-day reports remain milestone 07.

## Eligible voids

A void requires the original open shift, no completed prior return (including a zero-value
return) and no replacement basket. The complete original quantity and collected entitlement
are reversed with owner reason/approval. The original sale retains all financial/line/name
snapshots; only the existing void status/reason/actor/time fields change.

An explicit full financial refund is marked `IsVoidReversal`. Its refund event carries no
stock items; the SaleVoid posting restores the original stock exactly once. The transaction
stores both together. Application checks plus new SQLite triggers block ordinary returns
after void and void after completed return. Completed refunds/lines reject update/delete.
Older historical voids keep their existing drawer interpretation. Use an ordinary selected
return where a void is ineligible; no prior closed shift is rewritten.

## Atomicity, history and printing

One serialized SQLite write reloads all progress and commits refund, optional replacement,
explicit credit/source allocation, real payment, receipt allocations, stock postings, audits,
linked immutable result/document and the cleared replacement basket. The shared tracked
counter can issue both return and replacement numbers in one unit of work. Stock posting
receipts recognize the later domain events without duplicating effects, including empty
postings for damaged returns/void financial reversals. Both pack and loose return effects use
original conversion/cost snapshots; shared balances use the signed append-only ledger.

The operation UUID and immutable command fingerprint make exact retries return the saved
result before consulting today's prices/shift. Conflicting reuse fails. Concurrent competing
returns cannot spend the same quantity/value twice. A write failure rolls back stock, audit,
payments, refunds, replacement sale, counters, documents and basket changes together.

New version 3 Sinhala return/exchange/void documents show original and replacement receipt
numbers, original returned names/quantities/dispositions/credit, replacement lines, additional
collection, cash/card payouts, rounding and change. They reuse the existing renderer, barcode
and Windows adapter. Versions 1/2 sales receipts retain their old rendering. Print requests
and outcomes have separate append-only records: commit the authenticated attempt, submit once,
then append outcome. An interrupted outcome remains unknown, never automatically resubmitted.
Repeated attempts are copies; same attempt UUID does not print again. Driver failure cannot
repeat settlement or stock. External card approvals are confirmations of separately handled
terminal actions, not transactional gateway calls or automatic bank compensation.

The forward SQLite migration adds settlement/source fields and return/print records with
foreign keys and immutability/void guards. The retained PostgreSQL migration adds only shared
financial compatibility fields. No applied migration is edited and no old financial amount
is backfilled/repriced. The existing populated legacy sale upgrade checks continue to pass.

## Demonstrations and recorded checks

The file-backed application tests use real SQLite transactions. An injected printer contract
failure is explicitly not hardware acceptance. They cover:

- Original 100 against replacements 150/80/100: collect 50, refund 20, move zero. New payment
  rows distinguish applied exchange credit from actual cash, with correct drawer/stock and
  linked receipt numbers. Restart and exact replay preserve one committed operation.
- Return the 150 replacement later: its 100 exchange-credit source plus 50 real cash can
  refund 150 once, returning the drawer to its opening amount.
- Historical 40 cash + 60 card against replacement 80: applied credit carries 40 cash + 40
  card, with 20 externally confirmed card refund. Returning that replacement pays 40 cash +
  40 card and reconciles the drawer. No fabricated 80 card tender is created.
- Original three-unit tier bill 255 returns 85 then 170 after catalogue repricing/renaming/
  cost changes, preserving original 85 price and 50 unit cost. Damaged goods add no stock;
  subsequent saleable goods do. A prior return blocks void at application and SQLite level.
- Three fractional returns of a five-cent card line total five cents. Missing external card
  refund approval fails. Additional card collection requires external approval, records only
  the difference and rejects card credentials.
- Whole-bill void is one financial and one stock reversal. Exact replay adds none. A voided
  bill cannot be returned again. Missing/mismatched owner approval and locked replay fail.
- An injected post-SQL failure midway through an exchange leaves the original sale only,
  restores the prior basket/counter/stock/audit state and burns no numbers. A fresh owner
  grant can retry the same operation once. Competing returns allow only one winner.
- A replacement shortage requires explicit reasoned approval; changed current prices invalidate
  the old preview/grant. Return print failure, restart and copy retry retain one refund and
  historical Sinhala content; immutable return/refund rows reject alteration.

Final recorded gates:

- Release solution build with warnings-as-errors: zero warnings/errors, including the WPF
  application and Windows dispatcher test assembly (cross-target compilation on Linux).
- Domain: 157 passed. Application/file-backed foundation: 71 passed. Architecture: 11 passed.
  SQLite integration: 10 passed, with 3 optional PostgreSQL runtime checks explicitly skipped.
- SQLite and retained PostgreSQL EF snapshots report no pending model changes. Fresh databases
  and the existing populated legacy sale migration checks pass. Applied migration files are
  unchanged; only new forward migrations and current snapshots were added/updated.
- All 334 English/Sinhala interface keys match without duplicates; Returns literal, disposition
  and unit labels resolve. The fixed Sinhala return receipt labels are separate from UI culture.
  CI YAML parses and whitespace checks pass. No new package dependency was introduced.

## Windows and physical gates — not yet run

The real Windows dispatcher test now edits the Returns quantity cell, previews a partial
return from a real completed manual-price bill, enters the owner's PIN in the actual dialog,
checks the saved 80 refund and drawer 1,598, calls the actual Windows adapter's missing-queue
path, opens the actual version 3 preview and reopens saved-bill history with the linked result. It builds on Linux but has not
executed here. It does not substitute for physical disconnection/reconnection testing.

Run the Release build and Desktop executable suite on Windows, then review keyboard editing,
Sinhala shaping/translation, approval scrolling and the linked version 3 receipt on the shop's
printer. Xprinter with 80 mm paper is known; confirm Windows queue/driver, actual printable
area and scanner behavior on that PC. No specific model is required to continue source work.
Physical paper wrapping/totals/barcode and printer failure/retry remain unverified. No printer,
scanner, scale or payment terminal hardware was exercised here.

Required Windows runtime/physical validation is unavailable on this Linux host, so the local
merge is withheld. All work remains committed on `codex/returns-and-exchanges`; `development`
stays at `dfafb15`. No push is performed. No additional product decision is blocking the source
workflow; access to the Windows acceptance environment is the specific outstanding requirement.
