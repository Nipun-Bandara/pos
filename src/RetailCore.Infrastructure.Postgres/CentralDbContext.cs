using Microsoft.EntityFrameworkCore;
using RetailCore.Application.Abstractions;
using RetailCore.Infrastructure.Persistence;

namespace RetailCore.Infrastructure.Postgres;

/// <summary>
/// The central database, holding every shop.
/// </summary>
/// <remarks>
/// The same model as the till (<see cref="RetailCoreDbContext"/>), because PLAN.md Phase 1
/// exits when a sale round-trips through both and reads back with identical totals. What
/// differs is the tenancy posture: here many shops share the database and row level security
/// (TEN-02) keeps them apart, whereas a till holds exactly one shop.
/// </remarks>
public sealed class CentralDbContext(
    DbContextOptions<CentralDbContext> options,
    ITenantContext tenantContext,
    IDomainEventDispatcher? eventDispatcher = null)
    : RetailCoreDbContext(options, tenantContext, eventDispatcher);
