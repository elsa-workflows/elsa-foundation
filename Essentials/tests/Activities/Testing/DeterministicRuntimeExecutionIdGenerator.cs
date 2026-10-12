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
    private int _commandOrdinal;
    private int _envelopeOrdinal;

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

    // Numbered rather than constant: a checkpoint commit id derives from the command that produced it, so two
    // commands sharing an id collide as a replay conflict. That is invisible in a single-execution test and fatal
    // in one that starts a second workflow (a dispatched child), whose start command is generated, not authored.
    public string NewWorkflowExecutionCommandId() => $"command-generated-{Interlocked.Increment(ref _commandOrdinal)}";

    public string NewWorkflowExecutionCommandEnvelopeId() => $"envelope-generated-{Interlocked.Increment(ref _envelopeOrdinal)}";

    public string NewActivityExecutionId() =>
        _activityExecutionIds.TryDequeue(out var activityExecutionId)
            ? activityExecutionId
            : throw new InvalidOperationException("No deterministic activity execution ID is available.");
}
