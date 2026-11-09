# ADR 0004: Receipt numbers are per terminal and gapless

- **Status:** Accepted
- **Date:** 2026-08-25
- **Relates to:** SYN-02, SYN-03

> Scope update (2026-11-09): [ADR 0005](0005-single-shop-windows-pos.md) replaces the
> multi-terminal offline/sync deployment assumptions. The local ledger/receipt rule remains
> applicable. The rationale below records the original decision.

## Context

Receipt numbers appear on paper the customer keeps, and a tax audit expects a sequence with no
unexplained holes. Meanwhile every till must be able to issue one while offline.

These pull in opposite directions. A shop-wide sequence needs the tills to agree with each
other, which is exactly what they cannot do with the network down.

## Decision

Numbering is per terminal, prefixed with the terminal's code: `T1-000042`.

- A `ReceiptCounter` row per terminal holds the last number issued.
- The counter is incremented **inside the transaction that writes the sale**. A crash between
  allocating and saving rolls both back, so no number is burned.
- The number is assigned at completion, not at basket creation, so an abandoned basket
  consumes nothing.
- The counter carries a concurrency token, incremented by the aggregate rather than by the
  provider, so a lost update fails loudly instead of duplicating a number.

Primary keys are separately UUIDv7 (SYN-02): unique without coordination, and time-ordered so
index inserts stay sequential rather than fragmenting the B-tree as random v4 GUIDs would.

## Consequences

**Good.** A till never asks anyone for a number. Two tills in one shop cannot collide, because
the terminal code disambiguates. The sequence is gapless per terminal and auditable.

**Costs.** There is no single shop-wide sequence, so a report ordering "all receipts" must
order by time, not by number. Adding a terminal adds a sequence, which is a provisioning step
(TEN-05) rather than something a till can do for itself.

**Rejected alternative.** Allocating numbers from the server on sync — this makes the number
unavailable at the moment the receipt is printed, which is the moment it is needed.
