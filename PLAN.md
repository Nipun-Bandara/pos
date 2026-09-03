# RetailCore POS: Delivery Plan

**Product:** Multi-tenant, offline-first point of sale for general retail (minimarts, grocery, hardware, pharmacy without batch tracking).
**Stack:** .NET 10, C# 14, Avalonia, EF Core, SQLite on the till, PostgreSQL centrally.
**Team:** One developer, part time.
**Planning assumption:** 15 productive hours per week. Estimates below use that figure. Halve your available hours during exam periods and the schedule slips accordingly. Plan for it rather than being surprised by it.

Architecture rationale lives in `ARCHITECTURE.md`. This document covers sequencing, not design.

---

## 1. Definition of success

This project is finished when **one real shop runs a full trading week on it without the developer present**. Not when the feature list is complete. Feature completeness is a proxy metric and a misleading one.

Intermediate proof points, in order of how much they actually derisk the project:

| Proof point | Why it matters |
|---|---|
| A cashier who has never seen the software rings 20 items without asking for help | UI is usable under real pressure |
| Network cable pulled mid-transaction, sale still completes and later syncs | The core architectural bet is sound |
| Two shops on one server, one cannot query the other, proven by test | Multi-tenancy is real, not aspirational |
| Z-report matches the physical cash in the drawer | The money model is correct end to end |
| A shop owner asks to keep using it after the trial | The product is worth building |

---

## 2. Phases

Each phase has an exit criterion. Do not begin the next phase until it is met. The most common way solo projects fail is starting phase N+1 while phase N is at 80%, repeatedly, until nothing is finished.

### Phase 0: Foundations
**Estimate:** 1 week. **Status:** Complete.

| Deliverable | State |
|---|---|
| Architecture document and decision record | Done. `ARCHITECTURE.md`, four ADRs in `docs/adr/` |
| PostgreSQL schema with row level security | Done. Policies and immutability triggers applied by migration |
| SQLite terminal schema with outbox | Done. Outbox table exists; the pump is Phase 4 |
| Domain model: Money, Quantity, TaxRate, Sale aggregate | Done and compiling |
| xUnit specification suite | Done. 132 domain specifications |
| Build settings, SDK pin, gitignore | Done. Warnings as errors, .NET 10 pinned |

**Exit criterion:** `dotnet test` green. **Met** — 166 tests pass across the three suites.

---

### Phase 1: Persistence and pipeline
**Estimate:** 4 weeks. **Status:** Complete, with one item unverified.

| Deliverable | State |
|---|---|
| EF Core mapping for the domain model | Done. Money maps to two columns, Quantity to integer thousandths |
| Migrations for both providers | Done. SQLite and PostgreSQL generated from one shared model |
| Repository and unit of work | Done. Domain events dispatched inside `SaveChanges` |
| Tenant interceptor | Done. `SET LOCAL app.shop_id` per transaction, plus session scope on connection open |
| Architecture tests | Done. 11 rules, including no raw `decimal` money and no dependencies from the domain |
| Integration tests | Done. 23 specifications against SQLite and a real PostgreSQL container |
| GitHub Actions CI | Written, **not yet run on GitHub**. The gates pass locally |

**Exit criterion:** a sale created in code round-trips to both databases and reads back with identical totals, and CI blocks a merge that breaks it.
**Met locally.** `SaleRoundTripSpecs` runs every specification against both providers. The CI workflow is committed but has never executed on a runner, so treat that half as unproven until the first push.

Two things worth carrying forward:

- The tenancy tests initially passed while proving nothing, because Testcontainers connects as the `postgres` superuser and PostgreSQL exempts superusers from row level security silently. They only became meaningful once pointed at an unprivileged role — and then failed, exposing a real bug. See ADR 0003.
- **Deployment constraint:** the application's database role must not be a superuser and must not own the tables, or row level security does nothing.

---

### Phase 2: The till
**Estimate:** 6 weeks. The largest and most underestimated phase.

| Deliverable | Notes |
|---|---|
| Avalonia shell, keyboard-first navigation | Cashiers do not use a mouse |
| Cashier login by PIN, session lock | Argon2id, lockout after 5 failures |
| Shift open with float, close with Z report | Cash reconciliation is the owner's core need |
| Product search: barcode, SKU, name, category grid | Sub-100ms or it feels broken |
| Cart: quantity edit, line void, line discount with manager override | |
| Weighed item entry | Manual weight now, scale in Phase 3 |
| Tender screen: cash, card, QR, split payment | |
| Change and cash rounding display | |
| Void and refund flows with reason capture | |
| Held and recalled sales | Customer forgot their wallet, next customer served |
| X report mid-shift | |

**Exit criterion:** an untrained person completes a 20 item mixed basket with a split payment and a refund, unaided.

---

### Phase 3: Hardware
**Estimate:** 3 weeks. **Blocked on:** buying the devices. Order them during Phase 2.

| Deliverable | Notes |
|---|---|
| ESC/POS receipt printing over USB and network | 80mm and 58mm layouts |
| Cash drawer kick through the printer | |
| Barcode scanner input handling | Scanners act as keyboards, so distinguish scan from typing by timing |
| Weighing scale integration | Serial protocol varies by brand, budget time for one specific model |
| Printer failure handling | A dead printer must not block the sale, offer reprint |
| Receipt reprint from history | |

**Exit criterion:** a full sale prints, opens the drawer, and survives the printer being switched off mid-transaction.

---

### Phase 4: Sync
**Estimate:** 5 weeks. Highest technical risk in the project.

| Deliverable | Notes |
|---|---|
| Device enrolment and revocation | Device-bound refresh token |
| Outbox pump with exponential backoff | At-least-once delivery |
| Server-side idempotent ingest | Deduplication on primary key |
| Reference data pull with watermark cursor | Products, prices, users, tax rates |
| Conflict-free replay proof | FsCheck property tests over shuffled and duplicated batches |
| Sync health surfaced on the till | Unsynced count, last successful sync |
| Multi-terminal test in one shop | Two tills, one shop, both offline, then both reconnect |

**Exit criterion:** run 500 sales across two terminals with the network randomly interrupted, and server totals match the sum of terminal totals exactly.

---

### Phase 5: Back office and multi-shop
**Estimate:** 5 weeks.

| Deliverable | Notes |
|---|---|
| Blazor back office: login, dashboard | |
| Product and price management | |
| User and role management | |
| Sales history, receipt lookup, audit trail | |
| Shop and terminal provisioning | |
| Multi-shop views for an owner with several branches | |
| RLS penetration test | Attempt cross-tenant reads with query filters disabled |

**Exit criterion:** two shops provisioned on one server, and an automated test proves neither can read the other's data.

---

### Phase 6: Inventory and reporting
**Estimate:** 4 weeks.

| Deliverable | Notes |
|---|---|
| Goods received notes and supplier records | |
| Stock adjustments and wastage with reason codes | |
| Stock take with variance report | |
| Reorder level alerts | |
| Sales reports: by day, product, category, cashier, hour | |
| Margin report using cost snapshots | |
| Tax summary for filing | |
| CSV and Excel export | Owners will ask for this on day one |

**Exit criterion:** an owner answers "what were my top 10 products and my margin last month" without calling you.

---

### Phase 7: Customization and pilot
**Estimate:** 4 weeks plus the pilot itself.

| Deliverable | Notes |
|---|---|
| Per-shop settings surface | |
| Receipt template editor | Templates stored as data, not code |
| Pricing rules engine | Time-based, quantity-based, category discounts |
| Custom fields on products and customers | |
| Auto-update channel with staged rollout | Velopack, signed |
| Backup and restore runbook, rehearsed | |
| Onboarding guide and cashier training sheet | |

**Exit criterion:** a second shop is onboarded with different settings and no code change.

---

## 3. Schedule

| Phase | Weeks | Cumulative |
|---|---|---|
| 0 Foundations | 1 | 1 (complete) |
| 1 Persistence | 4 | 5 (complete) |
| 2 Till | 6 | 11 |
| 3 Hardware | 3 | 14 |
| 4 Sync | 5 | 19 |
| 5 Back office | 5 | 24 |
| 6 Inventory and reporting | 4 | 28 |
| 7 Customization and pilot | 4 | 32 |
| Contingency at 25% | 8 | 40 |

**Roughly 10 months at 15 hours per week.** Contingency is not optional padding. Every project of this kind consumes it, and a plan without it is a plan to be late.

If you need something demonstrable sooner, phases 0 to 3 give a single-shop offline till at around week 14. That is a sellable product for one customer, and selling one customer early is worth more than the remaining features.

---

## 4. Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Sync bugs corrupt or lose sales | Medium | Severe | Append-only design removes merge conflicts by construction. Property tests over shuffled replays. Never delete outbox rows until the server acknowledges. |
| Scope creep into customization too early | High | High | Customization is Phase 7 by design. Any "can it also do X" goes to a backlog file, not into the sprint. |
| Hardware arrives late or the protocol differs | Medium | Medium | Order devices during Phase 2. Abstract behind `IReceiptPrinter` so a second brand is a new adapter. |
| Tax or e-invoicing rules change | Medium | High | Tax rates are data with an effective date, never constants. Confirm current rules with an accountant before pilot. |
| Single developer, no backup | High | High | Everything in Git from day one. Architecture and decisions written down. This document is part of that mitigation. |
| Coursework collides with the schedule | High | Medium | Contingency above. Prefer finishing a phase over starting two. |
| Pilot shop loses confidence after a bad day | Medium | Severe | Do not pilot until Phase 4 exit criteria pass. Keep the shop's old system available for one week in parallel. |
| Data loss at the shop | Low | Severe | SQLite in WAL mode with `synchronous=FULL`. Nightly local backup file. Server holds the authoritative copy once synced. |

---

## 5. Working agreements

| Area | Rule |
|---|---|
| Branching | Trunk based. Short-lived branches, merged within two days. |
| Commits | Conventional commits, for example `feat(sales): add split tender`. |
| CI gates | Build with warnings as errors, unit tests, architecture tests, integration tests, pending-migration check. A red build is never merged. |
| Migrations | Forward only. Never edit an applied migration. Correct with a new one. |
| Domain changes | Any change to pricing, tax, or rounding requires a test in the same commit and a reason in the message. |
| Secrets | Never in the repository. User Secrets locally, environment variables in production. |
| Versioning | Semantic versioning. The till reports its version so a support call starts with a known build. |
| Decisions | Anything architectural gets an ADR in `docs/adr/`, numbered and dated. |

---

## 6. Explicitly out of scope

Writing this down matters as much as the feature list, because these are the requests that will arrive and quietly consume months.

- Full accounting: ledger, trial balance, payroll. Export to an accountant's software instead.
- E-commerce or online ordering.
- Restaurant features: table management, kitchen order tickets, course timing.
- Manufacturing, assembly, or bill of materials.
- Pharmacy batch and expiry tracking. Reconsider after the first pilot if a pharmacy asks.
- Loyalty points and gift cards. Backlog for a later version.
- Mobile cashier app. The desktop till is the product.

A request in this list is not refused, it is scheduled. Move it into the plan deliberately or not at all.
