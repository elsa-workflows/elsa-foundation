using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Describes the stimulus a start-trigger activity reacts to, at publish time (W7, E3-1). One provider is
/// registered per trigger activity type (e.g. the event/signal trigger); the trigger extractor asks each
/// provider to describe a node it recognizes. Returning <c>null</c> means "not my activity type" — the
/// extractor moves on to the next provider.
/// </summary>
/// <remarks>
/// Providers read only the published <see cref="ExecutableNode"/> (its literal input bindings), never a
/// running workflow, so the stimulus identity is fixed at publish time exactly as Elsa 4's pinned-executable
/// model requires. A provider whose trigger carries a non-literal, unresolvable stimulus key throws, which
/// fails the publish rather than persisting an unroutable trigger.
/// </remarks>
public interface IActivityTriggerStimulusProvider
{
    /// <summary>
    /// Returns the stimulus identity for <paramref name="node"/> if this provider recognizes its activity type;
    /// otherwise <c>null</c>.
    /// </summary>
    TriggerStimulusDescriptor? Describe(ExecutableNode node);
}
