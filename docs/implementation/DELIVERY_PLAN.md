# RetailCore — manageable delivery plan

**Recommended execution plan, revised 2026-11-09.** Build **8 milestones on 8 branches**, one at a time. Treat the 80 catalogue entries as acceptance checks inside these milestones. Each milestone ends in something the owner can demonstrate; it can contain several small commits and review checkpoints.

This replaces the earlier one-branch-per-feature recommendation. It does not silently remove the 69 Must requirements in FEATURES.md. All 69 are mapped once below, alongside the 3 foundation tasks. The 10 Should items and 1 Later item stay in a separate backlog. Reusing an optional capability already present in the code does not require exposing a full optional workflow.

## Three checkpoints

| Checkpoint | Finish through | What you can do |
|---|---|---|
| First usable checkout | 04 | Sign in, configure products/tiers, receive stock and save cash/card sales using test data. |
| Complete shop workflow | 07 | Print, find bills, handle returns/exchanges, close cash and export reports. |
| Ready for real trading | 08 | Install, back up, restore and verify the whole workflow with actual hardware and a supervised pilot. |

**Work on only the current milestone.** Merge it after its demonstration and relevant checks pass, then create the next branch from updated main. Do not open eight simultaneous branches or build placeholder implementations for later milestones. If a milestone becomes too large for one review, split its commits or create a temporary follow-up branch without returning to 80 standing tasks.

Treat localization, authorization, audit, historic snapshots, transaction safety and useful error handling as part of every feature. Milestone 01 supplies their reusable infrastructure, not blanket completion for future screens. Preserve the existing domain and architecture wherever they serve the current scope; no rewrite is required.

## Branch overview

| Order | Outcome | Branch |
|---|---|---|

| 01 | Open the app and sign in | `codex/app-foundation` |
| 02 | Create products and quantity prices | `codex/products-and-pricing` |
| 03 | Receive and explain stock | `codex/stock-management` |
| 04 | Complete a sale | `codex/checkout-and-payments` |
| 05 | Print and find a saved bill | `codex/receipts-and-history` |
| 06 | Return or exchange goods | `codex/returns-and-exchanges` |
| 07 | Reconcile and export a trading day | `codex/daily-close-and-reports` |
| 08 | Install safely and recover the shop | `codex/backup-installer-and-pilot` |

## Shared instructions for every milestone

Prepend this to the selected milestone prompt. The older [detailed feature prompts](FEATURE_PROMPTS.md) remain an acceptance reference; their individual branch recommendations are superseded.

```text
Read FEATURES.md, PLAN.md, ARCHITECTURE.md, docs/adr/0005-single-shop-windows-pos.md and applicable AGENTS.md. Implement only the milestone below on the suggested codex/ branch, starting from the merged previous milestone and preserving existing work. Reinspect current code before editing.

Use the existing .NET 10 domain/application/EF Core SQLite structure and a Windows WPF interface. Keep Money in integer minor units, Quantity in thousandths, historical completed records stable, stock/audit append-only and writes atomic. Use Sinhala/English resources for every new user-facing path and Sinhala receipts. Enforce owner authorization in application commands, not just UI visibility. Use forward-only migrations and meaningful tests for changed financial, inventory and security rules.

Build a complete reachable workflow for this milestone, in small reviewable commits. Run relevant warnings-as-errors builds, domain/application/integration tests and migration checks. Validate WPF on Windows and report physical hardware checks separately. Do not add cloud/sync, multi-tenancy, web dashboards, customer credit or payment-gateway integration. Do not mark work complete based on mocks if real Windows/hardware acceptance remains outstanding. Finish with the demonstrated behavior, checks run and any specific unresolved inputs.
```

## 01. Open the app and sign in

**Branch:** `codex/app-foundation`  
**Starts after:** none  
**Owns catalogue checks:** FND-01, FND-02, FND-03, CFG-01, CFG-06, UX-01, UX-03, SEC-01, SEC-02, SEC-04, SEC-06

**Work in these checkpoints:**

- Desktop shell and local database startup.
- Secure login, owner setup, owner/cashier permissions and reusable approval/lock dialogs.
- Settings, language preference, transaction/audit plumbing and appropriate CI.

**Implementation prompt:**

```text
Implement milestone 01: Open the app and sign in.

Build the .NET 10 WPF app with six navigation sections, one configured shop/terminal, local SQLite startup, owner/cashier PIN login, lockout, lock screen, owner approval and Sinhala/English resources. Add basic shop settings and per-user language preference. Set up transaction/retry and audit primitives for later workflows, plus SQLite tests and Windows build validation. Keep existing domain rules and historical migrations; deliberately transition legacy PostgreSQL/tenant CI only after local replacement checks work. Enable culture-aware UI despite the inherited invariant-globalization setting. Screens may be empty at this stage except login/settings; later milestones add their actual workflows.

Acceptance demonstration: On Windows, create the shop and first owner, switch language, restart and sign back in with the same saved settings. A cashier cannot access owner actions. Lockout survives restart. A file-backed transaction failure rolls back all writes.
```

**Boundary:** Only shell/login/settings translations and audit plumbing are complete here. Every later milestone must translate its new text and emit its own audit events.

## 02. Create products and quantity prices

**Branch:** `codex/products-and-pricing`  
**Starts after:** milestone 01 is merged  
**Owns catalogue checks:** CAT-01, CAT-02, CAT-04, CAT-10, CAT-12, PRC-01, PRC-02, PRC-03, PRC-04, PRC-05

**Work in these checkpoints:**

- Products/categories and bilingual data validation.
- Price bands, selection/calculation and snapshot persistence.
- Searchable product table and quantity-price preview.

**Implementation prompt:**

```text
Implement milestone 02: Create products and quantity prices.

Implement product/category create, edit and deactivate, Sinhala/English names with required Sinhala receipt name, unique SKU and a primary barcode, unit, cost, normal price and simple no-tax defaults. Add a searchable/sortable Products table. Build the four-band domain model (normal price plus up to three quantity breaks), validation, highest-qualifying selection, live price preview and historical tier/name snapshot fields. Allow valid fractional-unit thresholds and apply the selected final unit price to every unit. Provide reusable pricing operations for checkout; integrate them with real Billing in milestone 04. Stock columns use a signed ledger read projection, initially zero until receiving is delivered.

Acceptance demonstration: Create a Sinhala-named product at prices 1:100, 2:90, 3:85, 4:80. Preview quantities 1–5 as 100/180/255/320/400, then reduce quantity to 2 and see 180. Reject duplicate SKUs/barcodes and invalid bands. Restart without losing changes.
```

**Boundary:** One primary barcode per product is sufficient initially; multiple-barcode management and imports stay optional. Existing catalogue data needs an explicit migration path without invented Sinhala translations.

## 03. Receive and explain stock

**Branch:** `codex/stock-management`  
**Starts after:** milestone 02 is merged  
**Owns catalogue checks:** INV-01, INV-02, INV-03, INV-04, INV-05, INV-06, INV-07, INV-08, INV-09, INV-12

**Work in these checkpoints:**

- Immutable ledger, signed balances/history and base-unit/pack conversion.
- Receiving, corrections, damage/wastage and count confirmation.
- Low-stock visibility and reusable sale/return posting and shortage checks.

**Implementation prompt:**

```text
Implement milestone 03: Receive and explain stock.

Implement Stock receiving/opening balances, explicit base units and pack conversion, append-only movements, signed balances/history, owner-approved adjustments, damage/wastage, stock count with variance preview and stale-count protection, reorder thresholds and low-stock list. Warn on insufficient stock and enforce owner reasoned override at the write boundary. Snapshot cost/conversion factors and link sealed-pack and loose sales to one underlying balance without double-counting. Harden sale/return posting contracts with duplicate protection and saleable disposition support; milestone 04 wires sale completion and milestone 06 wires actual returns. Reuse the transaction/approval/audit infrastructure from milestone 01.

Acceptance demonstration: Receive two 25 kg bags as 50.000 kg. Confirm previewed consumption of 1.250 kg plus one sealed 25 kg pack leaves 23.750 kg. Post a reasoned adjustment/count, see the full history, and reject an unapproved negative-stock operation. Failure during posting rolls back stock and audit.
```

**Boundary:** Expiry batches are optional later work. The stock-posting building blocks are complete here; demonstrate the real sale/return effects again when their screens are delivered.

## 04. Complete a sale

**Branch:** `codex/checkout-and-payments`  
**Starts after:** milestone 03 is merged  
**Owns catalogue checks:** SAL-01, SAL-02, SAL-04, SAL-05, SAL-07, SAL-10, PAY-01, PAY-02, PAY-03, PAY-06, PAY-08, HW-03, UX-02, CAT-03, CSH-01

**Work in these checkpoints:**

- Scan/search/edit/weight workflows and tier/discount integration.
- Opening float, cash/change/rounding and external card settlement.
- Atomic completion, shortage approval, duplicate protection and historical snapshots.

**Implementation prompt:**

```text
Implement milestone 04: Complete a sale.

Build keyboard-first Billing: barcode scanner/manual input, search/category selection, repeated-scan aggregation, quantity edits/removal and manual weights to three decimals. Connect milestone 02 pricing and historical name/cost/tax/tier snapshots. Add owner-approved discounts with explicit tier interaction, cash tender/change, separate configured cash rounding and externally approved card recording. Add opening float and require an open shift for settlement. Complete sales idempotently in one SQLite transaction covering payments, receipt number, stock and audit. Preserve scan focus and actionable Sinhala/English errors. Store no card numbers, security codes or PINs. Accept one tender method per sale initially; keep the existing multi-payment model compatible.

Acceptance demonstration: Open a shift, scan the same product five times, enter 1.250 kg of another product, adjust quantities and complete cash/card examples. For Rs. 150 due and Rs. 200 cash, change is 50 and drawer cash rises by 150. Restart and retry completion: still one sale/receipt and one stock effect. Inject a write failure and see full rollback.
```

**Boundary:** This is the first usable checkout for internal testing. Physical receipts, returns, closing cash and backup recovery are still required before real trading. Two legitimate scans of the same code must count twice.

## 05. Print and find a saved bill

**Branch:** `codex/receipts-and-history`  
**Starts after:** milestone 04 is merged  
**Owns catalogue checks:** HW-01, HW-04, HW-05, HW-07, BO-04

**Work in these checkpoints:**

- Saved bill history, detail and exact receipt lookup.
- Sinhala rendering, preview, paper width and printer selection.
- Post-save print, bill barcode, failure recovery and historical reprint.

**Implementation prompt:**

```text
Implement milestone 05: Print and find a saved bill.

Implement bill history/detail with local-date filters and original financial snapshots. Add a shared Sinhala receipt preview/renderer, fixed owner-supplied custom template, configured printable width and one Windows printer adapter. Print a scannable bill barcode and support manual lookup. Save before printing, show failures and retry/reprint the existing bill without repeating payment or stock effects. Keep receipts Sinhala regardless of UI language and record reprints clearly. Build previews/contracts while awaiting actual template/printer details, but label those outputs provisional and leave physical acceptance pending.

Acceptance demonstration: Complete a bill, disconnect the printer, restart, find the bill and retry printing without creating another sale. Reprice/rename a product and reprint the original content unchanged. Physically verify Sinhala shaping, wrapping, totals and barcode on the actual printer.
```

**Boundary:** The custom template, printer model/driver and paper width are required for final physical sign-off; they do not block earlier milestones.

## 06. Return or exchange goods

**Branch:** `codex/returns-and-exchanges`  
**Starts after:** milestone 05 is merged  
**Owns catalogue checks:** RET-01, RET-02, RET-03, RET-04, RET-05, RET-06, RET-08, RET-09, RET-10

**Work in these checkpoints:**

- Original-bill partial returns, exact credit limits and dispositions.
- Owner-approved void eligibility and financial/stock reversal protections.
- Atomic linked exchanges, collect/refund/zero settlement and Sinhala receipts.

**Implementation prompt:**

```text
Implement milestone 06: Return or exchange goods.

Implement original-bill lookup/scan, selected partial returns, authenticated owner approval/reason and saleable versus damaged disposition. Harden refund limits using actual prior refunded amounts as well as quantities; use original discounted/tier prices without repricing kept items or exceeding money originally collected. Add eligible reasoned voids with no double reversal after returns. Link return, replacement sale and difference settlement atomically; replacement goods use current pricing. Represent exchange credit explicitly rather than fabricating cash/card tenders, default refunds to original tender and record externally handled card refunds. Print linked Sinhala return/exchange documents after commit and allow safe retries.

Acceptance demonstration: Return Rs. 100 against replacements of Rs. 150/80/100: collect 50/refund 20/move zero. Test pure refund, partial tier-priced returns, damaged items and a Rs. 0.05 line returned in three pieces totaling exactly 0.05. Inject failure midway through an exchange: no half-committed operation.
```

**Boundary:** No store/customer credit. Optional split-tender UI remains deferred, but return math must still handle valid historical multiple payments without assuming only one stored Payment row.

## 07. Reconcile and export a trading day

**Branch:** `codex/daily-close-and-reports`  
**Starts after:** milestone 06 is merged  
**Owns catalogue checks:** CSH-04, CSH-05, BO-05, BO-06, BO-07, BO-08, BO-10, BO-12, BO-13, CAT-11

**Work in these checkpoints:**

- Cash movements and close/count/variance.
- Start with daily sales and tender reconciliation, then reuse projections for product/profit/stock/audit views.
- Shared export pipeline, product exports and printable reports.

**Implementation prompt:**

```text
Implement milestone 07: Reconcile and export a trading day.

Add owner-approved reasoned cash pay-in/out and counted-cash shift close with persisted expected cash/variance. Implement privileged-change history, date-range gross/discount/return/net sales, product/category sales, estimated historical-cost gross profit, stock balance/value/movement/low-stock and actual cash/card/refund reconciliation. Fix ShiftRepository.SalesSummaryAsync so it does not subtract change from Payment.Amount a second time. Count return and replacement turnover once each and actual exchange settlement once. Add shared Sinhala-safe CSV/XLSX/PDF exports, all/filtered product export and PDF price lists, labeled with local period and filters. Preserve text barcodes and typed numbers/dates; neutralize formula-like spreadsheet text.

Acceptance demonstration: Reconcile a day containing cash change, card sales, returns, all exchange directions, wastage and pay-in/out to physical counted cash. Verify historical profit after catalogue cost changes. Open filtered Sinhala CSV/XLSX/PDF output, check leading zeroes and formula protection, and test shop-local midnight boundaries.
```

**Boundary:** Keep reports as simple tables with shared filters/export controls. Do not build a dashboard designer or require a separate branch for each report. This milestone can use several reviewable commits.

## 08. Install safely and recover the shop

**Branch:** `codex/backup-installer-and-pilot`  
**Starts after:** milestone 07 is merged  
**Owns catalogue checks:** OPS-01, OPS-07

**Work in these checkpoints:**

- Daily/manual backup, second destination, failure visibility and verified restore.
- Windows installation and controlled upgrade/recovery.
- Whole-workflow verification and owner-supervised pilot.

**Implementation prompt:**

```text
Implement milestone 08: Install safely and recover the shop.

Implement manual and daily backups through SQLite live backup API, retention, an external/USB second destination and visible last-success/failure status. Add owner-approved verified restore with exclusive database access, compatible schema/integrity checks and a safety backup. Package a Windows installer with application data outside installation files and controlled forward-only upgrades preceded by backup. Complete translation/keyboard review, real printer/scanner validation, failure recovery and performance measurements on the intended PC. Run a full pilot week only after install, restore, printing, stock and cash checks pass. Report any unverified external inputs explicitly.

Acceptance demonstration: Install on a clean Windows PC, restore a backup and compare bills, stock and cash. Test corrupt backups, unavailable USB, interrupted restore and failed upgrade. Then run a full pilot week and reconcile each trading day with the owner.
```

**Boundary:** This is the release gate for real trading. Earlier milestones are demonstrations using test data; backups and recovery are not optional release work.

## Optional backlog — leave out of the eight milestones

Choose a follow-up only after the main workflow works or when the pilot demonstrates a real need. These are not eleven new commitments to schedule now.

| Later improvement | Catalogue IDs |
|---|---|
| Faster catalogue setup and additional product codes | CAT-06 import; CAT-07 multiple barcodes |
| Checkout conveniences | SAL-11 held baskets; SAL-12 separate price check; PAY-05 split cash/card |
| Expiry tracking | INV-11 batch/expiry allocation; BO-14 near-expiry reports |
| Extra printing/hardware | HW-02 cash drawer; HW-09 barcode labels/shelf tags |
| Easier troubleshooting | OPS-02 support logs/export |
| Future scale connection, outside version 1 | HW-06 direct weighing-scale integration |

Basic actionable error handling remains part of core work; only the dedicated diagnostic/support package is deferred. Multiple payment rows already supported by the domain should remain valid even while the split-payment screen is deferred.

## Scope and estimates

Eight milestones reduce coordination and repeated integration work; they do not make the underlying financial/inventory requirements disappear. Avoid calendar promises until milestones 01–02 establish the desktop/database pace and the actual receipt template/printer are inspected. If less functionality is needed for a first release, explicitly revise the Must scope together rather than calling an incomplete shop workflow production-ready.

The printer template, model/driver and paper width are inputs for milestone 05. Develop earlier milestones without waiting for them. Keep all real trading until after verified restore, cash reconciliation and physical receipt checks in milestone 08.
