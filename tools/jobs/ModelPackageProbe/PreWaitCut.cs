using Elsa.Jobs.Workflows.Runtime.Activities;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

internal sealed record PreWaitCutOptions(string RootExecutionId, string WorkerExecutionId, string MarkerFile);

// Qualification observer around the selected public checkpoint contract. It forwards every capability and
// reader qualification and returns the original transaction result. It never creates state or cancellation proof.
internal sealed class PreWaitCutStore(IRuntimeCheckpointCommitStore inner, PreWaitCut gate,
    IWorkflowExecutionStateStore workflows) : IRuntimeCheckpointCommitStore
{
    public bool SupportsModelCheckpoint => inner.SupportsModelCheckpoint;
    public bool SupportsModelParentAdmission => inner.SupportsModelParentAdmission;
    public bool SupportsModelAdmission => inner.SupportsModelAdmission;
    public bool SupportsExactBookmarkResume => inner.SupportsExactBookmarkResume;
    public bool CanCommitModelState(IRuntimeModelExecutionReader reader) => inner.CanCommitModelState(reader);
    public bool CanCommitModelAdmission(IRuntimeModelExecutionReader reader, IWorkflowExecutionStateStore executions,
        IActivityExecutionStateStore activities, IRuntimePostCommitOutboxStore outbox) => inner.CanCommitModelAdmission(reader, executions, activities, outbox);
    public bool CanCommitExactBookmarkResume(IBookmarkStateStore bookmarks, IWorkflowExecutionStateStore executions,
        IActivityExecutionStateStore activities) => inner.CanCommitExactBookmarkResume(bookmarks, executions, activities);

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        var result = await inner.CommitAsync(commit, decision, cancellationToken);
        await gate.AfterCommittedAsync(commit, decision, result, workflows, cancellationToken);
        return result;
    }
}

internal sealed class PreWaitCut(PreWaitCutOptions options)
{
    private int _entered;
    internal static void Register(IServiceCollection services, PreWaitCutOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootExecutionId) || string.IsNullOrWhiteSpace(options.WorkerExecutionId) || File.Exists(options.MarkerFile))
            throw new InvalidDataException("The owned pre-wait cut needs exact root/worker identities and a fresh marker.");
        var descriptor = services.Single(item => item.ServiceType == typeof(IRuntimeCheckpointCommitStore));
        if (descriptor.ImplementationFactory is not { } factory || descriptor.Lifetime != ServiceLifetime.Scoped)
            throw new InvalidDataException("The owned pre-wait observer requires the selected scoped checkpoint factory.");
        services.AddSingleton(new PreWaitCut(options));
        services.Remove(descriptor);
        services.AddScoped<IRuntimeCheckpointCommitStore>(provider => new PreWaitCutStore((IRuntimeCheckpointCommitStore)factory(provider),
            provider.GetRequiredService<PreWaitCut>(), provider.GetRequiredService<IWorkflowExecutionStateStore>()));
    }

    internal async ValueTask AfterCommittedAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision,
        RuntimeCheckpointCommitStoreResult result, IWorkflowExecutionStateStore workflows, CancellationToken cancellationToken)
    {
        if (commit.WorkflowExecutionId != options.WorkerExecutionId || commit.Checkpoint.Name != RuntimeCheckpointNames.ActivityAttemptClaimed) return;
        var changes = commit.StateChanges.ActivityExecutions.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert &&
            change.State.Execution.ActivityType == RunJob.ActivityType && change.State.ParentActivityExecutionId is null).ToArray();
        if (changes.Length != 1) throw new InvalidDataException("The exact closed RunJob initial claim was not observed.");
        var activity = changes[0].State;
        if (decision.Mode != RuntimeCheckpointPersistenceMode.Immediate || activity.Status != ActivityExecutionStatus.Running ||
            activity.PrivateState is not null || activity.Completion is not null || activity.BookmarkIds.Count != 0 ||
            activity.TriggerRegistrations?.Count > 0 || activity.TriggerDeliveries?.Count > 0 ||
            activity.Attempts is not { Count: 1 } attempts || attempts.Single() is not { Reason: ActivityAttemptReason.Initial, EndedAt: null } attempt ||
            activity.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityAttemptActivationClaim) != attempt.AttemptId)
            throw new InvalidDataException("The observed claim is not a committed initial attempt before its first saved wait.");
        var worker = await workflows.FindAsync(options.WorkerExecutionId, cancellationToken) ?? throw new InvalidDataException("The real admitted worker is missing.");
        if (worker.ModelWorkOrigin is not { } origin || origin.ExecutionId != options.RootExecutionId || origin.WorkerExecutionId != options.WorkerExecutionId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(origin.WorkerExecutable, worker.PinnedExecutable))
            throw new InvalidDataException("The exact admitted native helper has no matching genuine model origin.");
        if (Interlocked.CompareExchange(ref _entered, 1, 0) != 0) throw new InvalidDataException("The owned pre-wait cut was entered more than once.");
        await Probe.WriteAsync(options.MarkerFile, new { state = "CommittedBeforeBody", occurrence = 1, processId = Environment.ProcessId,
            commit.CommitId, checkpointId = commit.Checkpoint.CheckpointId, checkpointName = commit.Checkpoint.Name,
            rootExecutionId = options.RootExecutionId, workerExecutionId = options.WorkerExecutionId,
            worker.Partition, worker.TenantId, worker.PinnedExecutable, worker.ModelWorkOrigin, activity,
            initialAttemptId = attempt.AttemptId, activationClaimMetadataKey = RuntimeMetadataKeys.ActivityAttemptActivationClaim,
            originalTransactionResult = result, bookmarkCount = 0 });
        try
        {
            // The controller must independently inspect this committed cut and kill this exact owned process.
            // Returning or timing out is never promoted into proof of a pre-body cancellation.
            await Task.Delay(TimeSpan.FromSeconds(90), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        await Probe.WriteAsync(options.MarkerFile + ".timed-out.json", new { state = "TimedOut", processId = Environment.ProcessId, commit.CommitId });
        throw new TimeoutException("The owned pre-wait cut timed out without the required process kill.");
    }
}
