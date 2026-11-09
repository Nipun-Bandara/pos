# ADR 0003: Row level security is the tenancy control, not EF query filters

- **Status:** Superseded for the current product by [ADR 0005](0005-single-shop-windows-pos.md)
- **Date:** 2026-08-25
- **Relates to:** TEN-01, TEN-02, TEN-03, TEN-04

## Context

Several shops share one central database. A cross-tenant leak is not a bug to be fixed in the
next release; it ends the business relationship and probably the product.

EF Core global query filters are the usual answer. They are also easy to bypass, sometimes
accidentally: `IgnoreQueryFilters()`, raw SQL, a reporting tool, a migration, or a new entity
whose configuration nobody remembered to filter.

## Decision

PostgreSQL row level security is the control. EF query filters are defence in depth.

- Every tenant-scoped table has a policy comparing `ShopId` to `app.shop_id`, with both
  `USING` (reads) and `WITH CHECK` (writes).
- `app.shop_id` is set by an interceptor: `SET LOCAL` inside each transaction, and at session
  scope on connection open so that reads outside a transaction are covered too.
- Policies use `FORCE ROW LEVEL SECURITY` so they apply to the table owner as well.
- **The application connects as an unprivileged `retailcore_app` role.** Migrations run as the
  owner; nothing else does.
- Every entity implements `IBelongsToShop`, including child tables like `SaleLines`, because a
  table with no shop column cannot carry a policy. An architecture test enforces this.

## Consequences

**Good.** The isolation holds for queries nobody anticipated. `current_setting(..., true)`
returns NULL when unset and NULL never compares equal, so an unidentified connection sees
nothing — it fails closed.

**Costs.** SQLite has no row level security, so the till relies on query filters alone. That
is acceptable: a till holds exactly one shop's data, so there is no second tenant to leak to.

Deployment gains a hard requirement: the application's database role must not be a superuser
and must not own the tables.

## Note on how this was validated

The first run of the tenancy suite passed while proving nothing, because Testcontainers
connects as the `postgres` superuser and PostgreSQL exempts superusers from row level security
without any error. The suite only became meaningful once it was pointed at a real
unprivileged role — at which point it failed, and exposed a genuine bug: the interceptor set
the tenant on transaction start only, so reads outside a transaction were unfiltered.

Both the exemption and the bug are the reason `TenantIsolationSpecs` includes a test that
disables EF's filters entirely and a test that connects with no shop set at all.
