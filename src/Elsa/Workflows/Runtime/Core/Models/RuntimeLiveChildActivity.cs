namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// One direct, non-terminal child activity execution of a structural parent, surfaced to the parent's
/// child-completion/child-fault callback (spec 119 D4). Read-only and spoof-proof — projected from
/// committed activity-execution state, never from user input. A structural activity that races sibling
/// children (the BPMN event-based gateway) resolves a losing sibling's <see cref="ActivityExecutionId"/>
/// from its <see cref="ExecutableNodeId"/> to stage the sibling's subtree cancellation (spec 112).
/// <see cref="IterationId"/> (spec 121) disambiguates N concurrent same-node children of a multi-instance
/// host: a teardown resolves the right instance's <see cref="ActivityExecutionId"/> by
/// <c>(ExecutableNodeId, IterationId)</c> rather than node id alone. It is <c>null</c> for ordinary
/// single-run children.
/// </summary>
public sealed record RuntimeLiveChildActivity(
    string ActivityExecutionId,
    string ExecutableNodeId,
    ActivityExecutionStatus Status,
    string? IterationId = null);
