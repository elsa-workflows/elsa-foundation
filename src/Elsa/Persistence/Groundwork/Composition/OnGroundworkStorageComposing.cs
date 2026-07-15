using Elsa.Events.Core.Contracts;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Named contribution event for the one host-selected Groundwork storage composition. The Unified
/// aggregator adds each selected feature declaration to <see cref="Context"/> before freezing it.
/// </summary>
public sealed record OnGroundworkStorageComposing : IEvent
{
    public OnGroundworkStorageComposing(GroundworkStorageCompositionContext context) =>
        Context = context ?? throw new ArgumentNullException(nameof(context));

    public GroundworkStorageCompositionContext Context { get; }
}
