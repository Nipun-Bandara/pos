# RetailCore POS: Architecture

**Current product decision: 2026-11-09.** A single-shop Windows desktop POS, initially installed
on one checkout computer. Sinhala/English interface and Sinhala receipts. No cloud service,
multi-tenancy or synchronization requirement. Feature scope is in FEATURES.md; sequencing is
in PLAN.md. ADR 0005 supersedes the earlier deployment assumptions.

## 1. Deployment

```text
Windows PC
  RetailCore.Desktop (WPF application)
    Application services and domain rules
      EF Core / SQLite local database
  USB barcode scanner + Windows receipt printer
  Backup destination (prefer an additional removable/external copy)
```

Use the existing .NET 10/C# code and EF Core SQLite mapping. WPF is the selected Windows UI
for the revised plan; milestone 01 implements setup/login/settings and a navigation shell.
See [milestone 01 validation notes](docs/implementation/MILESTONE_01.md) for the current
Windows validation boundary. No server process, remote API,
PostgreSQL service, containers or internet connection are required for the shop installation.
A second computer sharing live data is a separate future design decision. Never share the
SQLite file over a network drive as a shortcut to multiple tills.

## 2. Code boundaries and transition

Retain the useful separation:

- `RetailCore.Domain`: money, quantity, products, sales, refunds, stock and shifts.
- `RetailCore.Application`: use-case orchestration, authorization and persistence interfaces.
- `RetailCore.Infrastructure`: shared EF mappings, repositories and event dispatch.
- `RetailCore.Infrastructure.Sqlite`: local provider, migrations and connection setup.
- `RetailCore.Desktop`: WPF views/view models, translations and composition root.

The repository still contains PostgreSQL, tenant filters, shop IDs, terminal IDs and an outbox
schema from the former plan. They are legacy implementation, not requirements for version 1.
Do not delete applied migrations or silently remove columns. Milestone 01 adds a single-shop
runtime and required SQLite/Windows CI checks; PostgreSQL
compatibility checks are retained as optional explicit runs. Existing tenant fields/filters
and historical assertions remain useful compatibility checks, without tenant onboarding in
the UI. Use one configured shop/terminal internally where existing APIs require
IDs; do not expose tenant provisioning to the cashier.

Retain historical ADRs to explain the code. ADRs 0001 (integer money) and 0002 (stock ledger)
still supply useful domain rules. ADR 0003's tenancy decision is superseded for this product;
ADR 0004's receipt sequencing is retained locally without a sync requirement.

## 3. Money, quantities and pricing

Keep integer minor-unit Money and integer thousandths Quantity. Calculate and round line
amounts before summing, retain currency validation, and record cash rounding separately.
Reuse tax support where the shop needs it; setup should remain simple.

Product pricing needs a new model: normal price plus up to three quantity breaks, four bands
in total. Each break contains a minimum quantity and unit price in the selling unit. Select
the greatest qualifying threshold after aggregating the product's basket quantity; apply its
price to all units. Example: 1 at 100, 2 at 90, 3 at 85, 4+ at 80. Three cost 255.
Validate ascending thresholds and non-increasing positive prices. Snapshot the charged price,
applied tier and product names on the sale. Changes to tiers must not rewrite old receipts.
Manual price/discount overrides require owner approval; no invisible discount stacking.

Define one base stock unit per product. Pack conversion records the factor used, e.g. two
25 kg bags become 50.000 kg. Sealed pack sales and loose sales must consume the same underlying
stock correctly without double-counting. Retain cost and conversion snapshots for reporting.

## 4. Atomic transactions and stock

A completed sale, receipt counter increment, payments, stock movements and audit entries must
commit together through the application unit of work. No outbox is required. Inventory remains
an append-only movement ledger for explainability, even with only one computer.

Receiving, opening balances, wastage and counted-stock corrections need application screens
and authorization. Show insufficient-stock warnings and require an explicit owner override to
allow a negative balance. Optional expiry tracking is per received batch; tracked issues and
returns need a batch allocation so expiry balances stay meaningful.

Do not treat existing comments as proof of complete protections. Verify async save paths,
rollback/retry behavior, SQLite immutability triggers and database constraints during delivery.
The current handlers and repository summaries must be checked against the revised workflows.

## 5. Returns and exchanges

Require an original bill. Load previously returned quantities and prevent excess returns.
Use original charged prices, including discounts; do not reprice the items the customer kept.
Track actual prior refund amounts so repeated partial refunds cannot accumulate rounding
errors or exceed what was collected. Record saleable vs damaged disposition; milestone 03 posting now respects that disposition.
The actual return/exchange application workflow remains milestone 06.

An exchange links the original bill, a return, a replacement sale and the net settlement:
replacement total minus return credit. Positive means collect, negative means refund, zero
means no money changes hands. Replacement items use current prices. Cash rounding is a
separate settlement adjustment and must be accounted for once. Card refunds are performed
externally and recorded; version 1 has no acquirer integration or stored customer credit.

Add an exchange application service and settlement/link records rather than disguising return
credit as an external card/cash payment. Commit the return, new sale, stock movements, receipt
numbers, audit and settlement in one transaction. A failure rolls back the whole exchange.
Print only after commit and allow reprinting without reposting stock or payments.

## 6. Languages and UI

All interface text, validation, dialogs and report headings use Sinhala/English resources.
Remember language per user. Receipt language is always Sinhala regardless of interface choice.
Store Unicode product names in Sinhala and English; require a Sinhala receipt name before a
product is activated. Snapshot printed names for consistent historical reprints.

Milestone 01 disables invariant globalization to support Sinhala/English formatting. Validate
that formatting on the installed Windows build. Keep persistence serialization and numeric parsing explicit; do not localize stored
identifiers or turn barcodes into numbers.

Provide six sections: Billing, Products, Stock, Returns, Reports, Settings. Default to a clear
scan-ready billing screen, visible basket, large total and keyboard-accessible settlement.
Use searchable tables and filters rather than deep menus. Product export operates on all
products or the current filtered selection.

## 7. Printing and external devices

Use the owner's supplied receipt template as a fixed, versioned layout; no editor is needed.
Template file, actual printer model/driver and paper width are implementation inputs still to
be supplied. Render Sinhala using a suitable redistributable font and Windows printing or a
validated graphics adapter; do not depend on built-in printer character tables. Test long
names, wrapping, quantities, tier prices, totals, bill barcode and reprint on physical paper.

Wrap printing and barcode input behind small interfaces. Begin with one supported printer
and keyboard-mode scanner. Manual weight entry ships first; direct scale support can follow.
Printer failure leaves the committed bill accessible for retry; never repeat the sale commit
to retry printing. Cash drawer support is optional and hardware-dependent.

## 8. Reports, exports and access

Calculate sales, discounts, returns and net sales separately. Exchanges contribute their
return and replacement once each; settlement totals describe actual cash/card movement.
Validate expected drawer cash against applied cash payments; do not subtract change twice.
Estimated gross profit uses historical costs and return reversals, not today's catalogue cost.

Excel/CSV exports retain Unicode, barcode text and numeric/date values. Neutralize formula
injection in text cells. PDF reports are for printing/sharing and include selected dates and
filters. Report dates use the configured shop timezone (initially Asia/Colombo).

Expose owner/cashier access. Owner approval protects refunds, price changes, stock corrections
and restore. Milestone 01 supplies secure local hashing/login, persisted lockout and owner-checked settings
commands on top of the existing domain. Log privileged changes without secrets.

## 9. Durability, backup and installation

Configure SQLite WAL, synchronous FULL and foreign keys consistently on runtime connections.
Keep the writable database in an appropriate Windows application-data folder, outside the
installation directory. Save transactions before success is shown; handle full-disk and
write failures with clear messages. No local database can protect against physical disk loss
without a separate backup.

Use SQLite's supported live backup API, not an uncoordinated copy of a WAL database file.
Provide automatic daily and manual backups, a visible last-success status, a second backup
destination and retention. Restore requires owner approval, validation, exclusive database
access and a safety backup. Test restoring onto another clean installation.

Ship a Windows installer and controlled upgrades with a pre-upgrade backup and forward-only
migrations. Local logs and a small support export are enough initially; distributed tracing,
cloud health endpoints and remote device management are outside scope.

## Milestone 04 checkout boundary

`BillingService` uses the existing local transaction and owner-approval infrastructure.
Per-user SQLite baskets retain quantities and proposed final prices; every settlement
rebuilds the quote from current catalogue, tax, shift and stock revisions. The quote hash
and basket version protect approval against changed inputs. Manual discounts replace the
qualified tier price, require an exact owner grant at completion, and snapshot both prices.

The basket identity is the sale/command identity. One SQLite transaction saves its sale,
payment, receipt counter, converted stock posting, audit, immutable result and next basket.
An authenticated exact retry returns that saved result even after restart. Conflicting
retries fail. Completed lines preserve Sinhala receipt/name, normal/qualified/charged
prices, tax, cost and base-stock conversion snapshots. Forward migrations leave old bills
unchanged. Cash uses the configured increment and stores rounding separately; drawer cash
uses the applied cash amount without subtracting change again. Cards are external approvals
with restricted optional reference text, never payment credentials or gateway calls.

The WPF Billing screen and Windows dispatcher acceptance test are implemented; execution on
Windows and target-device acceptance remain required. See
[the milestone 04 validation record](docs/implementation/MILESTONE_04.md).
