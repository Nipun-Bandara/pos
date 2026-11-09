# RetailCore feature branch index

> **Execution plan updated:** use [the 8-milestone delivery plan](DELIVERY_PLAN.md). The per-feature branches below are reference suggestions, not the recommended work queue.

Full prompts and prerequisites: [FEATURE_PROMPTS.md](FEATURE_PROMPTS.md). Branches below are proposed, not created.

| ID | Feature | Priority | Suggested branch |
|---|---|---|---|
| FND-01 | Single-shop SQLite runtime | Prerequisite | `codex/single-shop-sqlite-runtime` |
| FND-02 | Transactional commands and retry safety | Prerequisite | `codex/transactional-command-foundation` |
| FND-03 | Single-shop CI and Windows validation | Prerequisite | `codex/sqlite-windows-validation` |
| SAL-01 | Scan barcode to add an item; repeated scans combine its quantity | Must | `codex/sal-01-barcode-checkout` |
| SAL-02 | Search by product name, SKU or barcode; quick category selection | Must | `codex/sal-02-product-search-checkout` |
| SAL-04 | Edit quantities and remove items before payment | Must | `codex/sal-04-basket-quantity-edit` |
| SAL-05 | Sell weighed goods to three decimal places, e.g. 1.250 kg | Must | `codex/sal-05-weighed-goods-checkout` |
| SAL-07 | Owner-authorized manual discount with reason; no silent stacking with tier prices | Must | `codex/sal-07-owner-approved-discounts` |
| SAL-10 | Calculate totals from items; preserve actual prices, costs and tax on the bill | Must | `codex/sal-10-sale-totals-snapshots` |
| SAL-11 | Hold a basket and recall it | Should | `codex/sal-11-hold-recall-basket` |
| SAL-12 | Price check without adding to the basket | Should | `codex/sal-12-price-check` |
| PAY-01 | Cash tender, amount due and change | Must | `codex/pay-01-cash-tender-change` |
| PAY-02 | Cash rounding shown separately using the configured cash increment | Must | `codex/pay-02-cash-rounding` |
| PAY-03 | Record externally approved card payments and an optional reference | Must | `codex/pay-03-external-card-payments` |
| PAY-05 | Split cash/card payment | Should | `codex/pay-05-split-cash-card` |
| PAY-06 | Prevent completion with an unpaid balance; prevent duplicate completion | Must | `codex/pay-06-atomic-sale-completion` |
| PAY-08 | Do not collect or store card numbers, security codes or PINs | Must | `codex/pay-08-payment-data-minimization` |
| CAT-01 | Add/edit/deactivate products: unique SKU, barcode, category, unit, cost, normal price | Must | `codex/cat-01-product-management` |
| CAT-02 | Simple category management | Must | `codex/cat-02-category-management` |
| CAT-03 | Preserve cost at sale time for estimated gross profit | Must | `codex/cat-03-historical-cost-snapshots` |
| CAT-04 | Reuse existing tax support where applicable; do not require tax setup for every product | Must | `codex/cat-04-simple-tax-defaults` |
| CAT-06 | CSV/Excel product import with preview, validation and duplicate handling | Should | `codex/cat-06-product-import-preview` |
| CAT-07 | Multiple barcodes for a product; preserve leading zeroes | Should | `codex/cat-07-multiple-product-barcodes` |
| CAT-10 | Searchable/sortable product table showing prices, tiers and current stock | Must | `codex/cat-10-product-stock-table` |
| CAT-11 | Export all products or filtered results to Excel/CSV; printable PDF price list | Must | `codex/cat-11-product-exports-price-list` |
| CAT-12 | Sinhala and English product names; Sinhala receipt name required for active products | Must | `codex/cat-12-bilingual-product-names` |
| PRC-01 | Four total quantity-price bands: normal price plus up to three quantity breaks | Must | `codex/prc-01-quantity-price-bands` |
| PRC-02 | Automatically apply the highest qualifying band to every unit of that product | Must | `codex/prc-02-quantity-tier-selection` |
| PRC-03 | Recalculate tiers when basket quantity changes; show applied unit price and savings | Must | `codex/prc-03-live-tier-recalculation` |
| PRC-04 | Validate ascending positive thresholds and positive non-increasing prices | Must | `codex/prc-04-price-band-validation` |
| PRC-05 | Snapshot charged price and applied tier; later price edits never alter old bills | Must | `codex/prc-05-sale-price-tier-snapshots` |
| INV-01 | Explain stock through an append-only movement history | Must | `codex/inv-01-append-only-stock-ledger` |
| INV-02 | Display stock balance and movement history per product | Must | `codex/inv-02-stock-balance-history` |
| INV-03 | Sales deduct stock; accepted saleable returns restore stock | Must | `codex/inv-03-atomic-sale-return-stock` |
| INV-04 | Opening stock and stock receiving with quantity, cost, date and optional supplier/reference | Must | `codex/inv-04-opening-stock-receiving` |
| INV-05 | Owner-authorized stock adjustment with mandatory reason | Must | `codex/inv-05-owner-stock-adjustments` |
| INV-06 | Damage, wastage and expired-stock recording | Must | `codex/inv-06-damage-wastage-stock` |
| INV-07 | Stock count with counted quantity, variance preview and confirmed adjustment | Must | `codex/inv-07-stock-count-variance` |
| INV-08 | Product reorder threshold and low-stock list | Must | `codex/inv-08-reorder-low-stock` |
| INV-09 | Warn about insufficient/negative stock; explicit owner override records a reason | Must | `codex/inv-09-insufficient-stock-override` |
| INV-11 | Optional batch/expiry on stock receipts; near-expiry list with configurable days | Should | `codex/inv-11-batch-expiry-tracking` |
| INV-12 | Base stock unit and explicit pack conversion, e.g. two 25 kg bags add 50 kg | Must | `codex/inv-12-base-units-pack-conversion` |
| RET-01 | Original bill required; find by bill number or scan printed bill barcode | Must | `codex/ret-01-original-bill-lookup` |
| RET-02 | Return selected items/quantities from a completed bill | Must | `codex/ret-02-selected-item-returns` |
| RET-03 | Block quantities beyond the unreturned quantity; use original paid prices | Must | `codex/ret-03-return-limits-refund-accuracy` |
| RET-04 | Owner-authorized void with reason; preserve original financial records | Must | `codex/ret-04-owner-authorized-voids` |
| RET-05 | Owner approval and reason for returns/exchanges | Must | `codex/ret-05-return-exchange-approval` |
| RET-06 | Choose saleable restock or damaged/non-saleable disposition | Must | `codex/ret-06-return-stock-disposition` |
| RET-08 | Exchange returned items for new products in one linked workflow | Must | `codex/ret-08-linked-exchange-workflow` |
| RET-09 | Collect positive difference, refund negative difference, or settle zero | Must | `codex/ret-09-exchange-difference-settlement` |
| RET-10 | Print Sinhala return/exchange receipt linked to original bill and replacement sale | Must | `codex/ret-10-sinhala-return-exchange-receipts` |
| HW-01 | Print Sinhala bill using supplied custom template and one validated Windows printer | Must | `codex/hw-01-sinhala-receipt-printing` |
| HW-02 | Open compatible cash drawer through printer | Should | `codex/hw-02-printer-cash-drawer` |
| HW-03 | USB keyboard-mode barcode scanner; manual barcode entry remains available | Must | `codex/hw-03-keyboard-barcode-scanner` |
| HW-04 | Save bill before printing; visible failure and retry without duplicate sale | Must | `codex/hw-04-save-before-print-retry` |
| HW-05 | Bill preview, sales-history reprint and printed bill barcode | Must | `codex/hw-05-receipt-preview-reprint-barcode` |
| HW-06 | Direct integration with one weighing-scale model | Later | `codex/hw-06-weighing-scale-integration` |
| HW-07 | Match actual printer width; first layout depends on supplied template and printer | Must | `codex/hw-07-printer-width-template-settings` |
| HW-09 | Generate internal barcode labels and shelf price tags | Should | `codex/hw-09-internal-barcodes-price-labels` |
| BO-04 | Sales history, bill lookup, item/payment detail and linked returns | Must | `codex/bo-04-sales-history-bill-detail` |
| BO-05 | Simple history of privileged changes | Must | `codex/bo-05-privileged-change-history` |
| BO-06 | Daily/date-range sales: gross, discounts, returns and net sales | Must | `codex/bo-06-sales-date-range-report` |
| BO-07 | Product/category sales, quantities and top sellers | Must | `codex/bo-07-product-category-sales-report` |
| BO-08 | Estimated gross profit from historical sale cost, adjusted for returns | Must | `codex/bo-08-estimated-gross-profit-report` |
| BO-10 | Excel/CSV data export and PDF printable reports | Must | `codex/bo-10-shared-report-export` |
| BO-12 | Stock balance/value at recorded cost, low stock and stock movement reports | Must | `codex/bo-12-stock-value-movement-report` |
| BO-13 | Cash/card totals, refunds and daily drawer reconciliation | Must | `codex/bo-13-tender-refund-reconciliation` |
| BO-14 | Near-expiry report for tracked deliveries | Should | `codex/bo-14-near-expiry-report` |
| CFG-01 | One shop profile: name/address/phone, currency, receipt details and printer | Must | `codex/cfg-01-single-shop-settings` |
| CFG-06 | Sinhala/English interface switch, remembered per user | Must | `codex/cfg-06-remember-interface-language` |
| UX-01 | Six sections: Billing, Products, Stock, Returns, Reports, Settings | Must | `codex/ux-01-wpf-desktop-shell` |
| UX-02 | Clear scan focus, large total, keyboard shortcuts, readable tables and actionable errors | Must | `codex/ux-02-keyboard-first-billing-ui` |
| UX-03 | Translate labels, dialogs, validation and report headings; receipts always Sinhala | Must | `codex/ux-03-complete-interface-translations` |
| SEC-01 | Simple owner/cashier roles; existing manager model need not be exposed | Must | `codex/sec-01-owner-cashier-access` |
| SEC-02 | Local PIN login with secure hashing and lockout | Must | `codex/sec-02-local-pin-login` |
| SEC-04 | Lock screen and owner approval for prices, adjustments, returns and restore | Must | `codex/sec-04-lock-screen-owner-approval` |
| SEC-06 | Audit sensitive actions without logging credentials or card data | Must | `codex/sec-06-sensitive-action-audit` |
| CSH-01 | Daily/shift opening float | Must | `codex/csh-01-shift-opening-float` |
| CSH-04 | Close day/shift with counted cash and variance | Must | `codex/csh-04-shift-close-cash-variance` |
| CSH-05 | Cash pay-in/out with reason | Must | `codex/csh-05-cash-pay-in-out` |
| OPS-01 | Windows installer, controlled upgrades and pre-upgrade backup | Must | `codex/ops-01-windows-installer-upgrades` |
| OPS-02 | Local error logs and a simple support export | Should | `codex/ops-02-local-logs-support-export` |
| OPS-07 | Automatic daily backup, manual backup and verified restore | Must | `codex/ops-07-daily-backup-verified-restore` |
