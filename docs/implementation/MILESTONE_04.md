# Milestone 04 — complete a sale

Branch: `codex/checkout-and-payments`, created from updated `development` at `be44df0`,
the merged milestone 03. Existing work was preserved. Source implementation and portable
checks are delivered; Windows runtime acceptance is outstanding. The branch is not merged
and the milestone is not marked accepted. No push was performed.

## Reachable workflow

Sign in and open Billing. Open a shift with an exact nonnegative opening float before
settling. Cashiers may open a shift; opening is authenticated and audited. The screen shows
opening float and expected drawer cash. Closing and cash pay-in/out remain milestone 07.

Enter or scan a barcode and press Enter. Repeated scans increment the existing product line
and recompute its quantity tier. Incoming completed scans are queued sequentially, including
while a prior scan is saving. Manual scan quantity and selected-product quantity support
three decimal places for fractional units; whole-item units reject fractional quantities.
Search Sinhala/English names, SKU or barcode, filter by category, add a selected product,
edit quantity, remove a line or clear the basket. F2/Escape focuses scanning, Ctrl+F search,
F4 cash tender, F6 remove and F9 complete. Focus returns to scanning after updates.

The basket shows normal, qualified tier and final unit prices, tier band, line total,
savings and discount reason. A blank manual final price uses the automatic highest qualifying
tier. A proposed positive price at or below that tier replaces the tier price for every
unit, without stacking. Settlement requires an owner PIN approval bound to the exact quote,
payment and stock preview, even when the signed-in user is an owner. A quantity change can
invalidate a proposed price; remove the proposal or enter an eligible price. Changed or
unavailable catalogue/tax data blocks payment with a localized error while leaving the cart
removable/clearable. Product/cost/tax/tier and stock revisions are checked again at settlement.

Choose one tender per sale. Cash shows configured rounding separately, rounded amount due,
tender and change. Card requires explicit confirmation that the external terminal approved
it and settles the exact unrounded total. Optional reference text is deliberately restricted
to short alphanumeric identifiers with a letter and at most eight digits; credential labels
and numeric card-like values are rejected before persistence. No card number, security code
or PIN field exists. No gateway is called. The underlying multi-payment aggregate remains
compatible; split tender is not exposed in this milestone.

Insufficient shared base stock warns with before/consumed/after quantities and base units.
Settlement requires explicit negative-stock consent, a reason and exact owner approval.
Pack and loose lines consume one base balance using their frozen conversion snapshots.
Owner authorization is enforced by the application transaction, independently of WPF controls.

Baskets are saved per authenticated user. On success the screen shows the saved receipt
identity, total, rounding, paid amount and change, and opens a new basket. Restart restores
the draft and last completed result. “Verify last completion” resubmits the saved command
and returns its original result; it does not create another receipt, payment or movement.
Full sales history, receipt rendering/printing, actual returns and close-day UI remain later
milestones. Sinhala receipt names are captured now for milestone 05.

## Atomicity and historical records

The basket UUID identifies the sale and immutable completion result. The SQLite transaction
contains sale/lines, payment, receipt allocation, converted stock posting, privileged and
completion audit, command result and replacement basket. Exact authenticated retries return
the committed result before rechecking today's catalogue or shift. Conflicting identity
reuse fails. A write failure rolls back the entire transaction, including receipt allocation.

New completed lines retain Sinhala/English/receipt names, cost, normal price, qualifying tier
price/band, actual charged price, tax and base-stock conversion. Manual lines also retain
reason and approver, and distinguish the qualified tier from the applied manual price.
New nullable fields do not invent snapshots for old bills. Forward SQLite and retained
PostgreSQL compatibility migrations leave historical amounts and names unchanged; no old
migration was edited. Stock and audit continue to use append-only infrastructure.

Money stays in integer minor units and quantities in thousandths. Changed money operations
reject arithmetic overflow; checkout also rejects drawer overflow before receipt allocation.
Expected drawer cash counts the applied cash payment, without subtracting change a second
time. Card amounts do not increase drawer cash; opening float remains separate from sales.

## Demonstrations covered by real portable tests

- A Sinhala product priced 1:100, 2:90, 3:85, 4:80 previews repeated scans 1–5 as
  100/180/255/320/400, then quantity 2 as 180. Fractional weight 1.250 is exact; 1.2501
  and fractions of whole-item units fail. Existing duplicate SKU/barcode and invalid-band
  tests remain passing. Drafts, prices and results survive reopening the database.
- Five tier items plus 1.250 kg at 150 produce 587.50, configured cash rounding to 588,
  tender 600, change 12 and drawer 1,588 from a 1,000 float. Stock becomes 95 items and
  8.750 kg, with historical tier and weight snapshots on the actual completed sale.
- Due 150, tender 200: paid 150, change 50 and expected cash 1,150 from float 1,000.
  Concurrent completion, restart and exact retry preserve one sale, receipt, payment,
  sale stock movement and completion audit. Catalogue changes do not alter that result.
- External card settles the exact total without cash rounding or drawer increase. Cash
  increment boundaries, rounded-zero cash and unsafe reference rejection are tested.
- Manual-price proposals require current, matching owner grants; expired/revoked grants,
  changed quotes, closed/reopened shifts, stale baskets and locked sessions fail safely.
- Two 25 kg bags become 50.000 kg. A real bill for 1.250 kg plus one sealed bag leaves
  23.750 kg. Unapproved shortage operations fail; exact reasoned owner override succeeds.
- An injected SQL failure after stock posting leaves no sale/payment, receipt counter,
  sale stock, command result or audit change. Retrying allocates the first receipt once.
- A populated milestone 03 database upgrades without changing its completed historical
  tier bill, Sinhala receipt name, cost, cash amount, tender or receipt identity.

## Recorded checks and outstanding gates

- Release solution build with warnings-as-errors: zero warnings and errors, including WPF
  and its real Windows dispatcher test assembly (cross-target compilation on Linux).
- Domain: 151 passed. Application/file-backed foundation: 48 passed. Architecture: 11 passed.
  Integration: 10 passed, 3 optional PostgreSQL runtime checks explicitly skipped.
- SQLite and retained PostgreSQL model snapshots have no pending migration changes. Fresh
  database initialization and populated legacy upgrade pass in the file-backed suite.
- All 247 English/Sinhala resource keys match without duplicates; Billing labels resolve.
  CI YAML parses; whitespace checks pass. Previous migration files are unchanged.
- The real WPF test now opens a shift, checks repeated scan tiers and quantity reduction,
  completes mixed weight/cash, simple cash and externally approved card bills, approves a
  manual final price through the PIN dialog, verifies drawer/stock, reopens and retries.
  It is compiled but has **not run on this Linux host**. Windows CI is configured to run it;
  no remote execution or CI result is claimed.
- Required before local merge: run the Release build, file-backed foundation and Desktop
  executable tests on Windows. Review Sinhala glyph shaping/translations, keyboard flow,
  focus, table/dialog scrolling and target-PC performance with a human cashier.
- Physical acceptance is separate and **not run**: test the shop's keyboard-mode scanner,
  especially rapid consecutive scans and focus after validation/payment. No scale or
  payment-terminal integration is introduced. Physical Sinhala printing remains milestone
  05 and needs the owner's template, printer/driver and paper width.

Required Windows runtime validation is unavailable here, so the authorized local merge is
withheld and work remains committed on `codex/checkout-and-payments`. `development` stays at
`be44df0`. No product decision blocks this source work; access to Windows/target-PC acceptance
is the specific unresolved requirement. Printing inputs are separate later-milestone inputs.
