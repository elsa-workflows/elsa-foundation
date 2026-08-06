using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Computes the canonical replay identity of a provider-facing checkpoint bundle.</summary>
public static class RuntimeCheckpointCommitFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(RuntimeCheckpointCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        // Preserve the exact pre-#676 JSON shape for commits without dispatch state so durable replay markers
        // written by older binaries keep the same fingerprint after upgrade.
        // Consumed scheduler work items only appear on the WU-1 atomic-ack path, which never existed for older markers.
        // Emit a superset shape that also includes them so the pre-existing branches below stay byte-identical (and old
        // durable replay markers keep the same fingerprint) whenever no work item is consumed.
        object stateChanges = commit.StateChanges.ConsumedSchedulerWorkItems.Count > 0 || commit.StateChanges.AlterationJobTerminalChange is not null
            ? new
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
                PostCommitOutbox = Order(commit.StateChanges.PostCommitOutbox)
            }
            : commit.StateChanges.WorkflowDispatchCancellations.Count > 0
            ? new
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
                PostCommitOutbox = Order(commit.StateChanges.PostCommitOutbox)
            }
            : commit.StateChanges.WorkflowDispatches.Count == 0
            ? new
            {
                commit.StateChanges.WorkflowExecution,
                commit.StateChanges.Scheduler,
                ActivityExecutions = Order(commit.StateChanges.ActivityExecutions),
                ActivityExecutionInspections = Order(commit.StateChanges.ActivityExecutionInspections),
                Bookmarks = Order(commit.StateChanges.Bookmarks),
                DurableValues = Order(commit.StateChanges.DurableValues),
                Incidents = Order(commit.StateChanges.Incidents),
                Operational = Order(commit.StateChanges.Operational),
                PostCommitOutbox = Order(commit.StateChanges.PostCommitOutbox)
            }
            : new
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
                PostCommitOutbox = Order(commit.StateChanges.PostCommitOutbox)
            };

        object checkpoint = commit.Checkpoint.Provenance is null
            ? new
            {
                commit.Checkpoint.CheckpointId,
                commit.Checkpoint.Name,
                commit.Checkpoint.WorkflowExecutionId,
                commit.Checkpoint.OccurredAt,
                commit.Checkpoint.ActivityExecutionIds,
                commit.Checkpoint.Metadata
            }
            : commit.Checkpoint;
        var payload = new
        {
            commit.CommitId,
            Checkpoint = checkpoint,
            StateChanges = stateChanges,
            PostCommitIntents = commit.PostCommitIntents.OrderBy(x => x.IntentId, StringComparer.Ordinal).ToArray(),
            commit.Metadata
        };
        return ComputeCanonicalHash(payload);
    }

    /// <summary>Computes the canonical identity of the raw proposal before store-assigned provenance is attached.</summary>
    public static string ComputeInput(RuntimeCheckpointCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var rawCheckpoint = commit.Checkpoint with { Provenance = null };
        return Compute(commit with { Checkpoint = rawCheckpoint });
    }

    /// <summary>Computes a canonical identity for one workflow's provider-facing state changes.</summary>
    public static string ComputeStateChanges(string workflowExecutionId, RuntimeCheckpointStateChangeSet stateChanges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(stateChanges);
        // Do not route this through Compute. That method deliberately retains historical marker shapes for backward
        // compatibility, including a legacy branch that predates activity-scope cleanups. Prepared-fold validation
        // instead needs a complete, purpose-specific identity for every provider-facing state-change family.
        return ComputeCanonicalHash(new
        {
            Domain = "elsa.runtime.checkpoint.prepared-fold-state.v1",
            WorkflowExecutionId = workflowExecutionId,
            StateChanges = CreateFullStateChangesPayload(stateChanges)
        });
    }

    /// <summary>Computes the canonical identity of every caller-supplied preparation field.</summary>
    public static string ComputeInput(RuntimeCheckpointPrepareRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Commit);
        ArgumentNullException.ThrowIfNull(request.RequestedExecutionContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationIdentity);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SerializeInputPayload(request))));
    }

    /// <summary>Computes the canonical authority of an atomic prepared-checkpoint fold.</summary>
    public static string ComputePreparedFold(RuntimeCheckpointPreparedFoldRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return ComputeCanonicalHash(new
        {
            Domain = "elsa.runtime.checkpoint.prepared-fold.v1",
            request.WorkflowExecutionId,
            request.MaxWorkflowCheckpointOrder,
            Members = request.Members.Select(member => new
            {
                member.CommitId,
                member.Token.LedgerToken,
                member.WorkflowCheckpointOrder,
                member.Token.CanonicalInputFingerprint,
                member.ExpectedCurrentAuthorityFence,
                member.ExpectedAuthorityRevision,
                member.Disposition,
                CommitFingerprint = member.PreparedCommit is null
                    ? null
                    : Compute(member.PreparedCommit.Commit),
                member.FailureCode,
                member.FailureMessage
            }).ToArray(),
            request.TargetAuthorityFence,
            request.RecoveryRoute,
            DeliveredPostCommitOutboxItemIds = (request.DeliveredPostCommitOutboxItemIds ?? [])
                .Order(StringComparer.Ordinal)
                .ToArray(),
            FoldedStateFingerprint = ComputeStateChanges(request.WorkflowExecutionId, request.FoldedStateChanges)
        });
    }

    /// <summary>Serializes the complete canonical raw preparation input for deterministic recovery.</summary>
    public static string SerializeInputPayload(RuntimeCheckpointPrepareRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Commit);
        ArgumentNullException.ThrowIfNull(request.RequestedExecutionContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationIdentity);

        var rawCommit = request.Commit with
        {
            Checkpoint = request.Commit.Checkpoint with { Provenance = null },
            ExpectedFence = null
        };
        return SerializeCanonical(new
        {
            request.SourceIdentity,
            request.OperationIdentity,
            request.RequestedExecutionContext,
            RawCommit = rawCommit
        });
    }

    private static string ComputeCanonicalHash(object payload)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SerializeCanonical(payload))));

    private static string SerializeCanonical(object payload)
    {
        var element = JsonSerializer.SerializeToElement(payload, SerializerOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(element, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static RuntimeStateChange<TState>[] Order<TState>(IReadOnlyCollection<RuntimeStateChange<TState>> changes) =>
        changes
            .OrderBy(x => x.StateId, StringComparer.Ordinal)
            .ThenBy(x => x.Operation)
            .ToArray();

    private static object CreateFullStateChangesPayload(RuntimeCheckpointStateChangeSet stateChanges) => new
    {
        stateChanges.WorkflowExecution,
        stateChanges.Scheduler,
        ActivityExecutions = Order(stateChanges.ActivityExecutions),
        ActivityExecutionInspections = Order(stateChanges.ActivityExecutionInspections),
        Bookmarks = Order(stateChanges.Bookmarks),
        DurableValues = Order(stateChanges.DurableValues),
        Incidents = Order(stateChanges.Incidents),
        Operational = Order(stateChanges.Operational),
        WorkflowDispatches = Order(stateChanges.WorkflowDispatches),
        WorkflowDispatchCancellations = stateChanges.WorkflowDispatchCancellations
            .OrderBy(request => request.DispatchId, StringComparer.Ordinal)
            .ToArray(),
        ActivityScopeCleanups = stateChanges.ActivityScopeCleanups
            .OrderBy(request => request.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(request => request.ExecutionScopeId, StringComparer.Ordinal)
            .ToArray(),
        ConsumedSchedulerWorkItems = stateChanges.ConsumedSchedulerWorkItems
            .OrderBy(item => item.WorkItemId, StringComparer.Ordinal)
            .ToArray(),
        stateChanges.AlterationJobTerminalChange,
        PostCommitOutbox = Order(stateChanges.PostCommitOutbox)
    };

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                return;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                return;
            default:
                element.WriteTo(writer);
                return;
        }
    }
}
