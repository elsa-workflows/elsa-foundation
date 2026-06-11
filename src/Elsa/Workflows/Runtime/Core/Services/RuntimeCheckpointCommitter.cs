using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeCheckpointCommitter
{
    private readonly IRuntimeCheckpointPersistencePolicy _persistencePolicy;
    private readonly IRuntimeCheckpointWriter _checkpointWriter;
    private readonly IRuntimePostCommitIntentDispatcher _postCommitIntentDispatcher;
    private readonly IRuntimePostCommitOutboxStore? _postCommitOutboxStore;
    private readonly TimeProvider _timeProvider;

    public RuntimeCheckpointCommitter(
        IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IRuntimeCheckpointWriter checkpointWriter,
        IRuntimePostCommitIntentDispatcher postCommitIntentDispatcher)
        : this(persistencePolicy, checkpointWriter, postCommitIntentDispatcher, null)
    {
    }

    public RuntimeCheckpointCommitter(
        IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IRuntimeCheckpointWriter checkpointWriter,
        IRuntimePostCommitIntentDispatcher postCommitIntentDispatcher,
        IRuntimePostCommitOutboxStore? postCommitOutboxStore)
        : this(persistencePolicy, checkpointWriter, postCommitIntentDispatcher, postCommitOutboxStore, TimeProvider.System)
    {
    }

    public RuntimeCheckpointCommitter(
        IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IRuntimeCheckpointWriter checkpointWriter,
        IRuntimePostCommitIntentDispatcher postCommitIntentDispatcher,
        IRuntimePostCommitOutboxStore? postCommitOutboxStore,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(persistencePolicy);
        ArgumentNullException.ThrowIfNull(checkpointWriter);
        ArgumentNullException.ThrowIfNull(postCommitIntentDispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _persistencePolicy = persistencePolicy;
        _checkpointWriter = checkpointWriter;
        _postCommitIntentDispatcher = postCommitIntentDispatcher;
        _postCommitOutboxStore = postCommitOutboxStore;
        _timeProvider = timeProvider;
    }

    public async ValueTask<RuntimeCheckpointPersistenceDecision> CommitAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        var decision = await _persistencePolicy.DecideAsync(commit.Checkpoint, cancellationToken);

        if (decision.Mode == RuntimeCheckpointPersistenceMode.Skip)
            return decision;

        await _checkpointWriter.WriteAsync(commit, decision, cancellationToken);

        var postCommitIntents = commit.PostCommitIntents.ToArray();
        var dispatchedIntentIds = new List<string>();

        await SavePendingOutboxItemsAsync(commit, postCommitIntents, cancellationToken);

        for (var index = 0; index < postCommitIntents.Length; index++)
        {
            var intent = postCommitIntents[index];

            try
            {
                await _postCommitIntentDispatcher.DispatchAsync(intent, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var undispatchedIntentIds = postCommitIntents
                    .Skip(index + 1)
                    .Select(undispatchedIntent => undispatchedIntent.IntentId)
                    .ToArray();
                var deliveryResultRecordingException = await TryRecordFailedOutboxDeliveryResultAsync(commit, intent, exception, cancellationToken);

                throw new RuntimePostCommitIntentDispatchException(
                    commit.CommitId,
                    intent.IntentId,
                    dispatchedIntentIds.ToArray(),
                    undispatchedIntentIds,
                    exception,
                    deliveryResultRecordingException);
            }

            dispatchedIntentIds.Add(intent.IntentId);
            await RecordOutboxDeliveryResultAsync(commit, intent, RuntimePostCommitOutboxStatus.Delivered, null, cancellationToken);
        }

        return decision;
    }

    private async ValueTask SavePendingOutboxItemsAsync(
        RuntimeCheckpointCommit commit,
        IReadOnlyCollection<RuntimePostCommitIntent> postCommitIntents,
        CancellationToken cancellationToken)
    {
        if (_postCommitOutboxStore is null)
            return;

        foreach (var intent in postCommitIntents)
        {
            await _postCommitOutboxStore.SavePendingAsync(new RuntimePostCommitOutboxItem(
                outboxItemId: NewOutboxItemId(commit, intent),
                intent: intent,
                status: RuntimePostCommitOutboxStatus.Pending,
                recordedAt: intent.RecordedAt,
                availableAt: commit.Checkpoint.OccurredAt,
                retryPolicy: RuntimePostCommitRetryPolicy.None,
                metadata: commit.Metadata),
                cancellationToken);
        }
    }

    private async ValueTask RecordOutboxDeliveryResultAsync(
        RuntimeCheckpointCommit commit,
        RuntimePostCommitIntent intent,
        RuntimePostCommitOutboxStatus status,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        if (_postCommitOutboxStore is null)
            return;

        await _postCommitOutboxStore.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: NewOutboxItemId(commit, intent),
            status: status,
            recordedAt: _timeProvider.GetUtcNow(),
            failureMessage: failureMessage),
            cancellationToken);
    }

    private async ValueTask<Exception?> TryRecordFailedOutboxDeliveryResultAsync(
        RuntimeCheckpointCommit commit,
        RuntimePostCommitIntent intent,
        Exception dispatchException,
        CancellationToken cancellationToken)
    {
        try
        {
            await RecordOutboxDeliveryResultAsync(commit, intent, RuntimePostCommitOutboxStatus.FailedFinal, FailureMessage(dispatchException), cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static string NewOutboxItemId(RuntimeCheckpointCommit commit, RuntimePostCommitIntent intent) =>
        $"{commit.CommitId}:{intent.IntentId}";

    private static string FailureMessage(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
}
