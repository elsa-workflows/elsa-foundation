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
        Append("elsa.post-commit.intent.v1");
        Append(intent.IntentId);
        Append(intent.WorkflowExecutionId);
        Append(intent.Kind);
        Append(intent.RecordedAt.UtcTicks.ToString(CultureInfo.InvariantCulture));
        Append(intent.ActivityExecutionId);
        Append(intent.IdempotencyKey);
        Append(intent.DependsOnWaitRegistrationId);
        Append(intent.WaitFailurePolicy is { } policy ? ((int)policy).ToString(CultureInfo.InvariantCulture) : null);
        Append(intent.NotBefore?.UtcTicks.ToString(CultureInfo.InvariantCulture));
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

    public static string Compute(RuntimeCheckpointCommit commit)
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

        var element = commit.ExpectedBookmarkResume is null
            ? Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(payload, RuntimeJsonHashProjection.For)
            : Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { ExactBookmarkResumeVersion = 1, Payload = payload, commit.ExpectedBookmarkResume }, RuntimeJsonHashProjection.For);
        if (commit.ModelState is not null)
            element = Elsa.Serialization.Core.JsonHashInput.ProjectOpaqueClr(new { ModelStateVersion = 1, Payload = element, commit.ModelState }, RuntimeJsonHashProjection.For);
        // ExpectedModelExecution, like ExpectedFence, is an attempt's concurrency condition. A retry can
        // validate a newer snapshot with the same live work. Its immutable semantic address is retained
        // in WorkflowExecutionState.ModelWorkOrigin, which already participates in the decision identity.
        // ExpectedModelParentWait is likewise only a refreshed physical condition. The model-owned state
        // retains its immutable native invocation/result address and participates in this fingerprint.
        return Elsa.Serialization.Core.JsonHashInput.Hash(element);
    }

    private static RuntimeStateChange<TState>[] Order<TState>(IReadOnlyCollection<RuntimeStateChange<TState>> changes) =>
        changes
            .OrderBy(x => x.StateId, StringComparer.Ordinal)
            .ThenBy(x => x.Operation)
            .ToArray();

}
