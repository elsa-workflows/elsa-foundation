using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Events;

/// <summary>Sequential, inline validation before a bookmark trigger claim or CLR activation.</summary>
/// <remarks>
/// Scoped handlers skip activity families they do not own. Zero handlers is the default.
/// Handlers validate only; they never mutate execution or delivery state. Exceptions stop the publisher.
/// A temporarily unavailable dependency uses RuntimeSchedulerWorkUnavailableException so the unconsumed
/// resume remains owed. Conflicting data fails closed.
/// </remarks>
public sealed class ActivityResumeValidating(ActivityExecutionState state, ExecutableNode node,
    RuntimeResumeBookmarkCommandPayload payload) : IEvent
{
    public ActivityExecutionState State { get; } = state;
    public ExecutableNode Node { get; } = node;
    public RuntimeResumeBookmarkCommandPayload Payload { get; } = payload;
}
