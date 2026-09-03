# RetailCore POS: Architecture

**Scope:** why the system is shaped the way it is. Sequencing lives in `PLAN.md`; the feature
list lives in `FEATURES.md`. Individual decisions with a date and a status are in `docs/adr/`.

---

## 1. The central bet

**A till must keep trading when the network does not.**

Every other decision here follows from that. A minimart in a power cut with a flaky connection
still has customers holding baskets. Software that stops selling under those conditions is
worse than a cash box and a notebook, and will be replaced by one.

Concretely, that bet forces:

| Consequence | Why it follows |
|---|---|
| The till owns a local database | It cannot ask a server whether a sale may proceed |
| Identifiers are generated on the client | Primary keys must be mintable with no coordination |
| Receipt numbers are per terminal | A shop-wide sequence needs agreement between tills |
| Stock is a ledger, not a counter | Two offline tills will both sell the last unit |
| Sync is one-way append, not merge | Merge conflicts on money have no correct resolution |
| Negative stock is legal | The alternative is refusing to sell goods that are in the customer's hand |

---

## 2. Layers

```
RetailCore.Domain            no dependencies at all
        ↑
RetailCore.Application       abstractions, use cases, event handlers
        ↑
RetailCore.Infrastructure    EF Core model, repositories, tenancy
        ↑
   ┌────┴─────┐
Sqlite     Postgres          providers and migrations
(till)     (central)
```

The domain project has no `PackageReference` and no `ProjectReference`. That is asserted by
`RetailCore.Architecture.Tests`, not merely intended. The rules a shop runs on should be
readable and testable without a database, a container or a UI framework in the way.

Provider-specific code is confined to the two leaf projects. The EF model is shared, because
Phase 1's exit criterion is that a sale round-trips through both databases with identical
totals — a claim that means nothing if the two sides have separate models.

---

## 3. The money model

### Money is an integer count of minor units

`Money` holds a `long` of minor units and an ISO currency code. Addition and subtraction are
exact. The only operation that can round is multiplication, and every multiplication forces
the caller to choose a rounding mode.

Two failures are being designed out:

- **Mixing currencies.** `Money` refuses to add LKR to USD rather than producing a number.
- **Fractional minor units.** A half-cent that survives into a total is a variance against the
  physical drawer that nobody can explain a day later.

An architecture test fails the build if a `decimal` named like money appears on a domain type.

### Rounding happens once, at the line

A line's extended amount is `round(unit price × quantity)`. Every total downstream is a sum of
already-rounded lines. Rounding at the end instead would produce a total that does not equal
the sum of the printed line amounts, which customers notice and cannot be argued with.

### Allocation never loses a unit

A whole-sale discount is apportioned across lines by the largest remainder method: floor each
share, then hand the leftover units to the shares with the largest discarded fraction. Ten
rupees across three equal lines becomes 3.34, 3.33, 3.33 — and the printed line totals still
sum to the printed sale total.

### Tax is extracted, not added

Shelf prices include tax (CMP-01), so the tax in a line is `gross × rate ÷ (1 + rate)`, not
`gross × rate`. The second formula overstates tax by the rate squared — at 18% that is a 3%
error on every line, in the shop's favour, and it is the kind of thing found during an
assessment.

### Cash rounds to the smallest coin, card does not

`Currency` carries both the minor-unit precision and the smallest coin in circulation, which
are not the same number. Sri Lanka quotes cents but the smallest coin is one rupee, so a cash
total of 249.60 is collected as 250.00 and the 0.40 is recorded as its own rounding
adjustment. Card and QR settle exactly, because rounding them would put the shop permanently
out of step with the acquirer.

The adjustment is held separately from the total. It is neither income nor a discount, and
burying it in either makes the Z report unexplainable.

---

## 4. Aggregates

| Aggregate | Boundary and reason |
|---|---|
| `Sale` | Lines and payments are reachable only through it, so "a short payment cannot complete a sale" is an invariant rather than a UI check |
| `Refund` | Separate from `Sale`, because financial records are immutable and one sale may be refunded over several visits |
| `Shift` | Owns cash movements; sales are counted by query, so a shift's transaction does not span the day's trading |
| `Product`, `Category`, `TaxRate` | Reference data, edited centrally, snapshotted onto sale lines |
| `StockMovement` | Not an aggregate root by itself — an append-only ledger entry |

### Totals are derived, never assigned

`Sale`'s totals have private setters and one writer, `Recalculate()`. They are *stored* so
reporting can aggregate them in SQL, but nothing outside the aggregate can write one. An
architecture test asserts there is no public setter.

### Snapshots, not references

A sale line copies the product's description, price, cost and tax treatment. A receipt
reprinted next year has to show what was charged then, and the margin report has to use the
cost that applied on the day. A foreign key to live reference data would silently rewrite
history every time a price changed.

---

## 5. Events and the unit of work

Domain events are raised by aggregates and dispatched **inside** `SaveChanges`, before the
write. Handlers add their own entities, so everything lands in one transaction:

```
Sale.Complete()
  → SaleCompleted
      → stock movements   (INV-03)
      → audit entries     (SEC-06)
      → outbox row        (SYN-04)
  → one commit, or none of it
```

A handler that fails takes the sale down with it. That is correct: a sale whose stock
movements did not post is worse than no sale at all. There is no window in which the shop has
sold something the ledger does not know about.

Nothing decrements stock directly. There is exactly one path from "goods left the shop" to
"the ledger says so", which is what makes the ledger a complete account.

---

## 6. Multi-tenancy

Three layers, in order of how much they are trusted:

1. **PostgreSQL row level security.** The control. Policies compare `ShopId` against
   `app.shop_id`, which an interceptor sets with `SET LOCAL` inside each transaction and on
   each connection open. `current_setting(..., true)` returns NULL when unset, and NULL never
   compares true, so a connection that has not identified a shop sees nothing. It fails closed.

2. **EF Core query filters.** Defence in depth, applied by walking the model rather than being
   written per entity, so a new table cannot be added unfiltered.

3. **`IBelongsToShop` on every entity.** Including child tables like `SaleLines`. Row level
   security is a per-table policy, and a table with no shop column cannot have one.

**The application must not connect as a superuser or as the table owner.** PostgreSQL exempts
both from row level security *silently*. The RLS migration therefore creates an unprivileged
`retailcore_app` role, and the integration tests connect as it — a test that connected as the
container's `postgres` user would pass while proving nothing. This was not hypothetical: the
first run of the tenancy suite passed against a superuser connection and failed the moment it
was pointed at a real application role.

---

## 7. Immutability

Financial records are append-only, enforced by database triggers rather than convention
(CMP-04). The application is not the only thing that will ever hold a connection.

| Table | Rule |
|---|---|
| `StockMovements`, `AuditEntries`, `Payments`, `CashMovements` | No update, no delete, ever |
| `Sales` | Mutable while open; frozen on completion. The only permitted change to a completed sale is the void, which touches no financial column |
| `Refunds` | Frozen once completed |

A mistake is corrected by a compensating record, so the history of what was believed and when
stays intact.

---

## 8. Offline and sync

- **Identifiers** are UUIDv7 (SYN-02): unique without coordination, and sortable, so index
  inserts stay sequential instead of scattering the B-tree.
- **Receipt numbers** are per terminal and gapless (SYN-03). The counter is incremented in the
  same transaction that writes the sale, so a crash cannot burn a number, and an abandoned
  basket never consumes one. A gap has to be explainable to an auditor.
- **The outbox** is written in the same transaction as the change it describes. If the sale
  committed, the outbox row committed. Delivery is at least once and rows survive until the
  server acknowledges them, so server ingest must be idempotent. Duplicate delivery is cheap
  to handle; lost delivery is not recoverable.
- **Durability on the till:** WAL journalling with `synchronous=FULL`. A few milliseconds per
  commit buys the guarantee that a sale the cashier saw complete is on the disk, which in a
  shop with unreliable power is not a close trade.

---

## 9. What is deliberately not here

| Not doing | Why |
|---|---|
| Merge-based sync | Append-only removes conflicts by construction; there is no correct automatic resolution for two tills disagreeing about money |
| A mutable stock-on-hand column | It would be wrong the first time two offline tills sold the last unit, with no way to find out how |
| Storing card data | Staying out of PCI scope entirely is worth more than any feature it would enable |
| A permissions matrix | Three roles cover what a minimart needs; SEC-09 is a Phase 7 "could" |
| Rounding card payments | Permanent discrepancy against the acquirer |

---

## 10. Known gaps

Recorded here rather than discovered later:

- **The outbox is schema only.** Rows are not yet written or pumped; that is Phase 4.
- **`ReceiptCounter` uses an application-incremented concurrency token**, not a provider
  rowversion, because SQLite has no equivalent and PostgreSQL's `xmin` would behave
  differently. Uniform behaviour across providers was worth more than provider-native tokens.
- **No FsCheck property tests yet.** PLAN.md places the conflict-free replay proof in Phase 4,
  which is where they belong.
- **`SearchAsync` uses `LIKE '%term%'`**, which will not meet the 150ms-over-10,000-items
  target without a proper index. Revisit in Phase 2 when the till actually queries it.
