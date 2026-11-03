using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Contracts;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Native admission and terminal delivery participate in the existing native checkpoint.</summary>
internal sealed class BpmnNativeWorkCheckpointEnricher(BpmnNativeWorkBoundary boundary,
    IRuntimeCoalescingSessionAccessor? sessions = null) : IRuntimeCheckpointCommitEnricher
{
    internal const string RetiredCheckpoint = RuntimeCheckpointNames.NativeModelWorkStartRetired;
    private const string RetirementMetadata = "bpmn.nativeWorkStartRetirement";
    // Capture every child-cancellation delivery contributed by native activity integrations, including older handlers.
    // ValidateFinalAsync refuses any later contributor that changes that inventory.
    public int Order => int.MaxValue;
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        if (commit.StateChanges.WorkflowExecution?.State is not { ModelWorkOrigin: { } origin } worker) return commit;
        var prior = await boundary.FindWorkerAsync(origin, cancellationToken);
        if (commit.Checkpoint.Name == RuntimeCheckpointNames.WorkflowStarted && prior is null)
        {
            if (await boundary.FindCancellationAsync(origin, cancellationToken) is { } cancelled)
            {
                if (sessions?.Current is { HasBufferedChanges: true } session && session.AppliesTo(commit.WorkflowExecutionId))
                    throw BpmnNativeWorkBoundary.Unavailable("Native start retirement cannot fold buffered execution changes.");
                return Retire(commit, cancelled.Cancellation, cancelled.Source.Intent.RecordedAt);
            }
            commit = commit with { ExpectedModelExecution = await boundary.RequireLiveAsync(origin, cancellationToken) };
        }
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
        if (commit.Checkpoint.Name == RetiredCheckpoint)
        {
            if (!commit.Metadata.TryGetValue(RetirementMetadata, out var json))
                throw new BpmnModelValidationException("Native start retirement lost its exact cancellation address.");
            var cancellation = JsonSerializer.Deserialize<BpmnNativeWorkCancellation>(json, BpmnNativeWorkBoundary.Web)
                ?? throw new BpmnModelValidationException("Native start retirement is missing.");
            var authorized = await boundary.FindCancellationAsync(cancellation.Origin, cancellationToken)
                ?? throw new BpmnModelValidationException("Native start retirement has no committed model cancellation.");
            if (cancellation.Origin.WorkerExecutionId != commit.WorkflowExecutionId ||
                await boundary.FindWorkerAsync(cancellation.Origin, cancellationToken) is not null ||
                commit.RetainSchedulerWork || commit.ModelState is not null || commit.ExpectedModelExecution is not null ||
                commit.ExpectedModelParentWait is not null || commit.ExpectedBookmarkResume is not null ||
                RuntimeCheckpointCommitFingerprint.Compute(commit) != RuntimeCheckpointCommitFingerprint.Compute(
                    Retire(commit, authorized.Cancellation, authorized.Source.Intent.RecordedAt)))
                throw new BpmnModelValidationException("Native start retirement cannot create or mutate native execution state or root work.");
            return;
        }
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

    private static RuntimeCheckpointCommit Retire(RuntimeCheckpointCommit commit, BpmnNativeWorkCancellation cancellation, DateTimeOffset at)
    {
        var metadata = new Dictionary<string, string>
        {
            [RetirementMetadata] = JsonSerializer.Serialize(cancellation with { Settled = false, NotAdmitted = false }, BpmnNativeWorkBoundary.Web),
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory
        };
        // Deterministic on replay, including after model settlement and an ownership change. The existing
        // checkpoint transaction still fences this worker mailbox and consumes its original claimed work.
        return new(commit.CommitId, new(commit.Checkpoint.CheckpointId, RetiredCheckpoint, commit.WorkflowExecutionId, at, [], metadata),
            new(null, null, [], [], [], [], []), [], metadata);
    }

    private static RuntimePostCommitIntent ResultIntent(RuntimeCheckpointCommit commit, BpmnNativeWorkResult result)
    {
        var intentId = result.SourceOutboxItemId[(commit.CommitId.Length + 1)..];
        return new(intentId, result.Origin.WorkerExecutionId, result.Origin.ResultIntentKind, commit.Checkpoint.OccurredAt,
            result.Completion?.InvocationId, intentId, JsonSerializer.SerializeToElement(result, BpmnNativeWorkBoundary.Web));
    }
}
