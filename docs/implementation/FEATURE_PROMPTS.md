# RetailCore POS — implementation prompts and feature branches

> **Execution plan updated:** use [the 8-milestone delivery plan](DELIVERY_PLAN.md). The per-feature branches below are reference suggestions, not the recommended work queue.

Prepared from the repository on 2026-11-09. This guide covers **all 80 feature IDs** in `FEATURES.md` (69 Must, 10 Should, 1 Later), plus **3 prerequisite implementation tasks**. Branch names are suggestions; no branches have been created and no application code has been changed.

## What the project currently contains

- .NET 10 domain entities/value objects for products, quantities, money, sales, payments, refunds, stock, shifts and security; shared EF mappings; SQLite and legacy PostgreSQL migrations.
- Application repository/unit-of-work interfaces and stock/audit event handlers. There is no WPF desktop project or complete user-facing feature workflow yet.
- Domain, architecture and provider integration tests. The integration fixture currently initializes both SQLite and PostgreSQL, even when a caller needs SQLite alone.
- Revised product scope: one shop, initially one Windows checkout PC, SQLite, Sinhala/English interface, Sinhala receipts. Earlier tenancy/sync comments are historical and must not redefine the product.
- Specific code concerns reflected below: `Product.Name` is currently a single field; pricing bands are absent; `SearchAsync` only searches name/SKU; `OnHandAsync` clamps negative stock; refund stock posting is unconditional; partial-refund remainder calculation estimates earlier refund money from quantity; shift summaries subtract change from already-applied cash amounts; and invariant globalization is currently enabled.

This is a source review for planning. The .NET SDK is available, but builds/tests were not run for this documentation-only task. Existing “Foundation” labels mean reusable pieces exist, not that a usable feature is finished. Reinspect code after each merged branch because these observations will change.

## How to use the prompts

Start each implementation chat with the shared instructions below, followed by the chosen feature prompt. Use a separate short-lived branch for each task, created from the updated main branch after its prerequisites merge. “Prerequisites” describe suggested implementation order, not branches to merge blindly. Optional features must not become required for Must features: keep optional adapters/contracts and meaningful fallbacks where necessary.

If you prefer fewer PRs, related feature IDs may be delivered together, but retain every listed acceptance check. Do not implement all 80 features in one change. Hardware-specific work can deliver interfaces, previews and test fixtures first; final physical sign-off still needs the real printer/template or scale.

### Shared implementation instructions — copy once into each implementation chat

```text
Work in the existing RetailCore repository. Read FEATURES.md, PLAN.md, ARCHITECTURE.md and applicable AGENTS.md before editing; ADR 0005 defines the current single-shop scope. Inspect the current implementation and merged prerequisites, then complete the feature described below on its suggested codex/ branch, respecting existing local changes. Reuse existing domain rules and keep dependencies Domain → Application → Infrastructure/Desktop in their intended direction; keep domain independent of UI/persistence and Application independent of Infrastructure.

Deliver the application service/query, domain changes, forward-only SQLite migration, DI wiring and reachable WPF UI needed for this feature. Do not add unused layers when no change is necessary. Keep Money as integer minor units and Quantity as integer thousandths; preserve historical bills, prices, costs and names. Keep stock/audit append-only and financial/stock writes atomic. Enforce authorization in application services. Use Sinhala/English resources for user-facing messages and always-Sinhala receipts. Preserve Unicode and text barcodes.

Do not add cloud hosting, multi-tenancy, sync, a web back office, customer credit or payment-gateway integration. Keep historical applied migrations intact. Keep real databases, backups, credentials and generated customer exports out of Git. Add meaningful tests for changed financial/stock/security rules and integration boundaries. Run relevant warnings-as-errors build, tests and migration checks; use Windows validation for WPF and actual hardware for physical claims. Report exactly what passed, failed or could not be run, plus changed behavior and remaining hardware inputs. Update relevant documentation to match delivered behavior. Do not claim the feature is complete while its authorized essential work remains unfinished.
```

## Suggested delivery order

1. **Foundation and access:** FND-01 → UX-01 → FND-03; SEC-01 → SEC-02 → SEC-06 → SEC-04; CFG-01, CFG-06, UX-03; FND-02 before financial writes.
2. **Products and pricing:** CAT-02, CAT-12, CAT-07, CAT-01; CAT-03/CAT-04; PRC-01 → PRC-04 → PRC-02 → PRC-03/PRC-05; INV-01/INV-02 → CAT-10; BO-10 → CAT-11. Import CAT-06 can follow.
3. **Stock:** INV-12 → INV-04; INV-05/INV-06/INV-07/INV-08/INV-09; complete INV-03 and RET-06 contracts before return posting. Optional INV-11 comes after core stock and return dispositions.
4. **Checkout and receipts:** SAL-10 → SAL-04/SAL-05/SAL-07; HW-03 → SAL-01/SAL-02; PAY-08/PAY-02/PAY-06 → PAY-01/PAY-03; CSH-01; HW-07/HW-04 → HW-01 → HW-05; UX-02. Build BO-04 early for bill history. Optional hold, price-check and split tender follow.
5. **Returns and cash:** RET-03/RET-05/RET-06 → RET-01/RET-02; RET-09 → RET-08 → RET-10; RET-04; CSH-05 → BO-13 → CSH-04. Settle return/exchange query contracts before building final reports.
6. **Reports and rollout:** BO-05/BO-06/BO-07/BO-08/BO-12 plus BO-10 exports; OPS-07 → OPS-01; OPS-02; real hardware, usability and restore validation. Optional BO-14/HW-02/HW-09 follow; HW-06 is Later.

The catalogue below keeps the original feature grouping for easy lookup. The delivery order above and each entry's prerequisites determine where to start. Receipt preview and original-bill lookup share a parser/contract; neither should depend on the other feature's whole screen implementation.

## Prerequisite tasks

### FND-01 — Single-shop SQLite runtime

**Branch:** `codex/single-shop-sqlite-runtime`  
**Prerequisites:** None

```text
Implement FND-01: Single-shop SQLite runtime.

Implement the deployment transition in ARCHITECTURE.md and ADR 0005. Provide one durable local shop/terminal identity, startup/database initialization and Windows application-data path selection. Make SQLite the only desktop runtime provider and enforce WAL, synchronous FULL and foreign keys on every applicable runtime connection. Remove the need for tenant/server onboarding or sync, retain historical migrations/ADRs and compatibility columns, and deliberately retire legacy runtime dependencies without breaking retained tests. Do not expose new business screens in this task.

Acceptance: Create and reopen a file database, restart without identity changes, migrate an existing database without losing data and verify PRAGMAs on newly opened connections. Confirm no PostgreSQL, Docker or internet connection is needed for normal local operation.
```

### FND-02 — Transactional commands and retry safety

**Branch:** `codex/transactional-command-foundation`  
**Prerequisites:** FND-01

```text
Implement FND-02: Transactional commands and retry safety.

Implement application transaction/idempotency primitives around the existing IUnitOfWork, RetailCoreDbContext, receipt allocator and domain event handlers. Provide scoped explicit transaction orchestration for multi-aggregate workflows, ensure the real event dispatcher is wired, and fail safely if required events cannot be handled. Review async/save paths and retry after event draining so stock/audit events are neither lost nor duplicated. Support persisted operation identities for sale, return, exchange and cash movement commands without adding an outbox/sync service.

Acceptance: Use file-backed SQLite integration tests with handler, save and commit-failure injection. Confirm receipt increments, business records, stock and audit commit/rollback together, and retries or uncertain commit results never create a second operation.
```

### FND-03 — Single-shop CI and Windows validation

**Branch:** `codex/sqlite-windows-validation`  
**Prerequisites:** FND-01, UX-01

```text
Implement FND-03: Single-shop CI and Windows validation.

Update CI deliberately for the revised single-shop product after replacement checks work. Split the current DatabaseFixture so SQLite tests can run without automatically starting PostgreSQL. Keep meaningful domain/layering/immutability tests, replace tenant-only required assertions with relevant local guarantees, and add required SQLite migration/transaction tests plus Windows WPF build validation. Preserve historical PostgreSQL migrations and any useful optional legacy tests; drop provider-parity requirements only as part of this documented transition.

Acceptance: Required checks build with warnings as errors, run domain/architecture and file-backed SQLite tests without Docker, check local pending migrations and build the desktop on Windows. Report any environment-dependent/manual validation separately, and do not disable failing meaningful checks to make CI green.
```

## Checkout

### SAL-01 — Scan barcode to add an item; repeated scans combine its quantity

**Branch:** `codex/sal-01-barcode-checkout`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** UX-01, CAT-07, PRC-02, SAL-10, HW-03

```text
Implement SAL-01: Scan barcode to add an item; repeated scans combine its quantity.

Connect barcode lookup to Billing through an application command. Preserve barcode strings and leading zeroes, reject inactive or unknown products with localized feedback, and aggregate repeated scans into the same compatible product line before selecting its quantity tier. Keep scanner focus ready after successful scans and route each completed input event once. Two physical scans of the same barcode must still count as two units; do not debounce legitimate repeated scans by code alone.

Acceptance: Scan the same product five times and see quantity 5 at the qualifying tier; scan an alternate barcode for that product; check unknown/inactive products and measure the under-100 ms target with 10,000 products on the intended PC.
```

### SAL-02 — Search by product name, SKU or barcode; quick category selection

**Branch:** `codex/sal-02-product-search-checkout`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** UX-01, CAT-01, CAT-02, CAT-12, SAL-10

```text
Implement SAL-02: Search by product name, SKU or barcode; quick category selection.

Add keyboard-accessible product search by Sinhala/English name, SKU and any barcode, plus category selection. Extend ProductRepository.SearchAsync or add a read-only query service because the existing search only checks Name and SKU. Return bounded results and add the selected active product through the same basket command as barcode scanning.

Acceptance: Find a product by each supported field, including a leading-zero alternate barcode; switch categories; confirm inactive products are excluded and measure the under-150 ms search target over 10,000 products.
```

### SAL-04 — Edit quantities and remove items before payment

**Branch:** `codex/sal-04-basket-quantity-edit`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** UX-01, SAL-10, PRC-03

```text
Implement SAL-04: Edit quantities and remove items before payment.

Expose Sale quantity-change and line-removal operations through Billing commands. Accept positive whole quantities for Each/Pack and up to three decimal places for fractional units. Recalculate tiers, discounts, tax and totals immediately; disallow edits to completed records and edits incompatible with payments already recorded.

Acceptance: Increase and decrease a tier-priced line, remove a line, reject zero/negative/invalid precision, and verify that a completed bill remains unchanged.
```

### SAL-05 — Sell weighed goods to three decimal places, e.g. 1.250 kg

**Branch:** `codex/sal-05-weighed-goods-checkout`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** UX-01, CAT-01, SAL-10, PRC-02

```text
Implement SAL-05: Sell weighed goods to three decimal places, e.g. 1.250 kg.

Add manual weight entry for units that allow fractions using the existing integer-thousandths Quantity. Show the selling unit, applied unit price and rounded line amount, and handle Sinhala/English entry without floating-point arithmetic. Apply tier thresholds in the selling unit. Direct scale integration is excluded from this task.

Acceptance: Sell 1.250 kg, test a fractional tier boundary, reject a fourth decimal place and fractional Each/Pack, and confirm correct line rounding and stock deduction.
```

### SAL-07 — Owner-authorized manual discount with reason; no silent stacking with tier prices

**Branch:** `codex/sal-07-owner-approved-discounts`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** SEC-04, SEC-06, SAL-10, PRC-03

```text
Implement SAL-07: Owner-authorized manual discount with reason; no silent stacking with tier prices.

Implement manual discounts with an authenticated owner approval and mandatory reason. Replace the old default policy allowing unauthorised discounts where it conflicts with this requirement. Show the final charged unit/line price and make any interaction with quantity tiers explicit; require fresh approval for changed override terms and never silently stack discounts.

Acceptance: A cashier cannot discount without approval; cancellation leaves prices unchanged; approved normal and tier-priced examples show clear before/after totals and an audit record without credentials.
```

### SAL-10 — Calculate totals from items; preserve actual prices, costs and tax on the bill

**Branch:** `codex/sal-10-sale-totals-snapshots`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** FND-02, CAT-03, CAT-04, CAT-12, PRC-05

```text
Implement SAL-10: Calculate totals from items; preserve actual prices, costs and tax on the bill.

Create basket application services around Sale and SaleLine, keeping calculations in the domain. Sum individually rounded lines; persist historical Sinhala/English descriptions, SKU, unit, charged prices, cost, discounts and tax treatment. Expose a consistent totals projection for Billing and receipts; use integer Money and Quantity throughout.

Acceptance: Reload a mixed packaged/weighed discounted bill and verify exact totals; change current names, prices, costs and tax and confirm the saved bill is unchanged.
```

### SAL-11 — Hold a basket and recall it

**Branch:** `codex/sal-11-hold-recall-basket`  
**Priority / current catalogue status:** Should / Foundation  
**Prerequisites:** SAL-10, SAL-04, SEC-02

```text
Implement SAL-11: Hold a basket and recall it.

Expose existing hold/recall domain operations and HeldSalesAsync in Billing. Persist held baskets across restarts, show useful identifying details, prevent two active recalls of the same basket, and define visible current-price/tier refresh on recall with confirmation before charging changed prices. Held baskets reserve neither stock nor receipt numbers.

Acceptance: Hold two baskets, restart, recall the right one, handle a deactivated/repriced product clearly, and confirm no payments, stock deductions or completed receipt numbers were created while holding.
```

### SAL-12 — Price check without adding to the basket

**Branch:** `codex/sal-12-price-check`  
**Priority / current catalogue status:** Should / Planned  
**Prerequisites:** SAL-02, PRC-02

```text
Implement SAL-12: Price check without adding to the basket.

Add a price-check action using product lookup without mutating the current basket. Display Sinhala/English names, selling unit, normal price, quantity bands and the price for an entered quantity. Return directly to the previous scan focus on close.

Acceptance: Check barcode and searched products at several tier quantities and verify that basket quantity, payments and stock do not change.
```

## Payments

### PAY-01 — Cash tender, amount due and change

**Branch:** `codex/pay-01-cash-tender-change`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** SAL-10, PAY-02, PAY-06

```text
Implement PAY-01: Cash tender, amount due and change.

Add cash settlement UI and application commands using Payment.Amount as the amount applied and Tendered as cash received. Show amount due, tendered and change clearly, validate sufficient tender and configured currency, and commit only after confirmation. Keep drawer arithmetic based on net cash retained.

Acceptance: For Rs. 150 due and Rs. 200 tendered, show Rs. 50 change and Rs. 150 retained; reject insufficient cash and ensure cancelling payment leaves the bill open.
```

### PAY-02 — Cash rounding shown separately using the configured cash increment

**Branch:** `codex/pay-02-cash-rounding`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** SAL-10, CFG-01

```text
Implement PAY-02: Cash rounding shown separately using the configured cash increment.

Implement configured cash increment rounding with its own stored adjustment and visible receipt/payment row. Define the base and sign for all-cash and mixed-tender settlement; do not round card-only bills or apply the adjustment twice. Preserve original rounding for later returns and give exchanges their own settlement rounding.

Acceptance: Test values below/at/above midpoint, card-only totals, mixed tender and repeated recalculation; verify persisted totals equal applied payments and the adjustment survives reload.
```

### PAY-03 — Record externally approved card payments and an optional reference

**Branch:** `codex/pay-03-external-card-payments`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** SAL-10, PAY-06, PAY-08

```text
Implement PAY-03: Record externally approved card payments and an optional reference.

Add recording of externally approved card payments with amount and optional approval reference. Require the cashier to confirm external approval; store only minimal non-sensitive payment metadata, allow no card over-tender/change, and make clear that this application does not call a card processor.

Acceptance: Record an approved card payment, cancel an unapproved one, reject excess amount, and verify no network/payment-gateway integration or sensitive card fields are introduced.
```

### PAY-05 — Split cash/card payment

**Branch:** `codex/pay-05-split-cash-card`  
**Priority / current catalogue status:** Should / Foundation  
**Prerequisites:** PAY-01, PAY-03

```text
Implement PAY-05: Split cash/card payment.

Support multiple cash/card tenders against one bill. Show applied amounts and remaining balance, allow cash over-tender only as change, handle cash rounding consistently on the cash portion, and disable completion until balanced. Record enough tender detail for original-tender refunds without representing exchange credit as cash/card.

Acceptance: Complete a Rs. 150 bill with Rs. 100 card plus Rs. 50 cash, test insufficient/excess card, cash change and cancellation, then verify the daily reconciliation and return allocation.
```

### PAY-06 — Prevent completion with an unpaid balance; prevent duplicate completion

**Branch:** `codex/pay-06-atomic-sale-completion`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** FND-02, SAL-10, INV-03, SEC-06

```text
Implement PAY-06: Prevent completion with an unpaid balance; prevent duplicate completion.

Implement the completion command with unpaid-balance validation and a stable operation identifier. Commit the completed sale, payments, local receipt-counter increment, stock and audit in one SQLite transaction. Disable repeated submit in the UI and enforce duplicate protection in the application/database so retry after an uncertain outcome returns the existing result.

Acceptance: Double-click completion, retry after a simulated lost success response, and inject failures before commit; observe exactly one completed sale/receipt/stock posting or a full rollback, with no completion while unpaid.
```

### PAY-08 — Do not collect or store card numbers, security codes or PINs

**Branch:** `codex/pay-08-payment-data-minimization`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** UX-01, SEC-06

```text
Implement PAY-08: Do not collect or store card numbers, security codes or PINs.

Enforce the existing minimal card-recording model across commands, UI, logs, support exports and receipts. Provide no inputs or fields for card numbers, security codes or PINs. Restrict the optional reference to the external approval/reference identifier and use clear guidance and validation to avoid collecting sensitive payment data.

Acceptance: Inspect all payment entry and output paths; submit invalid reference content and confirm it is rejected or safely handled without recording it in errors, audit details or support output.
```

## Products and categories

### CAT-01 — Add/edit/deactivate products: unique SKU, barcode, category, unit, cost, normal price

**Branch:** `codex/cat-01-product-management`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** UX-01, CFG-01, SEC-01, CAT-02, CAT-12

```text
Implement CAT-01: Add/edit/deactivate products: unique SKU, barcode, category, unit, cost, normal price.

Build owner-controlled product create/edit/deactivate workflows around Product. Support unique SKU/barcodes, category, selling unit, cost and normal price with currency/quantity validation, and database uniqueness constraints. Fill missing domain edit operations rather than using public state setters; preserve referenced historical products and refresh affected list/search views.

Acceptance: Create/edit/deactivate products, attempt duplicate SKU/barcodes and invalid money/unit values, and confirm old sales still load and inactive products cannot be newly sold.
```

### CAT-02 — Simple category management

**Branch:** `codex/cat-02-category-management`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** UX-01, SEC-01, FND-01

```text
Implement CAT-02: Simple category management.

Add simple category list/create/rename/reorder/deactivate services and Products UI around Category. Keep selection straightforward, preserve product links and reporting history, and require a usable category when activating products. Avoid building a complex hierarchy editor for version 1.

Acceptance: Create and reorder categories, rename one containing products, deactivate one with a clear effect on selection, and confirm existing product/history relationships remain valid.
```

### CAT-03 — Preserve cost at sale time for estimated gross profit

**Branch:** `codex/cat-03-historical-cost-snapshots`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** FND-01

```text
Implement CAT-03: Preserve cost at sale time for estimated gross profit.

Complete historical cost propagation from catalogue to sale lines and return reversals. Reuse SaleLine.UnitCost and CostAmount, validate currency and non-negative cost, persist/reload the snapshot, and define zero/missing-cost handling visibly for estimated profit. Later catalogue/receiving cost updates must not rewrite completed sale costs.

Acceptance: Sell at a recorded cost, change catalogue cost, reload the sale and return part of it; confirm both profit and return cost reversal use the original cost.
```

### CAT-04 — Reuse existing tax support where applicable; do not require tax setup for every product

**Branch:** `codex/cat-04-simple-tax-defaults`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** FND-01, CFG-01

```text
Implement CAT-04: Reuse existing tax support where applicable; do not require tax setup for every product.

Reuse TaxRate and TaxTreatment while making product setup usable without manual tax configuration for every item. Implement a deliberate no-tax/default treatment and simple optional explicit tax assignment, preserving historical rate snapshots and currency rounding. Do not introduce assumptions about statutory rates.

Acceptance: Create and sell a default untaxed product without extra setup, sell an explicitly taxed product, and confirm tax changes do not alter historical bills or refunds.
```

### CAT-06 — CSV/Excel product import with preview, validation and duplicate handling

**Branch:** `codex/cat-06-product-import-preview`  
**Priority / current catalogue status:** Should / Planned  
**Prerequisites:** CAT-01, CAT-07, PRC-04, BO-10

```text
Implement CAT-06: CSV/Excel product import with preview, validation and duplicate handling.

Implement CSV/XLSX product import with a preview and row-level validation for Unicode names, text barcodes, units, currency amounts and tier bands. Let the owner choose duplicate handling explicitly and confirm creates/updates before writing. Use a transaction or documented resumable batches with a clear result summary; do not modify products on preview.

Acceptance: Import Sinhala names and leading-zero barcodes, detect duplicate/conflicting rows and invalid tiers, cancel preview with no writes, and verify confirmed import counts and failure behavior.
```

### CAT-07 — Multiple barcodes for a product; preserve leading zeroes

**Branch:** `codex/cat-07-multiple-product-barcodes`  
**Priority / current catalogue status:** Should / Foundation  
**Prerequisites:** FND-01

```text
Implement CAT-07: Multiple barcodes for a product; preserve leading zeroes.

Complete management and indexed lookup of multiple barcodes per product using ProductBarcode. Treat codes as strings, preserve leading zeroes, enforce shop-wide ownership of each barcode in SQLite as well as application validation, and handle removal without invalidating historical bill data. CAT-01 owns the main editor and can consume this service.

Acceptance: Assign multiple codes, find the product by each, reject a code already owned by another product, and verify leading-zero round trips through persistence and search.
```

### CAT-10 — Searchable/sortable product table showing prices, tiers and current stock

**Branch:** `codex/cat-10-product-stock-table`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** CAT-01, PRC-01, INV-02

```text
Implement CAT-10: Searchable/sortable product table showing prices, tiers and current stock.

Build a searchable/sortable Products table showing SKU, Unicode names, category, unit, cost, normal price, tier summary, activation and signed current stock. Add bounded/paged read projections and useful filters without loading entire aggregates or issuing one stock query per row. Keep editing and export available from the selected results.

Acceptance: Filter/sort a 10,000-product catalogue, show a negative stock balance accurately, refresh after receiving/repricing, and confirm the filter selection can be reused by export.
```

### CAT-11 — Export all products or filtered results to Excel/CSV; printable PDF price list

**Branch:** `codex/cat-11-product-exports-price-list`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** CAT-10, BO-10

```text
Implement CAT-11: Export all products or filtered results to Excel/CSV; printable PDF price list.

Add owner-facing export of all products or current filtered results to CSV/XLSX and a printable PDF price list. Include clear columns for names, SKU, text barcodes, units, normal/tier prices and appropriate stock fields; label filters and date. Use the shared export pipeline for Unicode, numeric types and spreadsheet formula protection.

Acceptance: Export filtered and complete lists, open CSV/XLSX in Windows Excel, verify leading-zero barcodes and formula-like names, and inspect a PDF with long Sinhala names and page breaks.
```

### CAT-12 — Sinhala and English product names; Sinhala receipt name required for active products

**Branch:** `codex/cat-12-bilingual-product-names`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** FND-01

```text
Implement CAT-12: Sinhala and English product names; Sinhala receipt name required for active products.

Extend Product and EF mappings with explicit Sinhala and English names and a required Sinhala receipt name before activation. Add a forward-only migration that preserves existing Name data without inventing translations; surface legacy products needing completion. Define fallback behavior for interface display and snapshot both relevant descriptions on new sale lines.

Acceptance: Migrate a populated database, reject activation without a Sinhala receipt name, save/reload Unicode text and confirm old bill descriptions remain stable after renaming.
```

## Quantity pricing

### PRC-01 — Four total quantity-price bands: normal price plus up to three quantity breaks

**Branch:** `codex/prc-01-quantity-price-bands`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** FND-01

```text
Implement PRC-01: Four total quantity-price bands: normal price plus up to three quantity breaks.

Add a domain pricing model to Product with normal price plus zero to three quantity breaks, four bands total. Represent thresholds using Quantity and prices using Money in the selling currency/unit. Persist bands through EF and a forward-only SQLite migration and expose them in the product editor when available; keep selection and validation reusable by subsequent pricing features.

Acceptance: Round-trip zero, one and three breaks, reject a fourth break, and preserve existing normal prices when migrating products with no bands.
```

### PRC-02 — Automatically apply the highest qualifying band to every unit of that product

**Branch:** `codex/prc-02-quantity-tier-selection`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** PRC-01, PRC-04

```text
Implement PRC-02: Automatically apply the highest qualifying band to every unit of that product.

Implement a domain service selecting the highest qualifying quantity band after aggregating the product's compatible basket quantity. Apply that band's final unit price to every unit, using the selling unit for fractional thresholds. Keep pricing deterministic and shared by checkout, search price-check and exchange replacement goods.

Acceptance: For bands 1:100, 2:90, 3:85, 4:80, verify totals 100/180/255/320/400 at quantities 1–5, repeated-scan aggregation and fractional boundary cases.
```

### PRC-03 — Recalculate tiers when basket quantity changes; show applied unit price and savings

**Branch:** `codex/prc-03-live-tier-recalculation`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** PRC-02

```text
Implement PRC-03: Recalculate tiers when basket quantity changes; show applied unit price and savings.

Wire tier selection into basket add, quantity edit and removal commands. Display the selected unit price, band and savings relative to normal price; update totals immediately when quantities cross boundaries in either direction. Respect explicit approved manual discount terms and require reapproval when changed terms demand it.

Acceptance: Move quantity 3→4→2 and confirm totals 255→320→180, correct savings, no stale price after removal and no hidden manual-discount stacking.
```

### PRC-04 — Validate ascending positive thresholds and positive non-increasing prices

**Branch:** `codex/prc-04-price-band-validation`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** PRC-01

```text
Implement PRC-04: Validate ascending positive thresholds and positive non-increasing prices.

Enforce at most three positive, strictly ascending break thresholds and positive non-increasing prices in the same currency. Normal price applies below the first break; allow fractional-unit breaks below 1.000 where valid and require whole-unit breaks for Each/Pack. Reject duplicate/unreachable bands and expose localized field errors. Enforce the same rules for editor, import and application commands.

Acceptance: Reject duplicate/descending/zero thresholds, increasing/zero/negative prices, mixed currency and excess bands; accept valid Each and fractional-unit schedules at exact boundaries.
```

### PRC-05 — Snapshot charged price and applied tier; later price edits never alter old bills

**Branch:** `codex/prc-05-sale-price-tier-snapshots`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** PRC-01, CAT-12

```text
Implement PRC-05: Snapshot charged price and applied tier; later price edits never alter old bills.

Extend sale-line persistence to snapshot normal/reference price, actual charged unit price and the applied band's threshold/identity along with historical names and cost. Keep completed snapshots immutable, preserve old rows through explicit migration defaults, and let receipts/returns/reporting consume historical values without querying current tier definitions.

Acceptance: Complete a tier-priced sale, edit/delete current bands and rename the product; reload/reprint/return it and verify the original prices and applied-tier details are retained.
```

## Stock and receiving

### INV-01 — Explain stock through an append-only movement history

**Branch:** `codex/inv-01-append-only-stock-ledger`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** FND-01, FND-02

```text
Implement INV-01: Explain stock through an append-only movement history.

Harden the existing StockMovement ledger for the local runtime. Permit corrections only via linked compensating entries, enforce append-only behavior through EF paths and forward-only SQLite triggers/constraints where appropriate, and retain source document, actor, time, reason and quantity. Review async save and raw database protections rather than relying on comments.

Acceptance: Post and reload movements, attempt update/delete through EF and direct SQL, and verify a compensating entry changes the balance while preserving the original history.
```

### INV-02 — Display stock balance and movement history per product

**Branch:** `codex/inv-02-stock-balance-history`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** INV-01, UX-01

```text
Implement INV-02: Display stock balance and movement history per product.

Add Stock read services and a per-product balance/history screen with date/kind/source filters and links to source documents. Use signed ledger totals: OnHandAsync currently clamps negatives, while SignedOnHandAsync preserves them. Show negative balance honestly and distinguish historical running balance from current balance.

Acceptance: Receive, sell, return and adjust a fractional product; verify signed totals, chronology, filters and source links, including a genuinely negative balance.
```

### INV-03 — Sales deduct stock; accepted saleable returns restore stock

**Branch:** `codex/inv-03-atomic-sale-return-stock`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** FND-02, INV-01

```text
Implement INV-03: Sales deduct stock; accepted saleable returns restore stock.

Verify and complete stock posting from SaleCompleted, SaleVoided and RefundIssued inside the same transaction. Introduce duplicate-posting protection tied to source document/line or equivalent identifiers. Provide a disposition-aware event contract consumed by RET-06, avoiding unconditional restocking of non-saleable returns and avoiding double reversal after previous refunds.

Acceptance: Inject handler/save failures, retry commands, void eligible bills and return mixed dispositions; see one consistent ledger effect per accepted line and no stock write after rollback.
```

### INV-04 — Opening stock and stock receiving with quantity, cost, date and optional supplier/reference

**Branch:** `codex/inv-04-opening-stock-receiving`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** INV-01, INV-12, SEC-01, UX-01

```text
Implement INV-04: Opening stock and stock receiving with quantity, cost, date and optional supplier/reference.

Implement opening-stock and receiving commands/screens recording product, received/base quantity, cost, occurrence date and optional supplier/reference. Reuse immutable StockMovement and add receipt/document metadata where needed. Make any catalogue cost update explicit and owner-controlled; snapshot received cost and conversion factor on each movement.

Acceptance: Record opening stock and two 25 kg bags, verify 50.000 kg added once, preserve supplier/reference/cost after edits, and roll back the whole receipt on failure.
```

### INV-05 — Owner-authorized stock adjustment with mandatory reason

**Branch:** `codex/inv-05-owner-stock-adjustments`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** INV-01, SEC-04, SEC-06, UX-01

```text
Implement INV-05: Owner-authorized stock adjustment with mandatory reason.

Build an owner-approved positive/negative stock correction workflow with mandatory reason and preview of before/after balance. Append an adjustment movement rather than editing past movements. Validate units, nonzero amount and authorization at the application boundary, and record performer/approver with the same commit.

Acceptance: Attempt an adjustment as a cashier, cancel approval, reject a blank reason and confirm a successful correction produces one linked movement and audit entry.
```

### INV-06 — Damage, wastage and expired-stock recording

**Branch:** `codex/inv-06-damage-wastage-stock`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** INV-01, SEC-04, SEC-06, UX-01

```text
Implement INV-06: Damage, wastage and expired-stock recording.

Expose reasoned damage, wastage and expired-stock recording as explicit stock-out movements. Record quantity, category of loss, actor, owner approval where required and source/batch when tracked. Preview resulting stock and apply the negative-stock override policy instead of silently permitting excess loss.

Acceptance: Record damaged and expired fractional goods, reject invalid/zero quantities and missing reasons, and confirm stock/loss reports and audit match one committed movement each.
```

### INV-07 — Stock count with counted quantity, variance preview and confirmed adjustment

**Branch:** `codex/inv-07-stock-count-variance`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** INV-02, INV-05

```text
Implement INV-07: Stock count with counted quantity, variance preview and confirmed adjustment.

Add a stock-count workflow that captures counted quantities, shows ledger quantity and signed variance, and confirms owner-approved adjustment entries with a count reference. Detect movements between preview and confirmation and require a refreshed review instead of applying stale variance. Store the count evidence; zero variance produces no artificial stock movement.

Acceptance: Count above, below and equal to ledger stock, test fractional units and a sale between preview and confirmation, and verify cancellation/duplicate confirmation cannot post extra adjustments.
```

### INV-08 — Product reorder threshold and low-stock list

**Branch:** `codex/inv-08-reorder-low-stock`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** INV-02, CAT-01

```text
Implement INV-08: Product reorder threshold and low-stock list.

Finish Product.ReorderLevel validation, editing and persistence and add a low-stock query/list with product, signed on-hand, unit, threshold and useful filters. Define the boundary as at or below the threshold and make a zero threshold policy visible. Reuse this query in Products and reporting.

Acceptance: Check above/equal/below threshold, negative stock and fractional products, reject negative thresholds and confirm receiving refreshes the low-stock list.
```

### INV-09 — Warn about insufficient/negative stock; explicit owner override records a reason

**Branch:** `codex/inv-09-insufficient-stock-override`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** INV-02, SEC-04, SEC-06, FND-02

```text
Implement INV-09: Warn about insufficient/negative stock; explicit owner override records a reason.

Warn when the projected stock after a sale or stock-out is insufficient, accounting for all basket lines/conversions. Require a current authenticated owner approval and reason to permit negative stock, then recheck balance within the write transaction. Keep the signed shortfall visible and prevent direct command calls bypassing the check.

Acceptance: Sell exactly available stock, exceed it without/with approval, change stock before commit and test duplicate submission; only an approved committed override may leave a negative balance.
```

### INV-11 — Optional batch/expiry on stock receipts; near-expiry list with configurable days

**Branch:** `codex/inv-11-batch-expiry-tracking`  
**Priority / current catalogue status:** Should / Planned  
**Prerequisites:** INV-04, INV-03, RET-06

```text
Implement INV-11: Optional batch/expiry on stock receipts; near-expiry list with configurable days.

Add optional per-receipt batches with expiry date and remaining quantity, and a configurable near-expiry list. For tracked products, persist default issue allocations and allow correction; link returns to affected batches and disposition so balances stay explainable. Backfill existing untracked stock explicitly. Avoid full FEFO optimization or recall management.

Acceptance: Receive two expiry batches, issue from identified batches, return saleable/damaged goods, and reconcile batch totals to the ledger; test exact near-expiry cutoff and already-expired stock.
```

### INV-12 — Base stock unit and explicit pack conversion, e.g. two 25 kg bags add 50 kg

**Branch:** `codex/inv-12-base-units-pack-conversion`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** INV-01, CAT-01

```text
Implement INV-12: Base stock unit and explicit pack conversion, e.g. two 25 kg bags add 50 kg.

Model one base stock unit and explicit positive pack conversions. Support receiving packs and selling loose quantities against the same underlying balance; when sealed packs have their own barcode/price, link their consumption to the shared stock source. Snapshot the applied factor/base quantity on movements and historical lines, with an explicit precision policy and no double-counting.

Acceptance: Receive 2×25 kg = 50.000 kg, sell 1.250 kg then one sealed 25 kg pack and obtain 23.750 kg; reject zero/negative factors and confirm changing a factor does not rewrite past stock.
```

## Returns and exchanges

### RET-01 — Original bill required; find by bill number or scan printed bill barcode

**Branch:** `codex/ret-01-original-bill-lookup`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** BO-04, HW-03

```text
Implement RET-01: Original bill required; find by bill number or scan printed bill barcode.

Add original-bill lookup by exact receipt number or scanned printed bill barcode in Returns. Load complete original lines, payments and prior returns; show bill status and remaining returnable quantities and reject incomplete/voided/unknown bills clearly. Reuse the local receipt-number parser and retain manual entry.

Acceptance: Find yesterday's bill by typing and scanning, show previous partial returns and reject invalid/incomplete/voided bills without creating a refund.
```

### RET-02 — Return selected items/quantities from a completed bill

**Branch:** `codex/ret-02-selected-item-returns`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** RET-01, RET-03, RET-05, RET-06

```text
Implement RET-02: Return selected items/quantities from a completed bill.

Implement selection of original lines and return quantities with a preview of original paid-price credit. Support fractional quantities only for applicable units, prohibit empty returns, choose original-tender settlement by default and commit refund, receipt number, settlement, stock and audit atomically. Never modify the original sale.

Acceptance: Return one item and part of a weighed line, cancel with no writes, reload a completed return and confirm original bill and remaining returnable quantities are correct.
```

### RET-03 — Block quantities beyond the unreturned quantity; use original paid prices

**Branch:** `codex/ret-03-return-limits-refund-accuracy`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** FND-02, PRC-05

```text
Implement RET-03: Block quantities beyond the unreturned quantity; use original paid prices.

Harden Refund and IRefundRepository to track both completed returned quantities and actual prior refund amounts. Refund original discounted/tier-priced amounts, assigning only the actual unpaid remainder to the final return. Preserve original rounding and cap cumulative refund at originally collected money; recheck limits atomically to stop concurrent/retried over-refunds.

Acceptance: Split a Rs. 0.05 line of three units into three separate returns and refund exactly Rs. 0.05 total; cover bill discounts, weighted/tier lines, prior returns, cash rounding and competing submissions without repricing kept items.
```

### RET-04 — Owner-authorized void with reason; preserve original financial records

**Branch:** `codex/ret-04-owner-authorized-voids`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** SEC-04, SEC-06, INV-03, RET-03

```text
Implement RET-04: Owner-authorized void with reason; preserve original financial records.

Expose an owner-approved void with mandatory reason while preserving the original financial evidence and posting linked reversals. Define eligibility for unpaid drafts versus completed bills and prevent double reversal when a bill has prior returns; direct staff to the return workflow for ineligible cases. Record tender reversal evidence and stock/audit atomically.

Acceptance: Void an eligible bill once, repeat the request, attempt a void after a partial return and verify that neither cash nor stock is reversed twice and original bill details remain inspectable.
```

### RET-05 — Owner approval and reason for returns/exchanges

**Branch:** `codex/ret-05-return-exchange-approval`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** SEC-04, SEC-06

```text
Implement RET-05: Owner approval and reason for returns/exchanges.

Require authenticated owner approval and a nonblank reason for every return/exchange. Existing Refund.AuthorisedBy is only an identifier and must be backed by actual authorization. Bind approval to the exact operation and material quantities/amounts, invalidate it after changes, and ensure cancellation leaves no persisted financial or stock effects.

Acceptance: A cashier supplying an arbitrary owner ID cannot authorize a return; an approved operation records both actor and approver; editing quantities forces renewed approval and no PIN appears in audit.
```

### RET-06 — Choose saleable restock or damaged/non-saleable disposition

**Branch:** `codex/ret-06-return-stock-disposition`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** INV-01, FND-02

```text
Implement RET-06: Choose saleable restock or damaged/non-saleable disposition.

Add saleable-restock and damaged/non-saleable disposition per returned line, with persistence and event data. Replace PostStockForRefund's unconditional restocking. Saleable quantities restore sellable inventory; damaged quantities retain documented disposition without inflating sellable stock. Preserve batch linkage when batch tracking is enabled.

Acceptance: Return a mixed basket with saleable and damaged lines and verify only saleable units increase available stock; restart and confirm disposition, financial credit and source links remain intact.
```

### RET-08 — Exchange returned items for new products in one linked workflow

**Branch:** `codex/ret-08-linked-exchange-workflow`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** RET-02, RET-09, PAY-06

```text
Implement RET-08: Exchange returned items for new products in one linked workflow.

Build one Returns exchange workflow linking original bill, approved return, replacement sale and net settlement. Replacement goods use current tier pricing, return credit uses historical paid prices and is represented as internal exchange credit rather than fabricated external cash/card. Commit all records, receipt numbers, stock and audit in one transaction; print after commit.

Acceptance: Exchange returned Rs. 100 goods for Rs. 150, Rs. 80 and Rs. 100 replacements; inject failures after each stage and verify either the entire linked exchange exists once or nothing commits.
```

### RET-09 — Collect positive difference, refund negative difference, or settle zero

**Branch:** `codex/ret-09-exchange-difference-settlement`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** RET-03, RET-05, PAY-01, PAY-03

```text
Implement RET-09: Collect positive difference, refund negative difference, or settle zero.

Implement domain settlement records/calculation for replacement total minus return credit: collect positive, refund negative and settle zero. Support a pure refund, original-tender defaults and explicitly authorized externally handled card refund records. Store settlement rounding separately, count cash/card movement once and never leave outstanding customer/store credit.

Acceptance: For Rs. 100 credit, replacements of 150/80/100 collect 50/refund 20/move 0; cover original split tender, cash rounding and cumulative refund caps, and reconcile actual drawer movement separately from replacement turnover.
```

### RET-10 — Print Sinhala return/exchange receipt linked to original bill and replacement sale

**Branch:** `codex/ret-10-sinhala-return-exchange-receipts`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** RET-08, HW-01, HW-04, HW-05

```text
Implement RET-10: Print Sinhala return/exchange receipt linked to original bill and replacement sale.

Extend the fixed Sinhala renderer to print return/exchange documents with original bill, return and replacement links, returned dispositions/quantities, credit, replacement total, settlement direction/tender and rounding. Use committed historical snapshots and support preview/reprint without financial writes.

Acceptance: Preview and physically print pure return, collect/refund/equal exchange cases, including long Sinhala names, and verify disconnected-printer retry does not create additional refund/sale/stock records.
```

## Receipts and hardware

### HW-01 — Print Sinhala bill using supplied custom template and one validated Windows printer

**Branch:** `codex/hw-01-sinhala-receipt-printing`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** HW-07, SAL-10, HW-04

```text
Implement HW-01: Print Sinhala bill using supplied custom template and one validated Windows printer.

Implement receipt rendering and one validated Windows printer adapter using the owner's supplied custom template. Render Sinhala with a suitable redistributable font through Windows/graphics printing, include historical item/tier/discount/tax/settlement details and keep receipt language Sinhala regardless of UI language. Until the real template/model is supplied, complete the renderer contract and clearly mark sample output as provisional.

Acceptance: Verify shaped Sinhala, long-name wrapping, weighted quantities and totals in preview and on the actual printer; record the tested driver/model/font/template version and do not claim physical validation without it.
```

### HW-02 — Open compatible cash drawer through printer

**Branch:** `codex/hw-02-printer-cash-drawer`  
**Priority / current catalogue status:** Should / Planned  
**Prerequisites:** HW-01, PAY-01

```text
Implement HW-02: Open compatible cash drawer through printer.

Add an optional cash-drawer adapter for the supported printer/drawer, with owner-configured enablement and validated pulse parameters. Trigger according to explicit cash settlement behavior only after durable save, distinguish drawer failure from sale failure and audit manual opening where exposed. Keep unsupported hardware disabled.

Acceptance: On the real supported hardware, test cash versus card-only bills and disconnection; verify no duplicate sale, no repeated automatic drawer pulse from receipt reprint and clear failure feedback.
```

### HW-03 — USB keyboard-mode barcode scanner; manual barcode entry remains available

**Branch:** `codex/hw-03-keyboard-barcode-scanner`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** UX-01

```text
Implement HW-03: USB keyboard-mode barcode scanner; manual barcode entry remains available.

Implement a keyboard-mode scanner input adapter with Enter/terminator handling, plus manual barcode entry. Preserve leading zeroes and do not intercept ordinary text entry in dialogs or product editors. Expose distinct product-scan and bill-scan contexts for Billing and Returns, with focus restoration and testable input handling.

Acceptance: Feed complete/partial/rapid scan sequences, type a manual barcode, scan while a text dialog is open and confirm correct routing, focus and no swallowed characters or duplicate completion events.
```

### HW-04 — Save bill before printing; visible failure and retry without duplicate sale

**Branch:** `codex/hw-04-save-before-print-retry`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** PAY-06

```text
Implement HW-04: Save bill before printing; visible failure and retry without duplicate sale.

Create post-commit print orchestration with a receipt/job identifier, visible queued/succeeded/failed status and retry/reprint actions. Save the bill before any print attempt; treat ambiguous printer acknowledgement honestly since physical exactly-once printing is not guaranteed. A print retry must only load/render the existing committed bill.

Acceptance: Disconnect the printer after sale commit, restart and retry; check one sale, one payment set, one receipt number and one stock deduction regardless of print attempts, and label reprints appropriately.
```

### HW-05 — Bill preview, sales-history reprint and printed bill barcode

**Branch:** `codex/hw-05-receipt-preview-reprint-barcode`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** HW-01, BO-04

```text
Implement HW-05: Bill preview, sales-history reprint and printed bill barcode.

Add bill preview and history reprint using the same historical receipt view model and layout as initial printing. Generate a scannable internal bill barcode encoding an unambiguous local receipt identifier; route it to original-bill lookup. Reprints preserve original content and identify themselves without recalculating current prices.

Acceptance: Rename/reprice a product after sale, compare initial/preview/reprint, scan the printed bill barcode into Returns and verify no new financial or stock effects.
```

### HW-06 — Direct integration with one weighing-scale model

**Branch:** `codex/hw-06-weighing-scale-integration`  
**Priority / current catalogue status:** Later / Planned  
**Prerequisites:** SAL-05; deferred until hardware model/protocol supplied

```text
Implement HW-06: Direct integration with one weighing-scale model.

Later feature: after explicitly choosing one scale model/protocol, add a small adapter behind a weight-reading interface and connect it to the existing manual-weight dialog. Validate stable positive readings, units, precision, timeout and disconnection, and preserve manual entry as fallback. Keep device-specific code outside the domain.

Acceptance: Use recorded protocol fixtures and the actual scale to test stable/unstable readings, unit conversion, disconnect/reconnect and manual fallback; do not include this task in version 1 completion requirements.
```

### HW-07 — Match actual printer width; first layout depends on supplied template and printer

**Branch:** `codex/hw-07-printer-width-template-settings`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** CFG-01, UX-01

```text
Implement HW-07: Match actual printer width; first layout depends on supplied template and printer.

Add printer selection, paper/printable width and fixed-template version settings and a calibration preview/test print. Inspect the supplied template and actual driver margins before finalizing layout; use the chosen printer's printable area for wrapping/clipping and persist settings. Keep any sample width/template provisional until hardware inputs arrive.

Acceptance: Confirm selected printer persists, long Sinhala lines stay within actual printable width, and settings changed to an unavailable printer yield an actionable error; physical sign-off waits for real hardware/template.
```

### HW-09 — Generate internal barcode labels and shelf price tags

**Branch:** `codex/hw-09-internal-barcodes-price-labels`  
**Priority / current catalogue status:** Should / Planned  
**Prerequisites:** CAT-07, PRC-01, HW-07

```text
Implement HW-09: Generate internal barcode labels and shelf price tags.

Generate unique internal barcodes and printable product/shelf labels with Sinhala names, selling unit, normal price and useful tier information. Use an appropriate internal symbology without pretending codes are externally assigned GS1 identifiers. Add label size/printer selection, preview and collision-safe allocation independent of sale receipt numbering.

Acceptance: Generate many codes without collision, scan a printed label into checkout, preserve leading zeroes, and inspect long Sinhala names and actual dimensions on the chosen label printer/media.
```

## Reports and exports

### BO-04 — Sales history, bill lookup, item/payment detail and linked returns

**Branch:** `codex/bo-04-sales-history-bill-detail`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** FND-01, UX-01

```text
Implement BO-04: Sales history, bill lookup, item/payment detail and linked returns.

Add read-only sales history with receipt/date/status/tender filters, bounded results and complete bill details including items, historical prices, discounts, payments, rounding and linked returns/exchanges. Use shop-local date boundaries and expose actions for preview/reprint and authorized Returns without permitting mutation of completed records.

Acceptance: Find bills across a local-midnight boundary, inspect voids and partial returns, follow both directions of exchange links when available and confirm history never substitutes current catalogue values.
```

### BO-05 — Simple history of privileged changes

**Branch:** `codex/bo-05-privileged-change-history`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** SEC-06, UX-01

```text
Implement BO-05: Simple history of privileged changes.

Build a simple owner-visible audit history using AuditEntry with date/action/actor/entity filters and readable summaries of reason, approver and relevant before/after values. Link to source records and paginate; avoid exposing raw technical JSON or secrets to cashiers.

Acceptance: Perform a price override, stock adjustment, return and restore when supported; verify useful chronological records, owner-only access and no credentials/card data in the viewer.
```

### BO-06 — Daily/date-range sales: gross, discounts, returns and net sales

**Branch:** `codex/bo-06-sales-date-range-report`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** BO-04, RET-08

```text
Implement BO-06: Daily/date-range sales: gross, discounts, returns and net sales.

Add daily/date-range gross sales, manual/tier savings where defined, discounts, returns and net sales with explicit column definitions. Use shop-local boundaries and historical records, exclude voids consistently, and show exchanges once as a return plus replacement. Keep turnover separate from actual collected settlement.

Acceptance: Reconcile a fixture with normal/discounted/tier sales, partial returns, voids and all exchange directions; verify local-midnight boundaries and sums matching bill detail without double-counting exchange credit.
```

### BO-07 — Product/category sales, quantities and top sellers

**Branch:** `codex/bo-07-product-category-sales-report`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** BO-06, CAT-02

```text
Implement BO-07: Product/category sales, quantities and top sellers.

Add product/category quantities, sales, discounts, returns and top sellers using historical sale/return snapshots and units. Define category attribution for later product/category changes explicitly; do not silently rewrite history. Support dates, category/product filters and stable ranking without combining incompatible physical units into a misleading quantity total.

Acceptance: Compare report lines with underlying bills and returns, test ties/voids/fractional quantities, and verify category changes do not unexpectedly alter historical attribution.
```

### BO-08 — Estimated gross profit from historical sale cost, adjusted for returns

**Branch:** `codex/bo-08-estimated-gross-profit-report`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** CAT-03, RET-03, BO-06

```text
Implement BO-08: Estimated gross profit from historical sale cost, adjusted for returns.

Calculate estimated gross profit from historical net sales less historical sold cost, reversing the original cost for accepted returns. Define tax and discount treatment consistently, exclude voids and avoid using today's catalogue cost. Label the metric as estimated gross profit and visibly flag incomplete cost data.

Acceptance: Change current costs after sales, process partial/tier-priced returns and exchanges, then reconcile historical revenue, cost and estimated gross profit to manually calculated examples.
```

### BO-10 — Excel/CSV data export and PDF printable reports

**Branch:** `codex/bo-10-shared-report-export`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** FND-01, UX-01

```text
Implement BO-10: Excel/CSV data export and PDF printable reports.

Create shared CSV/XLSX/PDF export services behind application interfaces with chosen filters, shop-local period, headings and export timestamp. Preserve Sinhala Unicode, text barcodes and numeric/date cell types, neutralize spreadsheet formula injection in text fields and use a Windows-Excel-compatible CSV encoding. Render PDF Sinhala correctly with repeated headers and pagination; expose integration hooks for each report.

Acceptance: Open outputs in Windows Excel/PDF reader; check leading-zero barcodes, formula-like names, Sinhala shaping, typed amounts/dates, totals, selected filters, cancellation and disk/write errors.
```

### BO-12 — Stock balance/value at recorded cost, low stock and stock movement reports

**Branch:** `codex/bo-12-stock-value-movement-report`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** INV-02, INV-08, INV-04

```text
Implement BO-12: Stock balance/value at recorded cost, low stock and stock movement reports.

Add stock balance, recorded-cost valuation, low-stock and movement reports with product/category/date filters. Define and document the valuation policy from recorded receiving costs and opening stock; identify missing costs and negative balances instead of hiding them. Historical as-of balances must use movement cutoff rather than current stock.

Acceptance: Reconcile receiving, fractional sales, return dispositions, wastage and counts to ledger totals; test a price/cost change and verify as-of balances and valuation follow the documented policy.
```

### BO-13 — Cash/card totals, refunds and daily drawer reconciliation

**Branch:** `codex/bo-13-tender-refund-reconciliation`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** CSH-01, CSH-05, RET-09

```text
Implement BO-13: Cash/card totals, refunds and daily drawer reconciliation.

Build cash/card/refund/drawer reconciliation from actual external tender movements. Fix ShiftRepository.SalesSummaryAsync where it sums Payment.Amount then subtracts change again: Amount is already net cash retained. Include return/exchange net settlements and rounding once, and keep sales turnover separate from drawer movement.

Acceptance: A Rs. 150 sale tendered with Rs. 200 adds Rs. 150 to the drawer; reconcile refunds, split tender, pay-ins/outs, void reversals and 100→150/80/100 exchanges to physical cash.
```

### BO-14 — Near-expiry report for tracked deliveries

**Branch:** `codex/bo-14-near-expiry-report`  
**Priority / current catalogue status:** Should / Planned  
**Prerequisites:** INV-11, BO-10

```text
Implement BO-14: Near-expiry report for tracked deliveries.

Add a near-expiry report using remaining quantities by delivery batch and configured alert days. Distinguish already expired, due soon and zero-balance batches; show product, batch, expiry and quantity with local-date filters and exports. Reuse the same batch query as the Stock alert list.

Acceptance: Test today, exact alert-day boundary, yesterday and fully depleted batches, then compare report totals with batch allocations and exported Sinhala content.
```

## Shop and language settings

### CFG-01 — One shop profile: name/address/phone, currency, receipt details and printer

**Branch:** `codex/cfg-01-single-shop-settings`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** FND-01, UX-01, SEC-01

```text
Implement CFG-01: One shop profile: name/address/phone, currency, receipt details and printer.

Build owner-only settings for one shop name/address/phone, currency, cash increment, receipt details, Asia/Colombo timezone default and printer references. Reuse one internal shop/terminal identity and persist settings locally with validation. Prevent changing accounting currency in a way that reinterprets historical money; snapshot receipt identity details where necessary.

Acceptance: Save/restart, reject invalid currency/rounding settings, verify new bills use changed receipt details and old bills remain historically correct; expose no tenant/server onboarding.
```

### CFG-06 — Sinhala/English interface switch, remembered per user

**Branch:** `codex/cfg-06-remember-interface-language`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** UX-01, SEC-02

```text
Implement CFG-06: Sinhala/English interface switch, remembered per user.

Add Sinhala/English interface selection and persist it per user. Apply selection on login and when switching language without losing the basket; update resource bindings for labels/dialogs/formatting consistently. Keep receipts always Sinhala and persistence numeric/date formats explicit.

Acceptance: Use two users with different languages, restart/login, switch during an open basket and check no data loss, missing resource keys or unintended receipt-language changes.
```

## Windows interface

### UX-01 — Six sections: Billing, Products, Stock, Returns, Reports, Settings

**Branch:** `codex/ux-01-wpf-desktop-shell`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** FND-01

```text
Implement UX-01: Six sections: Billing, Products, Stock, Returns, Reports, Settings.

Create RetailCore.Desktop as a .NET 10 Windows WPF application using MVVM and DI, with Billing, Products, Stock, Returns, Reports and Settings navigation. Add Sinhala/English resource infrastructure and a scan-ready initial Billing view, composing application services and the local SQLite provider. Resolve inherited InvariantGlobalization=true for culture-aware desktop behavior without localizing persisted identifiers.

Acceptance: Build on Windows, launch to all six navigable sections, open/create the local database, check basic Sinhala/English text and clean startup failures; measure the under-5-second cold-start target on the intended PC.
```

### UX-02 — Clear scan focus, large total, keyboard shortcuts, readable tables and actionable errors

**Branch:** `codex/ux-02-keyboard-first-billing-ui`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** UX-01, SAL-01, SAL-02, PAY-01

```text
Implement UX-02: Clear scan focus, large total, keyboard shortcuts, readable tables and actionable errors.

Make Billing usable by keyboard with visible scan focus, readable basket columns, a large total, clear payment progress, documented shortcuts and predictable focus after dialogs. Add accessible contrast/tab order and localized actionable errors. Prevent scanner events and repeated keys from submitting payment accidentally.

Acceptance: Have a new cashier complete a mixed weighted/packaged bill without a mouse, recover from an unknown barcode and printer error, and verify shortcuts/focus under both languages and at the shop display resolution.
```

### UX-03 — Translate labels, dialogs, validation and report headings; receipts always Sinhala

**Branch:** `codex/ux-03-complete-interface-translations`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** UX-01

```text
Implement UX-03: Translate labels, dialogs, validation and report headings; receipts always Sinhala.

Establish Sinhala/English resource keys and localization adapters for labels, dialogs, validation, domain errors and report headings. Replace hardcoded user-visible messages as features land and provide a missing-key check. Render Sinhala using appropriate fonts; retain explicit storage parsing/serialization and always-Sinhala receipts.

Acceptance: Review navigation, login, product validation, payment/return errors and report headings in both languages; detect missing keys/fallbacks and verify no localized decimal/date text corrupts persisted data.
```

## Access and audit

### SEC-01 — Simple owner/cashier roles; existing manager model need not be exposed

**Branch:** `codex/sec-01-owner-cashier-access`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** FND-01, UX-01

```text
Implement SEC-01: Simple owner/cashier roles; existing manager model need not be exposed.

Expose only owner/cashier roles and owner-controlled user management in version 1. Enforce authorization in application services, keep last-active-owner protection and deactivate users without losing history. Keep the legacy Manager enum compatible for historical data but do not silently give it owner privileges or offer it in new role selection.

Acceptance: Verify cashier versus owner access through both screens and direct command calls, reject removal/deactivation of the last active owner and confirm legacy manager records are handled explicitly.
```

### SEC-02 — Local PIN login with secure hashing and lockout

**Branch:** `codex/sec-02-local-pin-login`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** SEC-01

```text
Implement SEC-02: Local PIN login with secure hashing and lockout.

Implement local PIN creation/change/verification behind a hashing interface with salted parameterized Argon2id, using the existing User hash and lockout state. Persist failed attempts and lockout before returning authentication failure, use an injectable clock, reject inactive users and create a secure first-owner setup without hardcoded default credentials.

Acceptance: Test correct/incorrect PINs, fifth-failure 15-minute lockout, restart during lockout, expiry and owner reset; verify only encoded hashes persist and no PIN appears in logs, errors or audit.
```

### SEC-04 — Lock screen and owner approval for prices, adjustments, returns and restore

**Branch:** `codex/sec-04-lock-screen-owner-approval`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** SEC-02, SEC-06

```text
Implement SEC-04: Lock screen and owner approval for prices, adjustments, returns and restore.

Add manual/inactivity lock and an owner reauthentication dialog reusable for price changes, discounts, stock corrections, returns/exchanges and restore. Bind approvals to operation details with short validity and cancellation behavior; enforce them at application boundaries rather than trusting UI state or supplied UserId. Preserve the locked basket without allowing actions until unlock.

Acceptance: Lock during Billing, unlock with correct credentials, cancel/expire approval, change approved values and attempt a direct command bypass; verify only the currently authorized operation can commit.
```

### SEC-06 — Audit sensitive actions without logging credentials or card data

**Branch:** `codex/sec-06-sensitive-action-audit`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** FND-02, SEC-01

```text
Implement SEC-06: Audit sensitive actions without logging credentials or card data.

Complete append-only audit coverage for privileged price/discount edits, stock operations, returns/voids, user changes, cash movements, backup/restore and settings. Reuse existing audit handlers but add missing events, actor/approver, reason and safe before/after summaries. Commit business changes and audit atomically; never include PINs, hashes or card data.

Acceptance: Perform representative authorized/denied actions, inject an audit-write failure, attempt audit mutation and inspect logs/support output; successful sensitive changes have useful immutable evidence and rolled-back changes do not appear as committed actions.
```

## Daily cash

### CSH-01 — Daily/shift opening float

**Branch:** `codex/csh-01-shift-opening-float`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** SEC-02, UX-01, FND-02

```text
Implement CSH-01: Daily/shift opening float.

Expose Shift.Open through a simple opening-float flow and persist one open shift per local terminal. Require a non-negative float in shop currency, record cashier/time, gate settlement on an open shift and recover the existing open shift after restart instead of creating another.

Acceptance: Open with zero/positive float, reject negative/mixed currency, submit twice, restart and verify the same shift remains open with one recorded opening balance.
```

### CSH-04 — Close day/shift with counted cash and variance

**Branch:** `codex/csh-04-shift-close-cash-variance`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** CSH-01, CSH-05, BO-13

```text
Implement CSH-04: Close day/shift with counted cash and variance.

Add counted-cash close with expected cash, signed variance and optional notes, persisting a stable closing record/report. Obtain a consistent tender/refund summary inside the close transaction; stop new transactions entering a closed shift. Preserve recorded close totals for reprint and use applied cash amounts without subtracting change twice.

Acceptance: Close a day with cash/card, refunds, all exchange directions and pay-ins/outs; reconcile expected/count/variance, reject duplicate close, test simultaneous attempted sale completion and verify a restarted close report agrees.
```

### CSH-05 — Cash pay-in/out with reason

**Branch:** `codex/csh-05-cash-pay-in-out`  
**Priority / current catalogue status:** Must / Foundation  
**Prerequisites:** CSH-01, SEC-04, SEC-06

```text
Implement CSH-05: Cash pay-in/out with reason.

Expose cash pay-in/pay-out commands using CashMovement with positive amount, direction, mandatory reason, actor and owner approval. Restrict to an open shift, append rather than edit records and show the immediate drawer effect. Keep these movements separate from sales revenue and record audit atomically.

Acceptance: Record both directions, reject zero/negative values, missing reason and closed shift, retry the same command safely and verify closing cash changes by the signed amount without changing turnover.
```

## Installation and maintenance

### OPS-01 — Windows installer, controlled upgrades and pre-upgrade backup

**Branch:** `codex/ops-01-windows-installer-upgrades`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** FND-03, OPS-07, UX-01

```text
Implement OPS-01: Windows installer, controlled upgrades and pre-upgrade backup.

Package the Windows WPF app and required runtime/fonts using a suitable installer with writable data in application-data storage, outside installation files. Add controlled forward-only migrations preceded by a verified backup, prevent unsupervised upgrades during trading and handle failed upgrades without losing the recoverable database. Preserve shop/user/printer settings across upgrades.

Acceptance: Install on a clean shop-like Windows machine, upgrade a populated prior database, simulate backup/migration failure and verify safe recovery; check uninstall/update behavior preserves business data and normal cashier operation requires no administrator rights.
```

### OPS-02 — Local error logs and a simple support export

**Branch:** `codex/ops-02-local-logs-support-export`  
**Priority / current catalogue status:** Should / Planned  
**Prerequisites:** UX-01, PAY-08

```text
Implement OPS-02: Local error logs and a simple support export.

Add rotating local error logs and an owner-triggered support export containing app/schema versions, relevant environment/printer details and redacted diagnostics. Keep default exports free of shop databases, backups, product exports, PIN/hash/card data and unnecessary personal information; expose the generated file location and useful error recovery text.

Acceptance: Trigger database/printing failures, verify log rotation and readable export contents, inspect redaction with sensitive test strings and confirm support export failure does not disrupt an active sale.
```

### OPS-07 — Automatic daily backup, manual backup and verified restore

**Branch:** `codex/ops-07-daily-backup-verified-restore`  
**Priority / current catalogue status:** Must / Planned  
**Prerequisites:** FND-01, SEC-04, SEC-06, UX-01

```text
Implement OPS-07: Automatic daily backup, manual backup and verified restore.

Implement manual and automatic daily backups through SQLite's live backup API, with retention, an external/USB second destination, visible last-success/failures and retry when a destination is absent. Restore requires owner approval, integrity/schema validation, exclusive database access and a safety backup before replacement. Block writes during restore and preserve a clear recovery route for failure; use atomic file replacement where supported.

Acceptance: Back up a WAL database while the app is running, restore to a clean installation and compare bills/stock/cash; test corrupt/incompatible files, unavailable/full destinations, interrupted restore and pending sales, and verify reported success only after validation.
```

## Release acceptance — after the Must branches merge

- Demonstrate a full mixed packaged/weighed sale with repeated scans, tier boundaries and owner discount approval, then restart and reprint without changing any financial/stock record.
- Demonstrate saleable/damaged partial returns, repeated-refund limits and all three exchange settlement directions, including rollback after interrupted writes.
- Reconcile opening float, cash/card, change, cash rounding, pay-ins/outs, refunds and exchanges against physical counted cash. Verify historical profit and stock movement totals.
- Review Sinhala/English navigation, validation and keyboard flow with a new cashier. Physically verify Sinhala receipts, the bill barcode, actual printer width and retry after disconnection.
- Open Sinhala CSV/XLSX/PDF exports, preserving leading-zero barcodes, correct totals and selected filters. Check spreadsheet formula protection.
- Restore a validated backup on a clean Windows installation and compare bills, stock and cash before pilot. Exercise daily backup failure and the second destination.
- Measure the documented scan/search/startup/save targets on the intended PC with 10,000 realistic products; document observed results rather than treating targets as guarantees.
- Complete a shop pilot with daily owner reconciliation. Should and Later features remain separately tracked; physical hardware checks cannot be replaced by mocked tests.
