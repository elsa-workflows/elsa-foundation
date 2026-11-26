using Elsa.Activities.Runtime.Services.StepState;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

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
        CancellationToken cancellationToken,
        StepWorkflowState? workflowState = null,
        IWorkflowStateChangePreparer? preparer = null,
        ClosedScopeRowCommit? rowCommit = null)
    {
        try
        {
            var sourcePolicy = SourcePolicy(session, workflowState);
            var variables = await ApplyAsync(session, workflowExecutionStateStore, activityExecutionStateStore, workflowExecutionId, node, invocation, occurredAt, cancellationToken, sourcePolicy, rowCommit);
            return await StepStateCommit.PrepareAsync(workflowState, variables, workflowExecutionStateStore, preparer,
                workflowExecutionId, invocation, occurredAt, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return StepVariableCommitResult.None with { Failure = exception };
        }
    }

    private static ValueProtectionPolicy? SourcePolicy(StepVariableSession? session, StepWorkflowState? workflowState) =>
        session?.Intents.Count > 0 && workflowState?.ReadPolicies.Any() == true ? workflowState.SourcePolicy() : null;

    /// <summary>
    /// Spec 1241 (ADR 0092 D-9): applies the step's writes into its closed scope's variable rows through
    /// <paramref name="rowCommit"/>, once; a later call returns the same result. The invoke and resume handlers call it ahead
    /// of the capture projection, so the step's captures are judged against the rows as its session writes left them
    /// (FR-007); <see cref="ApplyAsync"/> then reuses the result.
    /// </summary>
    public static async ValueTask<ClosedScopeSessionRowWrites> ApplyRowIntentsAsync(
        StepVariableSession? session,
        ClosedScopeRowCommit rowCommit,
        string workflowExecutionId,
        ExecutableNode node,
        ActivityExecutionState invocation,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken,
        StepWorkflowState? workflowState = null)
    {
        try
        {
            return await ApplyRowIntentsAsync(session, rowCommit, workflowExecutionId, node, invocation, occurredAt, cancellationToken, SourcePolicy(session, workflowState));
        }
        catch (InvalidOperationException exception)
        {
            // The step faults with it when its commit applies the session's writes.
            return rowCommit.SessionWrites = ClosedScopeSessionRowWrites.None with { Failure = exception };
        }
    }

    private static async ValueTask<ClosedScopeSessionRowWrites> ApplyRowIntentsAsync(
        StepVariableSession? session,
        ClosedScopeRowCommit rowCommit,
        string workflowExecutionId,
        ExecutableNode node,
        ActivityExecutionState invocation,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken,
        ValueProtectionPolicy? sourcePolicy)
    {
        ArgumentNullException.ThrowIfNull(rowCommit);
        if (rowCommit.SessionWrites is { } applied)
        {
            // The failure of the first application, with its original stack trace.
            if (applied.Failure is { } failure)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
            return applied;
        }

        var intents = session?.Intents.Where(intent => intent.ClosedScopeBoundaryId is not null).ToArray() ?? [];
        if (intents.Length == 0)
            return rowCommit.SessionWrites = ClosedScopeSessionRowWrites.None;

        var boundary = session!.ClosedScopeBoundary
                       ?? throw new StepVariableCommitRefusedException($"Node '{node.ExecutableNodeId}' writes a reusable activity's variable outside its body.");
        var writer = new VariableWriter(VariableWriteSource.SessionChange, node.ExecutableNodeId, invocation.InvocationId, session.AttemptId);
        var changes = new List<RuntimeStateChange<DurableValueState>>();
        var records = new List<VariableWriteRecord>();
        foreach (var intent in intents)
        {
            if (!StringComparer.Ordinal.Equals(intent.ClosedScopeBoundaryId, boundary.InvocationId))
                throw new StepVariableCommitRefusedException(
                    $"Node '{node.ExecutableNodeId}' writes variable '{intent.Name}' of reusable activity execution '{intent.ClosedScopeBoundaryId}', which is not the body it runs in.");
            var value = WithSourcePolicy(intent.Value, sourcePolicy);
            var id = ActivityExecutionScopeKeys.Variable(boundary.InvocationId, intent.VariableKey);
            var stored = await rowCommit.FindAsync(workflowExecutionId, id, cancellationToken)
                         ?? throw new StepVariableCommitRefusedException(
                             $"Node '{node.ExecutableNodeId}' writes variable '{intent.Name}', which its reusable activity's body no longer stores.");
            var row = await EncodeRowAsync(rowCommit, stored, value, occurredAt, cancellationToken);
            var (change, record) = await rowCommit.CommitAsync(workflowExecutionId, node, row, boundary, intent.Base, writer, value.Policy, occurredAt, cancellationToken);
            if (record is not null)
                record = record with { DroppedMembers = intent.DroppedMembers };
            if (record?.Outcome == VariableCommitOutcome.LostUpdateFault)
                return rowCommit.SessionWrites = new ClosedScopeSessionRowWrites([], [record], record);
            if (record is not null)
                records.Add(record);
            changes.Add(change);
        }

        return rowCommit.SessionWrites = new ClosedScopeSessionRowWrites(changes, records, null);
    }

    // The row keeps what it was seeded with (declared name, storage driver, protection minimum); the value is re-encoded
    // by the row's own driver, and the commit sets the version metadata again.
    private static async ValueTask<DurableValueState> EncodeRowAsync(
        ClosedScopeRowCommit rowCommit,
        DurableValueState stored,
        ValueEnvelope value,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var driverKey = stored.Metadata.TryGetValue(RuntimeMetadataKeys.StorageDriverKey, out var key) ? key : WellKnownRuntimeDurableValueStorageDrivers.Json;
        object? clrValue = value.Presence == ValuePresence.Present ? value.InlineValue : null;
        var encoding = await rowCommit.StorageDrivers.GetRequired(driverKey).EncodeAsync(clrValue, stored.Type, cancellationToken);
        var metadata = stored.Metadata
            .Where(item => item.Key is not (RuntimeMetadataKeys.RowVariableVersion or RuntimeMetadataKeys.RowVariableDigest or RuntimeMetadataKeys.RowVariableWriter))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        return new DurableValueState(
            stored.DurableValueId,
            stored.WorkflowExecutionId,
            stored.ValueId,
            stored.Type,
            stored.Lifecycle,
            encoding.Storage,
            encoding.InlineValue,
            encoding.ExternalReference,
            stored.SourceActivityExecutionId,
            occurredAt,
            RuntimeModelMetadata.Snapshot(metadata));
    }

    private static ValueEnvelope WithSourcePolicy(ValueEnvelope value, ValueProtectionPolicy? sourcePolicy)
    {
        if (sourcePolicy is null)
            return value;
        var policy = ValuePolicyCombiner.Combine(value.Policy, sourcePolicy, "Step variable after workflow-state reads");
        if (policy.RequiresEncryption || policy.Storage != DurableValueStorage.Inline)
            throw new StepVariableCommitRefusedException("VF-STATE-003: The variable destination cannot encode the workflow-state source protection.");
        return new ValueEnvelope(value.Type, value.Presence, value.InlineValue, value.ExternalReference, policy, value.WithheldValue);
    }

    public static async ValueTask<StepVariableCommitResult> ApplyAsync(
        StepVariableSession? session,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        string workflowExecutionId,
        ExecutableNode node,
        ActivityExecutionState invocation,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken,
        ValueProtectionPolicy? sourcePolicy = null,
        ClosedScopeRowCommit? rowCommit = null)
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

        // Spec 1241: writes into the closed scope's variable rows; on completion the handler applied them already, ahead of
        // the capture projection, and this reuses that result.
        var rows = ClosedScopeSessionRowWrites.None;
        if (intents.Any(intent => intent.ClosedScopeBoundaryId is not null))
        {
            if (rowCommit is null)
                throw new StepVariableCommitRefusedException(
                    $"Node '{node.ExecutableNodeId}' writes a variable of the reusable activity it runs in, but no durable value store is available to commit it.");
            rows = await ApplyRowIntentsAsync(session, rowCommit, workflowExecutionId, node, invocation, occurredAt, cancellationToken, sourcePolicy);
            if (rows.LostUpdateFault is { } rowRefused)
                return new StepVariableCommitResult(null, [], [rowRefused], rowRefused, [])
                {
                    Invocation = invocation.WithSessionReads(reads).WithVariableWrites([rowRefused])
                };
            records.AddRange(rows.Records);
        }

        foreach (var intent in intents.Where(intent => intent.ClosedScopeBoundaryId is null))
        {
            var intentValue = WithSourcePolicy(intent.Value, sourcePolicy);
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
            var write = frame.Commit(intent.VariableKey, intentValue, frame.Revision, intent.Base, mode, writer);
            if (VariableWriteRecord.IsRecorded(write.Decision))
            {
                var record = VariableWriteRecord.From(frame, write, intent.VariableKey, writer, occurredAt) with
                {
                    DroppedMembers = intent.DroppedMembers
                };
                if (write.Decision.Outcome == VariableCommitOutcome.LostUpdateFault)
                {
                    // Nothing the step staged applies; only the refused write is kept, on the faulted invocation.
                    var refused = record with { RejectedValue = intentValue };
                    return new StepVariableCommitResult(null, [], [refused], refused, [])
                    {
                        Invocation = invocation.WithSessionReads(reads).WithVariableWrites([refused])
                    };
                }

                records.Add(record);
                if (write.Decision.ChangesValue)
                    written.Add(new RecordedVariableWrite(intent.VariableKey, intentValue));
            }

            if (intent.OwnerInvocationId is null)
                workflow = workflow! with { RootVariableFrame = write.Frame };
            else
                owners[intent.OwnerInvocationId] = owners[intent.OwnerInvocationId] with { VariableFrame = write.Frame };
        }

        var advanced = InvocationVariableBases.Advance(invocation, records)
            .WithVariableWrites(records)
            .WithSessionReads(reads);
        return new StepVariableCommitResult(workflow, owners.Values.ToArray(), records, null, written) { Invocation = advanced, RowValues = rows.Changes };
    }
}

/// <summary>
/// What a step's session wrote into its closed scope's variable rows (spec 1241): the row changes to commit, the
/// write-history records, the <c>Fault</c> lost update that refuses the step, and a commit the step's state no longer
/// allows. A row write keeps no value snapshot, as a row capture keeps none.
/// </summary>
internal sealed record ClosedScopeSessionRowWrites(
    IReadOnlyCollection<RuntimeStateChange<DurableValueState>> Changes,
    IReadOnlyList<VariableWriteRecord> Records,
    VariableWriteRecord? LostUpdateFault)
{
    public static ClosedScopeSessionRowWrites None { get; } = new([], [], null);

    /// <summary>A write the step's state no longer allows; the step faults with it when its commit applies.</summary>
    public InvalidOperationException? Failure { get; init; }
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

    public IReadOnlyCollection<RuntimeStateChange<DurableValueState>> StateValues { get; init; } = [];
    public bool HasStateEffects { get; init; }

    /// <summary>The closed-scope variable rows the step's session wrote (spec 1241), committed ahead of its captures.</summary>
    public IReadOnlyCollection<RuntimeStateChange<DurableValueState>> RowValues { get; init; } = [];


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
