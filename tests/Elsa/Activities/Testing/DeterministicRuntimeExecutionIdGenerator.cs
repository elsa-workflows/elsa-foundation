using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Testing;

/// <summary>
/// Hands out caller-supplied activity-execution ids in order so that runtime tests can assert
/// parent/scheduling identity deterministically. The workflow execution id defaults to <c>wfexec-1</c>;
/// concurrency harnesses that run many distinct executions against one shared store pass a per-execution id
/// (see <see cref="WorkflowExecutionHarness.Builder.Build(WorkflowExecutableIdentity, string, IEnumerable{string})"/>).
/// </summary>
public sealed class DeterministicRuntimeExecutionIdGenerator : IRuntimeExecutionIdGenerator
{
    private readonly string _workflowExecutionId;
    private readonly Queue<string> _activityExecutionIds;

    public DeterministicRuntimeExecutionIdGenerator(IEnumerable<string> activityExecutionIds)
        : this(WorkflowExecutionHarness.WorkflowExecutionId, activityExecutionIds)
    {
    }

    public DeterministicRuntimeExecutionIdGenerator(string workflowExecutionId, IEnumerable<string> activityExecutionIds)
    {
        _workflowExecutionId = workflowExecutionId;
        _activityExecutionIds = new Queue<string>(activityExecutionIds);
    }

    public string NewWorkflowExecutionId() => _workflowExecutionId;
    public string NewWorkflowExecutionCommandId() => "command-generated";
    public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-generated";

    public string NewActivityExecutionId() =>
        _activityExecutionIds.TryDequeue(out var activityExecutionId)
            ? activityExecutionId
            : throw new InvalidOperationException("No deterministic activity execution ID is available.");
}
