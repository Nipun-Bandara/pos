using Microsoft.Extensions.DependencyInjection;
using RetailCore.Application.Abstractions;
using RetailCore.Domain.Common;

namespace RetailCore.Infrastructure.Persistence;

/// <summary>
/// Finds the handlers for each raised event and runs them.
/// </summary>
/// <remarks>
/// Resolution is by closed generic interface, so registering a handler is the only step
/// needed to make it run. Handlers execute inside the caller's transaction, which is what
/// makes stock movements (INV-03) and audit entries (SEC-06) part of the same commit as the
/// change that caused them.
/// </remarks>
public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync(
        IReadOnlyList<IDomainEvent> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        foreach (var domainEvent in events)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());

            foreach (var handler in services.GetServices(handlerType))
            {
                if (handler is null)
                {
                    continue;
                }

                var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
                await ((Task)method.Invoke(handler, [domainEvent, cancellationToken])!).ConfigureAwait(false);
            }
        }
    }
}
