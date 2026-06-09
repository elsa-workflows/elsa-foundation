using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;

namespace Elsa.Activities.Runtime.Tasks;

/// <summary>
/// Publishes <see cref="OnActivityConstructorsInitializing"/> with an empty collection; the single
/// aggregating handler fills it with every registered <see cref="IActivityConstructor"/>; the
/// contributions are then flushed into the <see cref="IActivityConstructorRegistry"/>. Canonical
/// §2.6.1 Registry + StartUp Task sub-pattern.
/// </summary>
public sealed class ActivityConstructorsStartupTask(
    IEventPublisher publisher,
    IActivityConstructorRegistry registry)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var constructors = new List<IActivityConstructor>();
        await publisher.Publish(new OnActivityConstructorsInitializing(constructors), cancellationToken: cancellationToken);
        registry.AddAll(constructors);
    }
}
