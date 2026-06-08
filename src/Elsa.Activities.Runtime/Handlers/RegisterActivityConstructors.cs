using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Events;
using Elsa.Events.Core.Contracts;

namespace Elsa.Activities.Runtime.Handlers;

/// <summary>
/// The single aggregating handler for <see cref="OnActivityConstructorsInitializing"/>. Collects every
/// <see cref="IActivityConstructor"/> registered in DI and adds it to the event's collection; the
/// startup task then flushes them into the registry. Features contribute a kind simply by registering
/// their <see cref="IActivityConstructor"/> — no per-consumer <c>IEnumerable&lt;TProvider&gt;</c>
/// injection (framework §2.6.1 / G21).
/// </summary>
public sealed class RegisterActivityConstructors(IEnumerable<IActivityConstructor> constructors)
    : IEventHandler<OnActivityConstructorsInitializing>
{
    public Task Handle(OnActivityConstructorsInitializing domainEvent, CancellationToken cancellationToken)
    {
        foreach (var constructor in constructors)
            domainEvent.Constructors.Add(constructor);

        return Task.CompletedTask;
    }
}
