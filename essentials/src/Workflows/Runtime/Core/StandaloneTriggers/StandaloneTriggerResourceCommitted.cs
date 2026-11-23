using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>Post-commit management notification for rebuilding local source projections. It conveys no invocation authority.</summary>
/// <remarks>Replayed management requests emit this again to repair a projection failure after a durable mutation.</remarks>
public sealed class StandaloneTriggerResourceCommitted(TriggerScope scope, string triggerId, string typeKey, long revision) : IEvent
{
    public TriggerScope Scope { get; } = scope;
    public string TriggerId { get; } = triggerId;
    public string TypeKey { get; } = typeKey;
    public long Revision { get; } = revision;
}
