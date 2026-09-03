# ADR 0001: Money is an integer count of minor units

- **Status:** Accepted
- **Date:** 2026-08-25
- **Relates to:** CMP-01, PAY-02, SAL-10

## Context

Every figure this system produces is eventually compared against physical cash in a drawer.
The obvious choice in .NET is `decimal`, which is exact for the arithmetic we do and is what
most codebases reach for.

Two problems are not solved by `decimal`:

1. It carries no currency. Adding rupees to dollars is a silent success.
2. It has no natural scale. A price of `1.005` is representable, and once one exists it
   propagates into totals as a half-cent that never reconciles.

## Decision

`Money` is a value type holding a `long` count of minor units and an ISO 4217 code.

- Addition and subtraction are exact and refuse mismatched currencies.
- Multiplication is the only rounding point, and every overload requires a rounding mode.
- `Money.FromMajor` **rejects** excess precision rather than rounding it away, so bad input
  fails at the edge instead of becoming a variance later.
- `Allocate` splits an amount by weights using the largest remainder method, guaranteeing the
  parts sum back to the whole.

An architecture test fails the build if a `decimal` named like an amount appears on a domain
type.

## Consequences

**Good.** Currency mismatches are compile-time-shaped errors at worst and loud exceptions at
best. Sums are exact. Storage is two ordinary columns, so reporting can `SUM` in SQL.

**Costs.** Every amount must be constructed deliberately; there is no implicit conversion from
a literal. Display requires `ToDecimal()`, which is documented as display-only.

**Rejected alternatives.** A `decimal` with a currency field beside it — still allows raw
arithmetic that skips the currency check. Storing money as a single string column — kills SQL
aggregation, which BO-06 through BO-09 all need.
