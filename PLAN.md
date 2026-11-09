# RetailCore POS: Delivery Plan

**Revised:** 2026-11-09.
**Product:** A simple Windows POS for one retail shop selling packaged and bulk goods.
**Languages:** Sinhala and English interface. Bills and return/exchange receipts in Sinhala,
using the owner's custom template.
**Deployment assumption:** One checkout computer, one local SQLite database. No cloud hosting,
multi-tenancy, offline sync or multi-computer database sharing.

FEATURES.md defines the scope; ARCHITECTURE.md defines the intended implementation. These
are a revised plan, not a claim that the planned application is implemented.

**Execution guide:** use [the 8-milestone delivery plan](docs/implementation/DELIVERY_PLAN.md)
for branch names, grouped implementation prompts and demonstrations. The phases below describe
the overall scope; individual feature IDs are acceptance checks, not separate required branches.

## 1. Success criterion

A shop can add/import products, receive stock, scan and bill packaged/weighed goods, apply
quantity tiers, print Sinhala bills, handle next-day returns/exchanges, reconcile cash and
export useful reports. A cashier should complete these tasks with minimal training. The
owner must be able to restore a backup on a clean PC before real trading begins.

## 2. Existing foundation

The repository contains domain rules, application interfaces/event handlers, EF mappings,
SQLite/PostgreSQL migrations and domain/architecture/integration test projects. Money,
quantities, sales, refunds, shifts, stock movements and audit entries can be reused.

Milestone 01 now adds desktop setup/login/settings, local authentication, language preferences
and transaction/audit primitives; Windows acceptance is still pending. See
[implementation and validation notes](docs/implementation/MILESTONE_01.md). Printing, tier pricing,
exchange workflows and backup/export remain unimplemented. Tenant/outbox infrastructure is legacy
scope. Earlier documents reported passing tests; current build/test status must be verified
in the implementation environment rather than carried forward as a guarantee.

## 3. Delivery phases

### Phase 1: Simplify the foundation and define the screen flow

- Add the Windows WPF desktop project and a six-section navigation shell.
- Establish one configured shop/terminal internally, with simple shop settings.
- Make SQLite the only runtime provider; no server or tenant onboarding.
- Retire PostgreSQL/tenant-specific required gates when the code transition is complete;
  preserve historical migrations and useful tests until deliberately replaced.
- Add Sinhala/English resources and language selection from the start. Review the current
  invariant-globalization setting before shipping culture-aware UI.
- Implement owner/cashier login, secure PIN hashing and lockout.
- Confirm the actual shop PC, printer, paper width, scanner and supplied receipt template.

**Exit:** Installed application opens in either language and can create/open its local database.
No untranslated system messages in the basic navigation/login path.

### Phase 2: Products, stock and tier pricing

- Product/category add, edit and deactivate; Sinhala/English names and barcode validation.
- Searchable product list with visible stock, cost, normal price and quantity bands.
- Four total price bands: normal plus up to three breaks; repeated scans aggregate quantity.
- Base units, fractional weights and explicit bag/pack conversions.
- Opening stock, stock receiving, reasoned adjustments, damage/wastage and stock count.
- Low-stock list and owner override for insufficient stock.
- Product Excel/CSV export and printable PDF list; validated CSV/Excel import if feasible.
- Optional delivery batch/expiry and near-expiry list after the core receiving flow works.

**Exit:** Receive two 25 kg bags, sell loose quantities against the same balance, and verify
stock. Demonstrate 1/2/3/4/5-unit tier boundaries and tier recalculation when quantity drops.
Exports preserve Sinhala text and leading-zero barcodes.

### Phase 3: Checkout and Sinhala printing

- Scan/search/category selection, quantity edit, line removal and owner discount approval.
- Cash/change/rounding and external card recording; split tender and held bills if feasible.
- Atomic sale, payment, receipt number, stock and audit commit.
- Apply the supplied Sinhala receipt layout; preview, print, receipt barcode and reprint.
- Save bill before print; support printer disconnection and retry without duplicate sales.
- Keyboard-focused checkout, clear totals and useful Sinhala/English errors.

**Exit:** An untrained cashier completes a mixed packaged/weighed basket and prints a readable
Sinhala bill. Restarting after commit preserves the bill. Printer failure never duplicates it.

### Phase 4: Returns, exchanges and daily cash

- Original-bill lookup/scan; selected returned quantities, reason and owner approval.
- Enforce remaining returnable quantity and use original discounted prices.
- Saleable vs damaged disposition; record stock effects correctly.
- Link a return to a replacement sale and settle the difference atomically.
- Print linked Sinhala return/exchange receipts and preserve original bills.
- Opening float, cash in/out, counted closing cash and variance.
- Correct any existing summary/rounding behavior revealed by real settlement tests.

**Exit:** Return Rs. 100 and buy Rs. 150 (collect 50), buy Rs. 80 (refund 20), exchange for equal
value, or take a pure refund. Test prior partial returns, damaged goods, tier-priced returns,
split payments and rollback on failure. Drawer totals must match physical cash.

### Phase 5: Reports, backups and shop pilot

- Sales history; date-range daily/product/category sales; discounts/returns/net sales.
- Payment/refund reconciliation, stock/value/movement/low-stock reports and estimated profit.
- Excel/CSV data exports and PDF reports with filters and correct Sinhala rendering.
- Automatic daily backup, manual backup, second destination, visible failures and safe restore.
- Windows installer, pre-upgrade backup, migration and local support logs.
- Physical hardware tests, keyboard usability and Sinhala/English translation review.
- Pilot with real shop products and opening stock; reconcile every pilot day with the owner.

**Exit:** A full trading week with bills, stock and cash reconciled; owner can export products
and reports unaided and restore a verified backup onto a clean installation.

## 4. Version 1 priorities

Complete the Must features in FEATURES.md. Should features can follow the first usable
checkout: import, held bills, split tender, optional expiry, labels and cash drawer support.
Do not add cloud services, sync, multi-tenancy, a web dashboard, loyalty, customer credit,
payment APIs, a template editor or additional hardware protocols to version 1.

No calendar estimate is committed until the printer/template is inspected and the first
checkout-and-print vertical slice works. Deliver and verify one phase before broadening it.

## 5. Working agreements

- Keep financial calculations in the domain and test changed pricing/rounding/return rules.
- Keep source and exports UTF-8/Unicode-safe; separate interface resources from data.
- Use short-lived changes, conventional commits and warnings-as-errors builds.
- Make migrations forward-only; never edit an applied migration to simplify the old design.
- During transition, keep existing CI intact until replacement single-shop checks pass.
- Version 1 required gates: build, domain/architecture tests relevant to the local design,
  SQLite integration/migration checks and Windows desktop validation.
- Test exchange atomicity, repeat return limits, tier boundaries, stock conversions and cash
  reconciliation; verify physical Sinhala printing and exported files manually.
- Keep secrets, local shop databases, backups and customer exports out of source control.
- Back up before upgrades; validate restore before pilot. No unsupervised upgrade during sales.

## 6. Inputs still needed before hardware implementation

The owner will supply the custom Sinhala receipt template, printer model/driver and paper
width, plus confirm the initial checkout computer count. This plan assumes one computer.
These inputs do not block product, pricing, translation and inventory design.
