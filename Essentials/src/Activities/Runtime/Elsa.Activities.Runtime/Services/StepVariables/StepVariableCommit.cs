using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services.StepVariables;

/// <summary>
/// Applies a step's session writes in its checkpoint commit (ADR 0090 decision 6, spec 1231 FR-011–FR-016): each intent
/// passes S-10's commit rule at the session write site, with the node's lost-update mode and the base the intent was
/// built on, against the variable's frame as it is committed now. A value equal to the stored one writes nothing and keeps
/// the version (C4). Every applied write and every lost update leaves a write-history record with source
/// <see cref="VariableWriteSource.SessionChange"/>. A <c>Fault</c> lost update refuses the whole step: nothing it staged
/// applies. Intents apply first; the step's captures then apply on the returned workflow state, so the capture wins.
/// </summary>
internal static class StepVariableCommit
{
    /// <summary>
    /// <see cref="ApplyAsync"/>, with a commit the step's state no longer allows (a written container frame was closed,
    /// or its owner is gone) turned into a step fault: the result carries <see cref="StepVariableCommitResult.Failure"/>
    /// and applies nothing, so the handler records it through its ordinary fault path.
    /// </summary>
    public static async ValueTask<StepVariableCommitResult> TryApplyAsync(
        StepVariableSession? session,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        string workflowExecutionId,
        ExecutableNode node,
        ActivityExecutionState invocation,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ApplyAsync(session, workflowExecutionStateStore, activityExecutionStateStore, workflowExecutionId, node, invocation, occurredAt, cancellationToken);
        }
        catch (StepVariableCommitRefusedException exception)
        {
            return StepVariableCommitResult.None with { Failure = exception };
        }
    }

    public static async ValueTask<StepVariableCommitResult> ApplyAsync(
        StepVariableSession? session,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        string workflowExecutionId,
        ExecutableNode node,
        ActivityExecutionState invocation,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(invocation);
        if (session is null)
            return StepVariableCommitResult.None;

        var reads = session.Reads;
        var intents = session.Intents;
        if (intents.Count == 0)
            return StepVariableCommitResult.None with { Invocation = invocation.WithSessionReads(reads) };

        var mode = LostUpdateModes.Of(node);
        var writer = new VariableWriter(VariableWriteSource.SessionChange, node.ExecutableNodeId, invocation.InvocationId, session.AttemptId);
        WorkflowExecutionState? workflow = null;
        var owners = new Dictionary<string, ActivityExecutionState>(StringComparer.Ordinal);
        var records = new List<VariableWriteRecord>();
        var written = new List<RecordedVariableWrite>();

        foreach (var intent in intents)
        {
            VariableFrameState frame;
            if (intent.OwnerInvocationId is null)
            {
                workflow ??= await workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken)
                             ?? throw new StepVariableCommitRefusedException(
                                 $"Node '{node.ExecutableNodeId}' writes workflow variable '{intent.Name}', but workflow execution '{workflowExecutionId}' has no durable state.");
                frame = workflow.RootVariableFrame
                        ?? throw new StepVariableCommitRefusedException($"Workflow execution '{workflowExecutionId}' has no canonical root variable frame.");
            }
            else
            {
                if (!owners.TryGetValue(intent.OwnerInvocationId, out var owner))
                {
                    owner = await activityExecutionStateStore.FindAsync(workflowExecutionId, intent.OwnerInvocationId, cancellationToken)
                            ?? throw new StepVariableCommitRefusedException(
                                $"Node '{node.ExecutableNodeId}' writes variable '{intent.Name}' of scope '{intent.ScopeId}', whose owner '{intent.OwnerInvocationId}' no longer exists.");
                    owners[owner.InvocationId] = owner;
                }

                frame = owner.VariableFrame is { Status: VariableFrameStatus.Active } active && StringComparer.Ordinal.Equals(active.ScopeId, intent.ScopeId)
                    ? active
                    : throw new StepVariableCommitRefusedException(
                        $"Node '{node.ExecutableNodeId}' writes variable '{intent.Name}' of scope '{intent.ScopeId}', which is no longer active.");
            }

            if (!frame.Values.ContainsKey(intent.VariableKey))
                throw new StepVariableCommitRefusedException(
                    $"Node '{node.ExecutableNodeId}' writes variable '{intent.Name}', which scope '{intent.ScopeId}' no longer declares.");
            var write = frame.Commit(intent.VariableKey, intent.Value, frame.Revision, intent.Base, mode, writer);
            if (VariableWriteRecord.IsRecorded(write.Decision))
            {
                var record = VariableWriteRecord.From(frame, write, intent.VariableKey, writer, occurredAt) with
                {
                    DroppedMembers = intent.DroppedMembers
                };
                if (write.Decision.Outcome == VariableCommitOutcome.LostUpdateFault)
                {
                    // Nothing the step staged applies; only the refused write is kept, on the faulted invocation.
                    var refused = record with { RejectedValue = intent.Value };
                    return new StepVariableCommitResult(null, [], [refused], refused, [])
                    {
                        Invocation = invocation.WithSessionReads(reads).WithVariableWrites([refused])
                    };
                }

                records.Add(record);
                if (write.Decision.ChangesValue)
                    written.Add(new RecordedVariableWrite(intent.VariableKey, intent.Value));
            }

            if (intent.OwnerInvocationId is null)
                workflow = workflow! with { RootVariableFrame = write.Frame };
            else
                owners[intent.OwnerInvocationId] = owners[intent.OwnerInvocationId] with { VariableFrame = write.Frame };
        }

        var advanced = InvocationVariableBases.Advance(invocation, records)
            .WithVariableWrites(records)
            .WithSessionReads(reads);
        return new StepVariableCommitResult(workflow, owners.Values.ToArray(), records, null, written) { Invocation = advanced };
    }
}

/// <summary>
/// What a step's session writes changed: the workflow state with its root frame written (null when no workflow variable
/// was written), the activities whose container frames were written, the write-history records, the <c>Fault</c> lost
/// update that refuses the step, and <see cref="Invocation"/>: the step's own state with its records, advanced bases
/// (FR-010) and session reads.
/// </summary>
internal sealed record StepVariableCommitResult(
    WorkflowExecutionState? Workflow,
    IReadOnlyList<ActivityExecutionState> Owners,
    IReadOnlyList<VariableWriteRecord> Records,
    VariableWriteRecord? LostUpdateFault,
    IReadOnlyList<RecordedVariableWrite> Written)
{
    public static StepVariableCommitResult None { get; } = new(null, [], [], null, []);

    /// <summary>A commit the step's state no longer allows; the step faults with it and nothing applies.</summary>
    public Exception? Failure { get; init; }

    /// <summary>The invocation after the commit; null when there was no session.</summary>
    public ActivityExecutionState? Invocation { get; init; }

    /// <summary>The invocation to commit: <see cref="Invocation"/> when the session changed it, else <paramref name="fallback"/>.</summary>
    public ActivityExecutionState Apply(ActivityExecutionState fallback) => Invocation ?? fallback;
}

/// <summary>
/// A step's session writes that its state no longer allows (no root frame, a gone owner, a closed container frame): the
/// step faults with it. Store and infrastructure failures are not this, so they keep their own retry handling.
/// </summary>
internal sealed class StepVariableCommitRefusedException(string message) : InvalidOperationException(message);
