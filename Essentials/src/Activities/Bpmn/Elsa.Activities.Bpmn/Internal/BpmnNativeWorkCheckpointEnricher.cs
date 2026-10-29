using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Native admission and terminal delivery participate in the existing native checkpoint.</summary>
internal sealed class BpmnNativeWorkCheckpointEnricher(BpmnNativeWorkBoundary boundary) : IRuntimeCheckpointCommitEnricher
{
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        if (commit.StateChanges.WorkflowExecution?.State is not { ModelWorkOrigin: { } origin } worker) return commit;
        var prior = await boundary.FindWorkerAsync(origin, cancellationToken);
        if (commit.Checkpoint.Name == RuntimeCheckpointNames.WorkflowStarted && prior is null)
            commit = commit with { ExpectedModelExecution = await boundary.RequireLiveAsync(origin, cancellationToken) };
        if (!worker.Status.IsTerminal() || prior?.Status.IsTerminal() == true) return commit;
        var result = await boundary.CaptureAsync(commit, worker, cancellationToken);
        var intent = ResultIntent(commit, result);
        var existing = commit.PostCommitIntents.SingleOrDefault(item => item.IntentId == intent.IntentId);
        if (existing is not null && !existing.IsEquivalentTo(intent))
            throw new BpmnModelValidationException("The native worker terminal result conflicts with its checkpoint responsibility.");
        return existing is null ? commit with { PostCommitIntents = [.. commit.PostCommitIntents, intent] } : commit;
    }

    public async ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        if (commit.StateChanges.WorkflowExecution?.State is not { ModelWorkOrigin: { } origin } worker) return;
        var prior = await boundary.FindWorkerAsync(origin, cancellationToken);
        if (commit.Checkpoint.Name == RuntimeCheckpointNames.WorkflowStarted && prior is null &&
            (commit.ExpectedModelExecution is not { } expected || !origin.MatchesModel(expected)))
            throw new BpmnModelValidationException("Initial native worker admission lost its model decision condition.");
        if (!worker.Status.IsTerminal() || prior?.Status.IsTerminal() == true) return;
        var expectedIntent = ResultIntent(commit, await boundary.CaptureAsync(commit, worker, cancellationToken));
        if (commit.PostCommitIntents.Count(item => item.IsEquivalentTo(expectedIntent)) != 1)
            throw new BpmnModelValidationException("The native worker terminal checkpoint lost its exact typed result delivery.");
    }

    private static RuntimePostCommitIntent ResultIntent(RuntimeCheckpointCommit commit, BpmnNativeWorkResult result)
    {
        var intentId = result.SourceOutboxItemId[(commit.CommitId.Length + 1)..];
        return new(intentId, result.Origin.WorkerExecutionId, result.Origin.ResultIntentKind, commit.Checkpoint.OccurredAt,
            result.Completion?.InvocationId, intentId, JsonSerializer.SerializeToElement(result, BpmnNativeWorkBoundary.Web));
    }
}
