# ADR 0005: Deliver a single-shop Windows POS

- **Status:** Accepted
- **Date:** 2026-11-09
- **Supersedes:** ADR 0003 for the active product's tenancy/deployment requirements
- **Updates:** Deployment assumptions in ADRs 0002 and 0004; their ledger and receipt rules remain useful

## Context

The owner needs a simple POS for one retail shop using Windows computers. Required work is
billing, products, stock, barcode reading, Sinhala printing from a custom template, reports,
exports, four quantity-price bands, and bill-backed returns/exchanges. The owner explicitly
removed cloud/multi-tenant architecture and synchronization and confirmed Sinhala/English
application languages. Earlier Tamil suggestions are superseded.

## Decision

Deliver a Windows WPF application using the existing .NET domain and EF Core SQLite storage.
Assume one checkout computer initially; no remote service or PostgreSQL deployment is needed.
Provide Sinhala/English UI resources and Sinhala receipts independently of interface choice.
Reuse money, fractional quantity, transaction, audit and ledger rules.

Four bands mean normal unit price plus up to three quantity breaks. The highest qualifying
band prices all units of the product. Returns require the original bill and original paid
prices. Exchanges atomically link the return, replacement sale and net collect/refund amount.

Keep the supplied receipt template as a fixed layout. Support one tested printer first.
Backups, restore and daily cash reconciliation are prerequisites for shop use.

## Consequences and transition

The deployment and feature set become smaller. Multi-tenant controls, an outbox pump, server
ingest, device enrollment, branch management and web back office are not version 1 work.

Existing PostgreSQL projects, tenant fields/filters, migrations and tests remain historical
implementation until a deliberate code simplification. This decision updates the plan; it
does not mean those code paths have already been removed or new features implemented.
Preserve migrations and working domain behavior; replace legacy required checks only when
single-shop replacements are verified. Multiple checkout PCs need a new deployment decision.
