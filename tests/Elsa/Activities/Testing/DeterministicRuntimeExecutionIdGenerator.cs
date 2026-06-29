using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Testing;

/// <summary>
/// Hands out caller-supplied activity-execution ids in order so that runtime tests can assert
/// parent/scheduling identity deterministically. The single workflow execution is always <c>wfexec-1</c>.
/// </summary>
public sealed class DeterministicRuntimeExecutionIdGenerator(IEnumerable<string> activityExecutionIds) : IRuntimeExecutionIdGenerator
{
    private readonly Queue<string> _activityExecutionIds = new(activityExecutionIds);

    public string NewWorkflowExecutionId() => WorkflowExecutionHarness.WorkflowExecutionId;
    public string NewWorkflowExecutionCommandId() => "command-generated";
    public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-generated";

    public string NewActivityExecutionId() =>
        _activityExecutionIds.TryDequeue(out var activityExecutionId)
            ? activityExecutionId
            : throw new InvalidOperationException("No deterministic activity execution ID is available.");
}
