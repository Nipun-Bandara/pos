# RetailCore POS: Feature Catalogue

Every feature carries an ID, a priority, the phase that delivers it, and its current state.

**Priority uses MoSCoW.** *Must* means the product is not shippable without it. *Should* means a pilot shop will notice it missing. *Could* means it is desirable and safe to drop. *Won't* means it is out of scope for version 1 and is recorded so the decision is not relitigated every month.

**Status:** `Done`, `Done (domain)`, `Done (schema)`, `Building`, `Planned`.

`Done (domain)` means the rule is implemented, tested and persisted, but the cashier cannot
yet reach it because there is no till UI. Those become `Done` in Phase 2. `Done (schema)`
means the tables exist and are exercised by tests, but the behaviour that uses them is a
later phase.

*Last reconciled against the code on 2026-08-25, after Phases 0 and 1.*

---

## 1. Sales and checkout

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| SAL-01 | Add item by barcode scan | Must | 2 | Planned |
| SAL-02 | Add item by SKU or name search | Must | 2 | Planned |
| SAL-03 | Category grid for items without barcodes | Must | 2 | Planned |
| SAL-04 | Edit line quantity | Must | 2 | Planned |
| SAL-05 | Weighed items to three decimal places | Must | 1 | Done |
| SAL-06 | Void a line before completion | Must | 1 | Done (domain) |
| SAL-07 | Line discount, amount or percent | Must | 1 | Done |
| SAL-08 | Whole-sale discount | Should | 1 | Done (domain) |
| SAL-09 | Manager override required above a discount threshold | Must | 1 | Done (domain) |
| SAL-10 | Totals computed from lines, never assignable | Must | 1 | Done |
| SAL-11 | Hold a sale and recall it later | Should | 1 | Done (domain) |
| SAL-12 | Price check without adding to cart | Should | 2 | Planned |
| SAL-13 | Open-price item for goods without a fixed price | Could | 1 | Done (domain) |
| SAL-14 | Customer name and phone on the sale | Could | 1 | Done (domain) |

## 2. Payments

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| PAY-01 | Cash tender with change calculation | Must | 1 | Done |
| PAY-02 | Cash rounding to the smallest circulating coin | Must | 1 | Done |
| PAY-03 | Card payment, approval reference only | Must | 1 | Done (domain) |
| PAY-04 | QR payment, approval reference only | Should | 1 | Done (domain) |
| PAY-05 | Split tender across multiple methods | Should | 1 | Done |
| PAY-06 | Short payment blocks completion | Must | 1 | Done |
| PAY-07 | Shop credit for known customers | Could | 6 | Planned |
| PAY-08 | Card data never stored anywhere | Must | All | Done (by design) |

## 3. Returns, voids and corrections

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| RET-01 | Refund against an original receipt | Must | 1 | Done |
| RET-02 | Partial refund of selected lines | Must | 1 | Done (domain) |
| RET-03 | Refund cannot exceed the original sale | Must | 1 | Done |
| RET-04 | Void a completed sale with a mandatory reason | Must | 1 | Done |
| RET-05 | Void and refund require manager authorisation | Must | 2 | Planned |
| RET-06 | Refunds restore stock automatically | Must | 1 | Done |
| RET-07 | No-receipt refund with manager override | Could | 6 | Planned |

## 4. Catalogue

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| CAT-01 | Products with SKU, barcode, name, unit | Must | 1 | Done |
| CAT-02 | Categories | Must | 1 | Done |
| CAT-03 | Cost price captured for margin reporting | Must | 1 | Done |
| CAT-04 | Tax rate per product | Must | 1 | Done |
| CAT-05 | Price and cost snapshotted onto each sale line | Must | 1 | Done |
| CAT-06 | Bulk import from CSV or Excel | Should | 5 | Planned |
| CAT-07 | Multiple barcodes for one product | Should | 1 | Done (schema) |
| CAT-08 | Product images for the category grid | Could | 5 | Planned |
| CAT-09 | Effective-dated price changes | Could | 7 | Planned |

## 5. Inventory

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| INV-01 | Stock as an append-only movement ledger | Must | 1 | Done |
| INV-02 | Stock on hand derived, never a mutable column | Must | 1 | Done |
| INV-03 | Sales and refunds post movements automatically | Must | 1 | Done |
| INV-04 | Goods received notes | Must | 6 | Planned |
| INV-05 | Manual adjustment with a reason code | Must | 6 | Planned |
| INV-06 | Wastage and damage recording | Should | 6 | Planned |
| INV-07 | Stock take with variance report | Should | 6 | Planned |
| INV-08 | Reorder level alerts | Should | 6 | Planned |
| INV-09 | Negative stock permitted offline | Must | 1 | Done |
| INV-10 | Supplier records and purchase history | Could | 6 | Planned |
| INV-11 | Batch and expiry tracking | Won't | - | Out of scope, see PLAN.md |

## 6. Cash control and shifts

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| CSH-01 | Open a shift with a counted float | Must | 1 | Done |
| CSH-02 | One open shift per terminal, enforced by the database | Must | 1 | Done |
| CSH-03 | X report, mid-shift, non-closing | Must | 1 | Done (domain) |
| CSH-04 | Z report on close with expected versus counted variance | Must | 1 | Done (domain) |
| CSH-05 | Cash pay-in and pay-out with a reason | Must | 1 | Done (domain) |
| CSH-06 | Cash drop to safe | Should | 1 | Done (domain) |
| CSH-07 | Blind close, cashier cannot see expected cash first | Should | 2 | Planned |
| CSH-08 | Variance alert to the owner above a threshold | Could | 5 | Planned |

## 7. Users and security

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| SEC-01 | Roles: owner, manager, cashier | Must | 1 | Done |
| SEC-02 | PIN login with Argon2id hashing | Must | 2 | Planned |
| SEC-03 | Lockout after repeated failed PIN entry | Must | 1 | Done (domain) |
| SEC-04 | Session lock between customers | Should | 2 | Planned |
| SEC-05 | Manager override prompt for privileged actions | Must | 1 | Done (domain) |
| SEC-06 | Immutable audit log of voids, discounts, price and drawer events | Must | 1 | Done |
| SEC-07 | Device enrolment with revocable tokens | Must | 4 | Planned |
| SEC-08 | TLS 1.3 with certificate pinning on the till | Must | 4 | Planned |
| SEC-09 | Granular permissions beyond the three roles | Could | 7 | Planned |

## 8. Hardware

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| HW-01 | ESC/POS receipt printing, USB and network | Must | 3 | Planned |
| HW-02 | Cash drawer opened through the printer | Must | 3 | Planned |
| HW-03 | Barcode scanner input, distinguished from typing | Must | 3 | Planned |
| HW-04 | Printer failure does not block the sale | Must | 3 | Planned |
| HW-05 | Reprint any receipt from history | Must | 3 | Planned |
| HW-06 | Weighing scale over serial | Should | 3 | Planned |
| HW-07 | 58mm and 80mm receipt layouts | Should | 3 | Planned |
| HW-08 | Customer-facing display | Could | 7 | Planned |
| HW-09 | Label and shelf-tag printing | Could | 7 | Planned |

## 9. Offline and sync

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| SYN-01 | Complete sales with zero connectivity | Must | 2 | Done (by design) |
| SYN-02 | Client-generated UUIDv7 identifiers | Must | 1 | Done |
| SYN-03 | Gapless per-terminal receipt numbering, offline safe | Must | 1 | Done |
| SYN-04 | Outbox with at-least-once delivery | Must | 1 | Done (schema) |
| SYN-05 | Idempotent server ingest, deduplicated on key | Must | 4 | Planned |
| SYN-06 | Reference data pull with a watermark cursor | Must | 4 | Planned |
| SYN-07 | Sync status visible on the till | Must | 4 | Planned |
| SYN-08 | Two terminals in one shop, both offline, reconcile on reconnect | Must | 4 | Planned |
| SYN-09 | Alert the owner when a till has not synced for 24 hours | Should | 5 | Planned |

## 10. Back office and reporting

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| BO-01 | Web login for owners and managers | Must | 5 | Planned |
| BO-02 | Product and price management | Must | 5 | Planned |
| BO-03 | User management | Must | 5 | Planned |
| BO-04 | Sales history and receipt lookup | Must | 5 | Planned |
| BO-05 | Audit trail viewer | Must | 5 | Planned |
| BO-06 | Daily sales summary dashboard | Must | 6 | Planned |
| BO-07 | Reports by product, category, cashier, hour | Should | 6 | Planned |
| BO-08 | Margin report from cost snapshots | Should | 6 | Planned |
| BO-09 | Tax summary for filing | Must | 6 | Planned |
| BO-10 | CSV and Excel export | Should | 6 | Planned |
| BO-11 | Scheduled email reports | Could | 7 | Planned |

## 11. Multi-shop and tenancy

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| TEN-01 | Shop as the tenant boundary on every table | Must | 1 | Done |
| TEN-02 | PostgreSQL row level security as the primary control | Must | 1 | Done |
| TEN-03 | EF Core query filters as defence in depth | Must | 1 | Done |
| TEN-04 | Automated cross-tenant leakage test | Must | 1 | Done |
| TEN-05 | Shop and terminal provisioning | Must | 5 | Planned |
| TEN-06 | Owner view spanning several branches | Should | 5 | Planned |
| TEN-07 | Stock transfer between branches | Could | 7 | Planned |

## 12. Customization

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| CFG-01 | Per-shop settings store | Must | 1 | Done (schema) |
| CFG-02 | Receipt template stored as data | Should | 7 | Planned |
| CFG-03 | Pricing rules: time, quantity, category | Should | 7 | Planned |
| CFG-04 | Custom fields on products and customers | Could | 7 | Planned |
| CFG-05 | Feature flags per shop | Could | 7 | Planned |
| CFG-06 | Multi-language interface, Sinhala and Tamil | Could | 7 | Planned |
| CFG-07 | Tenant-supplied scripts or plugins | Won't | - | Security and support burden outweigh the benefit |

## 13. Compliance

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| CMP-01 | Inclusive VAT extracted rather than added | Must | 1 | Done |
| CMP-02 | Tax rates as effective-dated data, never constants | Must | 1 | Done |
| CMP-03 | Exempt and zero-rated item handling | Must | 1 | Done |
| CMP-04 | Financial records immutable, enforced by database triggers | Must | 1 | Done |
| CMP-05 | Tax registration number printed on receipts | Must | 3 | Planned |
| CMP-06 | Out of PCI scope, card data never touches the system | Must | All | Done (by design) |
| CMP-07 | E-invoicing integration if mandated | Could | 7 | Confirm current rules before pilot |

## 14. Operations

| ID | Feature | Priority | Phase | Status |
|---|---|---|---|---|
| OPS-01 | Signed auto-update with staged rollout | Must | 7 | Planned |
| OPS-02 | Structured logging with correlation identifiers | Must | 1 | Planned |
| OPS-03 | OpenTelemetry traces and metrics | Should | 4 | Planned |
| OPS-04 | Health endpoints on the API | Must | 1 | Planned |
| OPS-05 | Terminal health: outbox depth, last sync, printer state | Must | 4 | Planned |
| OPS-06 | Point-in-time restore, rehearsed quarterly | Must | 7 | Planned |
| OPS-07 | Local nightly backup on the till | Should | 7 | Planned |
| OPS-08 | Remote support access to a terminal | Could | 7 | Planned |

---

## Definition of done

A feature is not done when the code works. It is done when all of the following hold:

| # | Requirement |
|---|---|
| 1 | Domain rules covered by tests in the same commit |
| 2 | Works offline if it belongs to the till |
| 3 | Scoped to a single shop and covered by the tenancy test |
| 4 | Errors handled with a message a cashier can act on |
| 5 | Privileged actions written to the audit log |
| 6 | CI green, including architecture tests |
| 7 | Reachable by keyboard alone, no mouse required |

---

## Non-functional targets

These are acceptance thresholds, not aspirations. A till that is slow is a till that gets replaced.

| Area | Target |
|---|---|
| Scan to line appearing | Under 100ms |
| Sale completion to receipt | Under 500ms |
| Application cold start | Under 5 seconds |
| Product search over 10,000 items | Under 150ms |
| Uptime of the till, independent of network | 100%, this is the central design bet |
| Sale data loss | Zero. A completed sale that vanishes ends the business relationship. |
| Local database growth | Under 500MB for two years of a busy minimart |
| Sync catch-up after 24 hours offline | Under 5 minutes |
