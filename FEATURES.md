# RetailCore POS: Feature Catalogue

**Scope updated:** 2026-11-09. One retail shop, initially one Windows checkout computer.
**Languages:** Sinhala and English interface; Sinhala receipts using the owner's custom template.
This replaces the previous cloud, multi-tenant and synchronization feature scope.

Milestone 01 implements setup/login/settings and the desktop shell in source. Windows runtime
acceptance remains pending; see [validation notes](docs/implementation/MILESTONE_01.md).
Foundation status still does not mean production acceptance or completion of later screens.

Priority: **Must** ships in version 1; **Should** is useful but can follow initial checkout;
**Later** is outside version 1. See PLAN.md for delivery order.
Status: **Foundation** means relevant domain/persistence code exists, but the complete usable
feature is not delivered; **Planned** means implementation is still required. Historical test
counts are not a statement that the current checkout passes tests.

## 1. Checkout and payments

| ID | Feature | Priority | Status |
|---|---|---|---|
| SAL-01 | Scan barcode to add an item; repeated scans combine its quantity | Must | Planned |
| SAL-02 | Search by product name, SKU or barcode; quick category selection | Must | Planned |
| SAL-04 | Edit quantities and remove items before payment | Must | Foundation |
| SAL-05 | Sell weighed goods to three decimal places, e.g. 1.250 kg | Must | Foundation |
| SAL-07 | Owner-authorized manual discount with reason; no silent stacking with tier prices | Must | Foundation |
| SAL-10 | Calculate totals from items; preserve actual prices, costs and tax on the bill | Must | Foundation |
| SAL-11 | Hold a basket and recall it | Should | Foundation |
| SAL-12 | Price check without adding to the basket | Should | Planned |
| PAY-01 | Cash tender, amount due and change | Must | Foundation |
| PAY-02 | Cash rounding shown separately using the configured cash increment | Must | Foundation |
| PAY-03 | Record externally approved card payments and an optional reference | Must | Foundation |
| PAY-05 | Split cash/card payment | Should | Foundation |
| PAY-06 | Prevent completion with an unpaid balance; prevent duplicate completion | Must | Foundation |
| PAY-08 | Do not collect or store card numbers, security codes or PINs | Must | Foundation |

## 2. Products and quantity pricing

| ID | Feature | Priority | Status |
|---|---|---|---|
| CAT-01 | Add/edit/deactivate products: unique SKU, barcode, category, unit, cost, normal price | Must | Foundation |
| CAT-02 | Simple category management | Must | Foundation |
| CAT-03 | Preserve cost at sale time for estimated gross profit | Must | Foundation |
| CAT-04 | Reuse existing tax support where applicable; do not require tax setup for every product | Must | Foundation |
| CAT-06 | CSV/Excel product import with preview, validation and duplicate handling | Should | Planned |
| CAT-07 | Multiple barcodes for a product; preserve leading zeroes | Should | Foundation |
| CAT-10 | Searchable/sortable product table showing prices, tiers and current stock | Must | Planned |
| CAT-11 | Export all products or filtered results to Excel/CSV; printable PDF price list | Must | Planned |
| CAT-12 | Sinhala and English product names; Sinhala receipt name required for active products | Must | Planned |
| PRC-01 | Four total quantity-price bands: normal price plus up to three quantity breaks | Must | Planned |
| PRC-02 | Automatically apply the highest qualifying band to every unit of that product | Must | Planned |
| PRC-03 | Recalculate tiers when basket quantity changes; show applied unit price and savings | Must | Planned |
| PRC-04 | Validate ascending positive thresholds and positive non-increasing prices | Must | Planned |
| PRC-05 | Snapshot charged price and applied tier; later price edits never alter old bills | Must | Planned |

Default pricing example (four bands in total):

| Minimum quantity | Price per unit |
|---|---:|
| 1 | Rs. 100 |
| 2 | Rs. 90 |
| 3 | Rs. 85 |
| 4 | Rs. 80 |

Three units cost Rs. 255; five cost Rs. 400. Aggregate repeated scans before selecting a band.
Weighted-product thresholds use its selling unit, e.g. kg. Tier prices are final unit prices,
not successive percentage discounts. Manual discounts require explicit owner approval and
must show the resulting price. The initial tier example assumes normal price is the first
of four bands; additional bands are not required for version 1.

## 3. Stock and receiving

| ID | Feature | Priority | Status |
|---|---|---|---|
| INV-01 | Explain stock through an append-only movement history | Must | Foundation |
| INV-02 | Display stock balance and movement history per product | Must | Foundation |
| INV-03 | Sales deduct stock; accepted saleable returns restore stock | Must | Foundation |
| INV-04 | Opening stock and stock receiving with quantity, cost, date and optional supplier/reference | Must | Foundation |
| INV-05 | Owner-authorized stock adjustment with mandatory reason | Must | Foundation |
| INV-06 | Damage, wastage and expired-stock recording | Must | Foundation |
| INV-07 | Stock count with counted quantity, variance preview and confirmed adjustment | Must | Planned |
| INV-08 | Product reorder threshold and low-stock list | Must | Planned |
| INV-09 | Warn about insufficient/negative stock; explicit owner override records a reason | Must | Planned |
| INV-11 | Optional batch/expiry on stock receipts; near-expiry list with configurable days | Should | Planned |
| INV-12 | Base stock unit and explicit pack conversion, e.g. two 25 kg bags add 50 kg | Must | Planned |

Keep sealed packs explicit when sold under their own barcode/price. Conversion factors must
be positive and snapshotted on movements; do not double-count a bag and its contents.
Expiry belongs to a delivery batch, not a single date on the product. If expiry tracking is
enabled, stock issues/returns must identify the affected batch, allowing a default allocation
and correction. Full automatic FEFO optimization and recall management are later features.
The current refund handler restores all returned stock: disposition support is still needed.

## 4. Returns and exchanges

| ID | Feature | Priority | Status |
|---|---|---|---|
| RET-01 | Original bill required; find by bill number or scan printed bill barcode | Must | Foundation |
| RET-02 | Return selected items/quantities from a completed bill | Must | Foundation |
| RET-03 | Block quantities beyond the unreturned quantity; use original paid prices | Must | Foundation |
| RET-04 | Owner-authorized void with reason; preserve original financial records | Must | Foundation |
| RET-05 | Owner approval and reason for returns/exchanges | Must | Planned |
| RET-06 | Choose saleable restock or damaged/non-saleable disposition | Must | Planned |
| RET-08 | Exchange returned items for new products in one linked workflow | Must | Planned |
| RET-09 | Collect positive difference, refund negative difference, or settle zero | Must | Planned |
| RET-10 | Print Sinhala return/exchange receipt linked to original bill and replacement sale | Must | Planned |

Return credit is the amount actually paid for returned goods, including original discounts.
A partial return does not reprice the remaining original purchase. Replacement goods use
current pricing. Preserve original rounding adjustments and record settlement rounding
separately; cumulative refunds must not exceed the amount originally collected. Example:
Rs. 100 return against a Rs. 150 replacement collects Rs. 50; against Rs. 80 refunds Rs. 20.
For a pure return, refund the applicable amount. Do not create outstanding customer/store
credit in version 1. Refunds default to the original tender, subject to owner authorization
and externally handled card refunds. Store return, replacement, settlement and stock effects
atomically. An interrupted exchange must not leave only half the operation committed.

## 5. Receipts and hardware

| ID | Feature | Priority | Status |
|---|---|---|---|
| HW-01 | Print Sinhala bill using supplied custom template and one validated Windows printer | Must | Planned |
| HW-02 | Open compatible cash drawer through printer | Should | Planned |
| HW-03 | USB keyboard-mode barcode scanner; manual barcode entry remains available | Must | Planned |
| HW-04 | Save bill before printing; visible failure and retry without duplicate sale | Must | Planned |
| HW-05 | Bill preview, sales-history reprint and printed bill barcode | Must | Planned |
| HW-06 | Direct integration with one weighing-scale model | Later | Planned |
| HW-07 | Match actual printer width; first layout depends on supplied template and printer | Must | Planned |
| HW-09 | Generate internal barcode labels and shelf price tags | Should | Planned |

Manual weight entry is sufficient for version 1. Internal barcodes must be unique and not
pretend to be externally assigned GS1 identifiers. Use Sinhala font rendering through the
Windows print path or validated graphics printing; do not assume printer code-page support.

## 6. Reports and exports

| ID | Feature | Priority | Status |
|---|---|---|---|
| BO-04 | Sales history, bill lookup, item/payment detail and linked returns | Must | Planned |
| BO-05 | Simple history of privileged changes | Must | Foundation |
| BO-06 | Daily/date-range sales: gross, discounts, returns and net sales | Must | Planned |
| BO-07 | Product/category sales, quantities and top sellers | Must | Planned |
| BO-08 | Estimated gross profit from historical sale cost, adjusted for returns | Must | Planned |
| BO-10 | Excel/CSV data export and PDF printable reports | Must | Planned |
| BO-12 | Stock balance/value at recorded cost, low stock and stock movement reports | Must | Planned |
| BO-13 | Cash/card totals, refunds and daily drawer reconciliation | Must | Planned |
| BO-14 | Near-expiry report for tracked deliveries | Should | Planned |

Reports use selected dates and the shop's local time, clearly distinguish sales from cash
collected, and count exchanges once as a return plus replacement sale. Gross profit is not
net business profit. Exports preserve Sinhala Unicode, numeric/date types and barcode text;
neutralize spreadsheet formula injection from text values. Export the chosen filters and
label the reporting period. CSV should open correctly in Windows Excel.

## 7. Interface, access and maintenance

| ID | Feature | Priority | Status |
|---|---|---|---|
| CFG-01 | One shop profile: name/address/phone, currency, receipt details and printer | Must | Foundation |
| CFG-06 | Sinhala/English interface switch, remembered per user | Must | Planned |
| UX-01 | Six sections: Billing, Products, Stock, Returns, Reports, Settings | Must | Planned |
| UX-02 | Clear scan focus, large total, keyboard shortcuts, readable tables and actionable errors | Must | Planned |
| UX-03 | Translate labels, dialogs, validation and report headings; receipts always Sinhala | Must | Planned |
| SEC-01 | Simple owner/cashier roles; existing manager model need not be exposed | Must | Foundation |
| SEC-02 | Local PIN login with secure hashing and lockout | Must | Foundation |
| SEC-04 | Lock screen and owner approval for prices, adjustments, returns and restore | Must | Planned |
| SEC-06 | Audit sensitive actions without logging credentials or card data | Must | Foundation |
| CSH-01 | Daily/shift opening float | Must | Foundation |
| CSH-04 | Close day/shift with counted cash and variance | Must | Foundation |
| CSH-05 | Cash pay-in/out with reason | Must | Foundation |
| OPS-01 | Windows installer, controlled upgrades and pre-upgrade backup | Must | Planned |
| OPS-02 | Local error logs and a simple support export | Should | Planned |
| OPS-07 | Automatic daily backup, manual backup and verified restore | Must | Planned |

Back up outside the application database folder; offer a second destination such as USB.
Display last successful backup and failures. A second copy on the same disk is not sufficient
protection against disk loss. Restore requires owner approval and a safety backup.

## 8. Outside version 1

No cloud hosting, web back office, multi-tenancy, multi-branch views, device enrollment,
outbox/sync pipeline, customer credit, loyalty, ecommerce, accounting ledger, payment-gateway
integration, receipt-template editor, custom scripting, or multi-computer shared database.
Tamil is not an interface requirement; the requested interface languages are Sinhala/English.

## Definition of done

- Reachable through the Windows application, with useful Sinhala and English messages.
- Financial/stock rules covered by meaningful tests; completed records remain historical.
- Verified with realistic products, repeated scans, fractional weights and tier boundaries.
- Receipt preview and physical Sinhala output checked on the shop's actual printer.
- Exported Sinhala text, barcode leading zeroes and report totals verified.
- Interrupted writes do not leave partial sales/exchanges; printing can be retried safely.
- Backup restored on a clean installation and totals checked before pilot.
- Core checkout is keyboard-accessible and understandable to an untrained cashier.

## Performance targets

On the intended shop PC with 10,000 products: scan-to-line under 100 ms, product search under
150 ms and cold start under 5 seconds. Saving a typical sale should finish within 500 ms;
physical printing is measured separately against the chosen printer. These are targets to
validate, not current guarantees. Billing has no internet dependency.
