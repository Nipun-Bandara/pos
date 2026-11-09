# Milestone 03 — receive and explain stock

Branch: `codex/stock-management`, created from updated `development` at `9737caf`,
which contains the merged product/pricing milestone. No existing changes were discarded.
Source implementation is delivered; required Windows runtime acceptance remains pending.
Do not merge or call the milestone accepted until that validation passes.

## Reachable workflow

Sign in as owner and open Stock. The sortable/searchable table shows signed base balances,
conversion factors, reorder thresholds and base units. “Low stock only” lists active base
products at or below their threshold, without counting linked packs as independent stock.
Selecting a product shows all movements of its underlying base product, including movements
entered through other pack products. History shows dates in the shop timezone, entered/base
quantities, conversion factor, both cost views, reason, supplier/reference, actor/approver and
source identity. Cashiers can inspect stock and preview consumption; stock changes are owner
commands independently of disabled controls.

A root product's selling unit is its base stock unit. A separate product sold as Pack can
link directly to that root with a positive factor (for example 25 kg per bag). Chains and
cycles are rejected. Whole-item base units require whole factors. Configure the pack before
its first movement; mappings with stock history or dependent packs cannot be reassigned.
Unit edits cannot reinterpret existing stock or configured pack mappings. Reorder thresholds
are set on the base product. Products also display the shared signed balance and its base
unit; a bag is never given an independent counter.

Select Receiving or Opening balance, enter the quantity in the selected selling unit, cost
per entered unit, movement date, mandatory reason and optional supplier/reference. Save.
Opening balance is allowed only before the first movement against that base product. Costs
and quantities are exact at entry; conversions finer than 0.001 base units are rejected.
The movement retains input quantity, input cost/currency, conversion factor, base quantity,
base-unit cost and base unit. Rounded base-unit cost is a convenience view; original input
cost is preserved. Receiving records the movement's cost; it does not silently replace the
catalogue's current selling cost. Owners maintain that cost in Products. Historical movements
are never rewritten when catalogue costs change.

Adjustment in/out, damage/wastage and stock counts require a PIN approval bound to the exact
operation, quantity, cost, date, reason, reference and count/negative-stock preview. Counts
are entered against the base product in base units; preview shows signed variance and new
balance. Approval and posting recheck the current requester/session and active owner in the
same transaction. A negative result requires the explicit negative-stock checkbox, reason
and exact owner approval. Ordinary owner sign-in alone does not bypass this rule.

Count previews bind both the expected balance and a hash of all movement IDs. Any intervening
movement invalidates the preview, even if another movement restores the same balance. Refresh,
review and approve again. A zero-variance count writes its audit/command result without a
zero-quantity ledger entry. Retrying the same operation returns the original result; reusing
its identity for different details is rejected. All mutations and audit commit atomically.

## Demonstration

1. Create loose rice as Kilogram and a separate sealed bag as Pack in Products. Keep the
   milestone 02 Sinhala/English/receipt names and unique SKU/barcode requirements.
2. In Stock, select the loose rice base product and set reorder threshold 10. Select the
   bag, choose loose rice as base, set factor 25 and save.
3. Receive quantity 2 bags with cost 2,500 per bag. Both product views show 50.000 kg.
   History records 2 bags × 25, input cost 2,500 and base-unit cost 100.
4. Select loose rice for consumption quantity 1.250, choose the bag as the second product
   with quantity 1 and preview. Consumption is 26.250 kg; the projected remainder is
   23.750 kg. This screen saves no sale or stock movement.
5. Post a reasoned wastage/adjustment or count using owner PIN approval. Inspect the full
   history. Try exceeding stock without explicit approval: no stock/audit effect commits.
6. Restart, sign in and verify the same balances/history. The product/pricing regression
   still previews quantities 1–5–2 as 100/180/255/320/400/180 and rejects duplicates/bad bands.

Portable tests additionally post the mixed consumption through the real reusable transaction
contract and demonstrate the actual 23.750 kg persisted balance, without presenting it as a
completed Billing sale.

## Posting contracts and historical compatibility

Sale lines now snapshot base product, factor and base unit. Refund lines copy those fields
and original cost, and distinguish saleable from damaged disposition. Completed sale/refund
events carry those historical values. Saleable returns restore the original base quantity;
damaged returns restore none. Void events also reverse historical converted quantities.

The event handlers post complete batches through the ledger. Immutable posting receipts
identify a whole source, including a damaged return with zero movements. Exact retries do
nothing; conflicting stock effects for the same source fail. A unique movement posting key
provides an additional database guard. Historical financial movements without a receipt
cause a conflicting replay to fail safely rather than duplicate stock. Old voids that used
“Sale” as source are included in that protection.

`StockService.PrepareSalePosting` prepares a database-only callback for milestone 04. Prepare
it once outside the retryable transaction; invoke it inside the same `ILocalTransactions`
write that completes the sale. It freezes request inputs, checks availability across all
lines sharing a base product, and requires exact owner approval/reason/revisions for a
shortage. The later sale-completed event recognizes the already-posted effects. Do not call
this callback in a separate commit from the sale. The raw handler fails closed on unapproved
shortages. Real Billing wiring is milestone 04; actual return workflow wiring is milestone 06.

New forward SQLite/PostgreSQL compatibility migrations preserve old records. Existing stock
is interpreted with factor 1, existing return disposition remains saleable, and no historical
movement, sale amount or audit entry is rewritten. New receipt immutability and posting
validation triggers supplement the existing stock/audit append-only triggers. Old migration
files remain unchanged. No cloud, sync, tenancy workflow or payment integration is added.

## Windows and physical acceptance

The real WPF workflow test now configures base/pack products through Stock controls, receives
two bags, previews the mixed consumption, approves wastage through the actual PIN dialog,
reopens the window, checks persisted balances, switches language and checks cashier editors
are disabled. The existing price preview/duplicate/validation tests are retained. Compilation
of this Windows test assembly on Linux does not execute it.

Required outstanding validation: run the WPF suite and review Sinhala glyph shaping,
translations, keyboard navigation, table scrolling and approval dialogs on Windows/the target
PC. This Linux environment cannot provide Windows runtime acceptance, so the local merge is
withheld. Physical printer/scanner tests were not run; they are separate later-milestone
checks. This workflow requires no scale integration, printer template or physical hardware
input. Sinhala receipt rendering remains milestone 05.

## Recorded verification and merge result

- Final Release solution build with warnings-as-errors: zero warnings/errors, including
  cross-targeted WPF and its Windows workflow test assembly.
- Domain: 142 passed. Architecture: 11 passed. Application/file-backed foundation: 30 passed.
  SQLite integration: 10 passed, with 3 optional PostgreSQL-only tests explicitly skipped.
- Real file-backed tests demonstrate 50.000 → 23.750 kg shared stock, restart persistence,
  exact owner approval, stale-count rejection after equal-net changes, duplicate/conflicting
  posting protection, legacy void replay rejection, saleable/damaged return effects,
  historical pack/cost snapshots and complete stock/audit rollback after injected SQL failure.
- Populated legacy upgrade preserves ledger quantity/cost and default factor 1; append-only
  protections remain effective. SQLite and retained PostgreSQL migration snapshots have no
  pending model changes. Existing migration files were not modified.
- All 186 English/Sinhala resource keys match; stock view literal/enum labels resolve.
  CI YAML parses and whitespace checks pass.
- Not run here: Windows WPF runtime tests, target-PC human/translation/performance review,
  optional PostgreSQL runtime tests or physical hardware checks.
- Merge was not attempted: required Windows runtime validation is unavailable on this Linux
  host. All work remains committed on `codex/stock-management`; `development` remains at
  `9737caf`, the merged milestone 02. No push was performed. No additional product or
  hardware input is required to finish source work; Windows acceptance is the outstanding gate.
