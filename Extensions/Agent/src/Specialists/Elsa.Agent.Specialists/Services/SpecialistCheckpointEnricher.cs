using System.Collections.Concurrent;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Services;

// Records responsibility in the native checkpoint. No specialist-store writes occur here.
internal sealed class SpecialistCheckpointEnricher(SpecialistSettlementBoundary boundary, IActivityExecutionStateStore activities, IWorkflowExecutableReader executables) : IRuntimeCheckpointCommitEnricher
{
    // Only in-flight enrichment evidence, keyed by the stable commit identity. Later contributors
    // may replace the checkpoint object or add unrelated state. Final validation consumes the entry.
    private readonly ConcurrentDictionary<string, ExpectedIntents> _expected = new(StringComparer.Ordinal);
    private sealed record ExpectedPlan(IReadOnlyList<RuntimePostCommitIntent> Intents, IReadOnlySet<string>? FacadeNodes,
        IReadOnlyDictionary<string, ActivityExecutionState>? ScannedFacades = null);
    private sealed record NativeChange(string Id, RuntimeStateChangeOperation Operation, ActivityExecutionState State);
    private sealed record ExpectedIntents(string WorkflowId, RuntimeStateChange<WorkflowExecutionState>? Workflow,
        bool WholeCancelled, NativeChange[] Activities, ExpectedPlan Plan);

    /// <exception cref="SpecialistTurnException">A contribution failure retaining its cause and checkpoint/workflow identity.</exception>
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        try { return await EnrichCoreAsync(commit, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { _expected.TryRemove(commit.CommitId, out _); throw; }
        catch (Exception exception)
        {
            _expected.TryRemove(commit.CommitId, out _);
            throw SpecialistSettlementBoundary.Failure(commit.CommitId, commit.WorkflowExecutionId, exception);
        }
    }

    private async ValueTask<RuntimeCheckpointCommit> EnrichCoreAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        var expected = await ExpectedOnceAsync(commit, cancellationToken);
        var intents = commit.PostCommitIntents.ToList();
        foreach (var intent in expected)
        {
            var prior = intents.SingleOrDefault(item => item.IntentId == intent.IntentId);
            if (prior is null) intents.Add(intent);
            else if (!prior.IsEquivalentTo(intent)) throw SpecialistSettlementBoundary.Invalid();
        }
        return commit with { PostCommitIntents = intents };
    }

    /// <exception cref="SpecialistTurnException">A final-envelope failure retaining its cause and checkpoint/workflow identity.</exception>
    public async ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        try { await ValidateFinalCoreAsync(commit, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { _expected.TryRemove(commit.CommitId, out _); throw; }
        catch (Exception exception) { throw SpecialistSettlementBoundary.Failure(commit.CommitId, commit.WorkflowExecutionId, exception); }
    }

    private ValueTask ValidateFinalCoreAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_expected.TryRemove(commit.CommitId, out var evidence)) throw SpecialistSettlementBoundary.Invalid();
        EnsureUnchanged(commit, evidence);
        var expected = evidence.Plan.Intents;
        var actual = commit.PostCommitIntents.Where(item => item.Kind is SpecialistSettlementBoundary.AppliedKind or SpecialistSettlementBoundary.CancelKind).ToArray();
        if (actual.Length != expected.Count || expected.Any(intent => actual.Count(item => item.IsEquivalentTo(intent)) != 1))
            throw SpecialistSettlementBoundary.Invalid();
        return ValueTask.CompletedTask;
    }

    private static bool WholeCancelled(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.WorkflowExecution is { Operation: RuntimeStateChangeOperation.Upsert, State.Status: WorkflowExecutionStatus.Cancelled };

    private static void EnsureUnchanged(RuntimeCheckpointCommit commit, ExpectedIntents evidence)
    {
        if (commit.WorkflowExecutionId != evidence.WorkflowId) throw SpecialistSettlementBoundary.Invalid();
        // An artifact with no specialist facade cannot acquire a specialist responsibility from
        // later control/root projections. Its ordinary state belongs to those other contributors.
        if (evidence.Plan.FacadeNodes is { Count: 0 }) return;
        if (WholeCancelled(commit) != evidence.WholeCancelled ||
            evidence.Plan.FacadeNodes is not null && commit.StateChanges.WorkflowExecution != evidence.Workflow)
            throw SpecialistSettlementBoundary.Invalid();
        var final = RelevantActivities(commit, evidence.Plan.FacadeNodes).ToDictionary(change => change.Id, StringComparer.Ordinal);
        foreach (var before in evidence.Activities)
            if (!final.Remove(before.Id, out var after) || before != after) throw SpecialistSettlementBoundary.Invalid();
        // The later control producer can add a previously scanned root to the change set. It
        // may decorate its control fields, but cannot change the native cancellation identity.
        foreach (var added in final.Values)
            if (!evidence.WholeCancelled || added.Operation != RuntimeStateChangeOperation.Upsert ||
                evidence.Plan.ScannedFacades?.TryGetValue(added.Id, out var scanned) != true ||
                added.State != WithoutControl(scanned)) throw SpecialistSettlementBoundary.Invalid();
    }

    private static ActivityExecutionState WithoutControl(ActivityExecutionState state) =>
        state with { ProducedControl = null, ProducedControlSourceCommitId = null };

    private static IEnumerable<NativeChange> RelevantActivities(RuntimeCheckpointCommit commit, IReadOnlySet<string>? facades) =>
        commit.StateChanges.ActivityExecutions.Where(change =>
            (facades is null || facades.Contains(change.State.Execution.ExecutableNodeId)) &&
            (WholeCancelled(commit) || change.Operation == RuntimeStateChangeOperation.Upsert &&
                (change.State.Status == ActivityExecutionStatus.Cancelled ||
                 commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted && change.State.Status == ActivityExecutionStatus.Completed && change.State.ContractIdentity is not null)))
            // These fields belong to the later control-producer phase, not specialist settlement.
            .Select(change => new NativeChange(change.StateId, change.Operation, WithoutControl(change.State)));

    private async ValueTask<IReadOnlyList<RuntimePostCommitIntent>> ExpectedOnceAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_expected.TryGetValue(commit.CommitId, out var prior))
        {
            EnsureUnchanged(commit, prior);
            return prior.Plan.Intents;
        }
        var plan = await ExpectedAsync(commit, cancellationToken);
        var evidence = _expected.GetOrAdd(commit.CommitId, new ExpectedIntents(commit.WorkflowExecutionId, commit.StateChanges.WorkflowExecution,
            WholeCancelled(commit), RelevantActivities(commit, plan.FacadeNodes).ToArray(), plan));
        EnsureUnchanged(commit, evidence);
        return evidence.Plan.Intents;
    }

    private async ValueTask<ExpectedPlan> ExpectedAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        var changed = commit.StateChanges.ActivityExecutions.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert)
            .ToDictionary(change => change.StateId, change => change.State, StringComparer.Ordinal);
        var completed = commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted ?
            changed.Values.Where(state => state.Status == ActivityExecutionStatus.Completed && state.ContractIdentity is not null).ToArray() : [];
        var cancelled = changed.Values.Where(state => state.Status == ActivityExecutionStatus.Cancelled).ToList();
        var wholeCancelled = commit.StateChanges.WorkflowExecution is { Operation: RuntimeStateChangeOperation.Upsert, State.Status: WorkflowExecutionStatus.Cancelled };
        if (completed.Length == 0 && cancelled.Count == 0 && !wholeCancelled) return new([], null);

        // Native checkpoint metadata (or its workflow change) already carries the exact pin.
        // Reuse the drain's executable reader before touching workflow/activity stores. Ordinary
        // workflows have no settlement responsibility, including whole-workflow cancellation.
        var pin = commit.StateChanges.WorkflowExecution?.State.PinnedExecutable;
        var artifactId = pin?.ArtifactId ?? commit.Checkpoint.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ExecutableArtifactId);
        var artifactHash = pin?.ArtifactHash ?? commit.Checkpoint.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ExecutableArtifactHash);
        (WorkflowExecutionState Workflow, WorkflowExecutable Executable)? loaded = null;
        WorkflowExecutable executable;
        if (artifactId is not null && artifactHash is not null)
        {
            executable = await executables.FindAsync(artifactId, cancellationToken) ?? throw SpecialistSettlementBoundary.Invalid();
            if (executable.Identity.ArtifactHash != artifactHash) throw SpecialistSettlementBoundary.Invalid();
        }
        else
        {
            // Compatibility for contributions that do not carry the native checkpoint pin.
            loaded = await boundary.WorkflowAsync(commit.WorkflowExecutionId, cancellationToken);
            executable = loaded.Value.Executable;
        }
        var facadeNodes = executable.NodesById.Values.Where(node => node.ActivityContract?.Activation.Capability == SpecialistRuntimeIds.FacadeCapability)
            .Select(node => node.ExecutableNodeId).ToHashSet(StringComparer.Ordinal);
        if (facadeNodes.Count == 0) return new([], facadeNodes);
        if (!wholeCancelled && !completed.Concat(cancelled).Any(parent =>
                executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var node) &&
                node.ActivityContract?.Activation.Capability == SpecialistRuntimeIds.FacadeCapability)) return new([], facadeNodes);

        var scannedFacades = new Dictionary<string, ActivityExecutionState>(StringComparer.Ordinal);
        if (wholeCancelled)
        {
            string? continuation = null;
            do
            {
                var page = await activities.ListPageAsync(new(commit.WorkflowExecutionId, continuationToken: continuation), cancellationToken);
                foreach (var state in page.Items.Where(state => !changed.ContainsKey(state.InvocationId) && facadeNodes.Contains(state.Execution.ExecutableNodeId)))
                    if (!scannedFacades.TryAdd(state.InvocationId, state)) throw SpecialistSettlementBoundary.Invalid();
                cancelled.AddRange(page.Items.Where(state => !changed.ContainsKey(state.InvocationId) &&
                    state.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Superseded)));
                if (page.NextContinuationToken is not null && page.NextContinuationToken == continuation) throw SpecialistSettlementBoundary.Invalid();
                continuation = page.NextContinuationToken;
            } while (continuation is not null);
        }
        if (completed.Length == 0 && cancelled.Count == 0) return new([], facadeNodes, scannedFacades);
        var proof = loaded ?? await boundary.WorkflowAsync(commit.WorkflowExecutionId, cancellationToken);
        var workflow = proof.Workflow;
        if (proof.Executable.Identity != executable.Identity) throw SpecialistSettlementBoundary.Invalid();
        if (wholeCancelled)
        {
            var terminal = commit.StateChanges.WorkflowExecution!.State;
            if (terminal.PinnedExecutable != workflow.PinnedExecutable || terminal.WorkflowExecutionId != workflow.WorkflowExecutionId || terminal.TenantId != workflow.TenantId || terminal.Partition != workflow.Partition)
                throw SpecialistSettlementBoundary.Invalid();
            workflow = terminal;
        }
        var intents = new List<RuntimePostCommitIntent>();
        foreach (var parent in completed)
        {
            if (!executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var node)) throw SpecialistSettlementBoundary.Invalid();
            if (node.ActivityContract?.Activation.Capability != SpecialistRuntimeIds.FacadeCapability) continue;
            intents.Add(SpecialistSettlementBoundary.Intent(await boundary.AppliedAsync(workflow, parent, node, cancellationToken)));
        }
        foreach (var parent in cancelled)
        {
            if (!executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var node)) throw SpecialistSettlementBoundary.Invalid();
            if (node.ActivityContract?.Activation.Capability != SpecialistRuntimeIds.FacadeCapability) continue;
            intents.Add(SpecialistSettlementBoundary.Intent(await boundary.CancelledAsync(workflow, parent, node, cancellationToken)));
        }
        return new(intents, facadeNodes, scannedFacades);
    }
}
