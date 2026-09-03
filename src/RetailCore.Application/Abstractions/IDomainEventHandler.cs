using System.Diagnostics.CodeAnalysis;
using RetailCore.Domain.Common;

namespace RetailCore.Application.Abstractions;

/// <summary>Reacts to something that happened in the domain, inside the same transaction.</summary>
/// <typeparam name="TEvent">The event handled.</typeparam>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The rule reserves the EventHandler suffix for CLR event delegates. This is a " +
                    "domain event handler in the usual sense of the term, and renaming it to satisfy " +
                    "the analyser would make it less recognisable to anyone reading the code.")]
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>Finds and runs the handlers for events raised during a unit of work.</summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken = default);
}
