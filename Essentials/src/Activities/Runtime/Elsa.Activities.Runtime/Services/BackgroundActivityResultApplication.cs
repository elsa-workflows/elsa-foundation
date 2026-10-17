using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Scoped, explicitly opened context connecting the native terminal checkpoint to its ready receipt.</summary>
internal sealed class BackgroundActivityResultApplication
{
    public BackgroundActivityWorkItem? Item { get; private set; }

    public IDisposable Begin(BackgroundActivityWorkItem item)
    {
        if (Item is not null)
            throw new InvalidOperationException("Only one background result can be applied in a coordinator scope at a time.");
        Item = item;
        return new ApplicationLease(this);
    }

    private sealed class ApplicationLease(BackgroundActivityResultApplication owner) : IDisposable
    {
        public void Dispose() => owner.Item = null;
    }
}

/// <summary>Folds receipt acceptance and explicit native cancellation into the same fenced checkpoint.</summary>
internal sealed class BackgroundActivityCheckpointEnricher(
    BackgroundActivityResultApplication application,
    IBackgroundActivityWorkStore? store = null) : IRuntimeCheckpointCommitEnricher
{
    public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (store is null)
            return ValueTask.FromResult(commit);
        var changes = commit.StateChanges;
        var background = changes.BackgroundActivityWork;
        var acceptances = background.Acceptances.ToList();
        var cancellations = background.Cancellations.ToList();
        var item = application.Item;
        var appliesResult = item is not null && commit.Checkpoint.WorkflowExecutionId == item.Request.WorkflowExecutionId &&
            changes.ActivityExecutions.Any(change => change.Operation != RuntimeStateChangeOperation.Delete &&
                change.StateId == item.Request.ActivityExecutionId &&
                change.State.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled);
        if (appliesResult)
            acceptances.Add(new BackgroundActivityWorkAcceptance(item!.Request.WorkflowExecutionId, item.Request.WorkId, item.ReceiptDigest!, commit.Checkpoint.OccurredAt));

        if (changes.WorkflowExecution is { Operation: not RuntimeStateChangeOperation.Delete } workflow && workflow.State.Status.IsTerminal())
            cancellations.Add(new BackgroundActivityWorkCancellationRequest(commit.Checkpoint.WorkflowExecutionId));
        else
        {
            var cancelledIds = changes.ActivityExecutions
                .Where(change => change.Operation != RuntimeStateChangeOperation.Delete && change.State.Status == ActivityExecutionStatus.Cancelled &&
                    (!appliesResult || change.StateId != item!.Request.ActivityExecutionId))
                .Select(change => change.StateId).Distinct(StringComparer.Ordinal).ToArray();
            if (cancelledIds.Length > 0)
                cancellations.Add(new BackgroundActivityWorkCancellationRequest(commit.Checkpoint.WorkflowExecutionId, cancelledIds));
        }
        if (acceptances.Count == background.Acceptances.Count && cancellations.Count == background.Cancellations.Count)
            return ValueTask.FromResult(commit);
        var enriched = new BackgroundActivityWorkCheckpointChanges(background.Admissions, acceptances, cancellations);
        return ValueTask.FromResult(commit with { StateChanges = changes.WithBackgroundActivityWork(enriched) });
    }
}
