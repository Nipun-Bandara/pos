# ADR 0002: Stock on hand is derived from an append-only ledger

- **Status:** Accepted
- **Date:** 2026-08-25
- **Relates to:** INV-01, INV-02, INV-03, INV-09

> Scope update (2026-11-09): [ADR 0005](0005-single-shop-windows-pos.md) replaces the
> multi-terminal offline/sync deployment assumptions. The local ledger/receipt rule remains
> applicable. The rationale below records the original decision.

## Context

The conventional design is a `QuantityOnHand` column updated on every sale. It is simple, fast
to read, and wrong for this product.

Two tills in one shop trade offline simultaneously. Both hold a copy of the catalogue. Both
sell the last tin. When they reconnect, a mutable counter has to be reconciled, and there is
no record of how it reached the value it holds.

## Decision

Stock movements are rows in an append-only ledger. Stock on hand is `SUM(signed quantity)`.

- Sales, refunds and voids post movements from domain events, inside the same transaction as
  the document that caused them. Nothing else writes to the ledger.
- Manual movements (adjustment, wastage, stock take) require a reason and a user.
- Rows are never updated or deleted; a database trigger enforces this. A mistake is corrected
  by a compensating movement.
- Stock is allowed to go negative.

## Consequences

**Good.** Reconciliation is addition, so two offline tills merge by construction with no
conflict resolution. Every movement is explainable: what, when, who, and which document. Stock
take variance is a first-class ledger entry rather than an unexplained correction.

**Costs.** Reading stock on hand is an aggregate, not a column read. Mitigated by an index on
`(ShopId, ProductId, OccurredAt)`; if it becomes a problem, the fix is a periodic snapshot
row, not a mutable counter. The table grows without bound and will eventually need archival —
noted against the 500MB-for-two-years target in `FEATURES.md`.

**On negative stock.** This is deliberate (INV-09). An offline till cannot know what another
till has just sold. A cashier holding the item in their hand is not helped by software
insisting it does not exist. Negative stock is a reporting signal, not an error condition.
