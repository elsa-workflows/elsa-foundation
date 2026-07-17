using System.Security.Cryptography;
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
        object stateChanges = commit.StateChanges.WorkflowDispatchCancellations.Count > 0
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

        var payload = new
        {
            commit.CommitId,
            commit.Checkpoint,
            StateChanges = stateChanges,
            PostCommitIntents = commit.PostCommitIntents.OrderBy(x => x.IntentId, StringComparer.Ordinal).ToArray(),
            commit.Metadata
        };

        var element = JsonSerializer.SerializeToElement(payload, SerializerOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(element, writer);

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static RuntimeStateChange<TState>[] Order<TState>(IReadOnlyCollection<RuntimeStateChange<TState>> changes) =>
        changes
            .OrderBy(x => x.StateId, StringComparer.Ordinal)
            .ThenBy(x => x.Operation)
            .ToArray();

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
