using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Computes the canonical replay identity of a provider-facing checkpoint bundle.</summary>
public static class RuntimeCheckpointCommitFingerprint
{
    /// <summary>
    /// Identifies an immutable persisted intent for a delivery-state extension. This is separate from
    /// checkpoint replay identity and grants no authority without checking the exact persisted source.
    /// Opaque string fields retain their UTF-16 code units; in-process conduits are deliberately excluded.
    /// </summary>
    public static string ComputeIntent(RuntimePostCommitIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        using var frame = new MemoryStream();
        Append(intent.HistoryCausation is null ? "elsa.post-commit.intent.v1" : "elsa.post-commit.intent.v2");
        Append(intent.IntentId);
        Append(intent.WorkflowExecutionId);
        Append(intent.Kind);
        Append(intent.RecordedAt.UtcTicks.ToString(CultureInfo.InvariantCulture));
        Append(intent.ActivityExecutionId);
        Append(intent.IdempotencyKey);
        Append(intent.DependsOnWaitRegistrationId);
        Append(intent.WaitFailurePolicy is { } policy ? ((int)policy).ToString(CultureInfo.InvariantCulture) : null);
        Append(intent.NotBefore?.UtcTicks.ToString(CultureInfo.InvariantCulture));
        if (intent.HistoryCausation is { } origin)
        {
            Append(origin.HistoryEventId); Append(origin.WorkflowExecutionId); Append(origin.DeliveryId);
            Append(origin.CommandId); Append(origin.SchedulerWorkItemId);
        }
        if (intent.Payload is { } payload)
        {
            AppendBytes(Elsa.Serialization.Core.JsonHashInput.CanonicalBytes(payload));
        }
        else AppendBytes(null);
        Append(intent.Metadata.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var pair in intent.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Append(pair.Key);
            Append(pair.Value);
        }
        return Elsa.Serialization.Core.JsonHashInput.HashFramed(frame.ToArray());

        void Append(string? value)
        {
            if (value is null) { AppendBytes(null); return; }
            var bytes = new byte[checked(value.Length * sizeof(char))];
            for (var index = 0; index < value.Length; index++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * sizeof(char), sizeof(char)), value[index]);
            AppendBytes(bytes);
        }

        void AppendBytes(byte[]? bytes)
        {
            Span<byte> header = stackalloc byte[5];
            header[0] = bytes is null ? (byte)0 : (byte)1;
            BinaryPrimitives.WriteInt32BigEndian(header[1..], bytes?.Length ?? 0);
            frame.Write(header);
            if (bytes is not null) frame.Write(bytes);
        }
    }

    /// <summary>Canonical identity of a model input, independent of transport timestamps and queue envelopes.</summary>
    public static string ComputeModelInput(RuntimeModelExecutionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var payload = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { Format = "elsa.model.input.v1", Command = command }, RuntimeJsonHashProjection.For);
        return Elsa.Serialization.Core.JsonHashInput.Hash(payload);
    }

    public static string Compute(RuntimeCheckpointCommit commit) => ComputeCore(commit, exactHistorySource: false);

    /// <summary>Canonical exact source agreement for history capture, separate from durable replay identity.</summary>
    public static string ComputeHistorySource(RuntimeCheckpointCommit commit) => ComputeCore(commit, exactHistorySource: true);

    private static string ComputeCore(RuntimeCheckpointCommit commit, bool exactHistorySource)
    {
        ArgumentNullException.ThrowIfNull(commit);

        // Preserve every owner-defined logical field, including cleanup-only state changes. S-9 moves its encoding once before
        // the first stable release; no old-hash verification path remains.
        var stateChanges = new
        {
            commit.StateChanges.WorkflowExecution,
            commit.StateChanges.Scheduler,
            ActivityExecutions = Order(commit.StateChanges.ActivityExecutions),
            ActivityExecutionInspections = Order(commit.StateChanges.ActivityExecutionInspections),
            Bookmarks = Order(commit.StateChanges.Bookmarks),
            DurableValues = Order(commit.StateChanges.DurableValues),
            Incidents = Order(commit.StateChanges.Incidents),
            Operational = Order(commit.StateChanges.Operational),
            WorkflowDispatches = Order(commit.StateChanges.WorkflowDispatches),
            WorkflowDispatchCancellations = commit.StateChanges.WorkflowDispatchCancellations
                .OrderBy(request => request.DispatchId, StringComparer.Ordinal)
                .ToArray(),
            ActivityScopeCleanups = commit.StateChanges.ActivityScopeCleanups
                .OrderBy(request => request.WorkflowExecutionId, StringComparer.Ordinal)
                .ThenBy(request => request.ExecutionScopeId, StringComparer.Ordinal)
                .ToArray(),
            ConsumedSchedulerWorkItems = commit.StateChanges.ConsumedSchedulerWorkItems
                .OrderBy(item => item.WorkItemId, StringComparer.Ordinal)
                .ToArray(),
            commit.StateChanges.AlterationJobTerminalChange,
            BackgroundActivityWork = new
            {
                Admissions = commit.StateChanges.BackgroundActivityWork.Admissions
                    .OrderBy(request => request.WorkId, StringComparer.Ordinal).ToArray(),
                Acceptances = commit.StateChanges.BackgroundActivityWork.Acceptances
                    .OrderBy(request => request.WorkId, StringComparer.Ordinal).ToArray(),
                Cancellations = commit.StateChanges.BackgroundActivityWork.Cancellations
                    .OrderBy(request => request.WorkflowExecutionId, StringComparer.Ordinal)
                    .ThenBy(request => System.Text.Encoding.UTF8.GetString(Elsa.Serialization.Core.JsonHashInput.CanonicalBytes(Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(request.ActivityExecutionIds, RuntimeJsonHashProjection.For))), StringComparer.Ordinal)
                    .ToArray()
            },
            PostCommitOutbox = Order(commit.StateChanges.PostCommitOutbox)
        };

        var payload = new
        {
            commit.CommitId,
            commit.Checkpoint,
            StateChanges = stateChanges,
            PostCommitIntents = commit.PostCommitIntents.OrderBy(x => x.IntentId, StringComparer.Ordinal).ToArray(),
            commit.Metadata
        };

        JsonElement element;
        if (commit.ExpectedBookmarkResume is not null)
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { ExactBookmarkResumeVersion = 1, Payload = payload, commit.ExpectedBookmarkResume }, RuntimeJsonHashProjection.For);
        else if (commit.StateChanges.ActivityExecutionInspections.Any(change => change.State.CheckpointValueCapture is not null))
            // This domain tag identifies commits that carry checkpoint value capture.
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { CheckpointValueCaptureVersion = 1, Payload = payload }, RuntimeJsonHashProjection.For);
        else
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(payload, RuntimeJsonHashProjection.For);
        if (commit.IngressWaitOrigin is not null)
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { IngressWaitOriginVersion = 1, Payload = element, commit.IngressWaitOrigin }, RuntimeJsonHashProjection.For);
        if (commit.PostCommitIntents.Any(intent => intent.HistoryCausation is not null) ||
            commit.StateChanges.PostCommitOutbox.Any(change => change.State?.Intent?.HistoryCausation is not null))
        {
            // Bind the causation domain and its ordered intent/outbox inventory explicitly.
            // The shared opaque projection preserves the original UTF-16 identities throughout.
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { RuntimePostCommitHistoryCausationVersion = 1,
                Payload = element,
                Intents = commit.PostCommitIntents.Where(intent => intent.HistoryCausation is not null)
                    .OrderBy(intent => intent.IntentId, StringComparer.Ordinal).ToArray(),
                Outbox = commit.StateChanges.PostCommitOutbox.Where(change => change.State?.Intent?.HistoryCausation is not null)
                    .OrderBy(change => change.StateId, StringComparer.Ordinal).ToArray() }, RuntimeJsonHashProjection.For);
        }
        if (commit.Checkpoint.HistoryCausation is { } cause)
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { RuntimeHistoryCausationVersion = 1, Payload = element, Cause = cause }, RuntimeJsonHashProjection.For);
        if (commit.HistorySourceFacts is { Count: > 0 } facts)
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { RuntimeHistorySourceVersion = 1, Payload = element, SourceFacts = facts }, RuntimeJsonHashProjection.For);
        // History-owned cleanup adds its own domain tag and exact ordered native cleanup inventory.
        if (commit.StateChanges.ActivityScopeCleanups.Count > 0 &&
            (commit.HistorySourceFacts is not null || commit.HistoryBatches is { Count: > 0 } ||
             commit.StateChanges.ActivityScopeCleanups.Any(request => request.HistoryBookmarks is not null)))
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new
            {
                RuntimeHistoryCleanupSourceVersion = 1, Payload = element,
                Cleanups = commit.StateChanges.ActivityScopeCleanups.OrderBy(request => request.WorkflowExecutionId, StringComparer.Ordinal)
                    .ThenBy(request => request.ExecutionScopeId, StringComparer.Ordinal).ToArray()
            }, RuntimeJsonHashProjection.For);
        if (commit.HistoryBatches is { Count: > 0 } history)
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { RuntimeHistoryVersion = 1, Payload = element, HistoryBatches = history }, RuntimeJsonHashProjection.For);
        if (commit.ModelState is not null)
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { ModelStateVersion = 1, Payload = element, commit.ModelState }, RuntimeJsonHashProjection.For);
        if (commit.ExpectedInitialStartFault is not null)
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { InitialStartFaultVersion = 1, Payload = element, commit.ExpectedInitialStartFault }, RuntimeJsonHashProjection.For);
        // ExpectedModelExecution, like ExpectedFence, is an attempt's concurrency condition. A retry can
        // validate a newer snapshot with the same live work. Its immutable semantic address is retained
        // in WorkflowExecutionState.ModelWorkOrigin, which already participates in the decision identity.
        // ExpectedModelParentWait is likewise only a refreshed physical condition. The model-owned state
        // retains its immutable native invocation/result address and participates in this fingerprint.
        if (exactHistorySource)
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { HistoryCaptureSourceVersion = 1, Payload = element, commit.RetainSchedulerWork }, RuntimeJsonHashProjection.For);
        return Elsa.Serialization.Core.JsonHashInput.Hash(element);
    }

    private static RuntimeStateChange<TState>[] Order<TState>(IReadOnlyCollection<RuntimeStateChange<TState>> changes) =>
        changes
            .OrderBy(x => x.StateId, StringComparer.Ordinal)
            .ThenBy(x => x.Operation)
            .ToArray();

}
