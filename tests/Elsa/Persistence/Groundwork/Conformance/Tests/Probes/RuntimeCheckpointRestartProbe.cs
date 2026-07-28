using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Conformance.Tests.Probes;

/// <summary>
/// Defines the closed payload used by the process-restart checkpoint probe. The child process opens the same
/// physical provider and reads every persisted member of the checkpoint bundle through the public stores.
/// </summary>
internal static class RuntimeCheckpointRestartProbe
{
    public const string ProbeId = "runtime-checkpoint-verify-bundle";

    private static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static GroundworkProcessProbeRequest CreateRequest(RuntimeCheckpointRestartProbePayload payload) =>
        new(
            ProbeId,
            GroundworkProcessProbeOperation.RuntimeCheckpointVerifyBundle,
            payload.CommitId,
            JsonSerializer.Serialize(payload, JsonOptions));

    public static RuntimeCheckpointRestartProbePayload DecodePayload(string json) =>
        JsonSerializer.Deserialize<RuntimeCheckpointRestartProbePayload>(json, JsonOptions)
        ?? throw new InvalidOperationException("The runtime checkpoint restart-probe payload contained JSON null.");

    public static async ValueTask<RuntimeCheckpointRestartProbeObservation> VerifyAsync(
        IDocumentStore store,
        IGroundworkRuntimeDocumentSerializer serializer,
        IPersistenceAccessContextAccessor accessContextAccessor,
        RuntimeCheckpointRestartProbePayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        ArgumentNullException.ThrowIfNull(payload);

        var workflow = await new GroundworkWorkflowExecutionStateStore(store, serializer, accessContextAccessor)
            .FindAsync(payload.WorkflowExecutionId, cancellationToken);
        var activity = await new GroundworkActivityExecutionStateStore(store, serializer)
            .FindAsync(payload.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken);
        var inspection = await new GroundworkActivityExecutionInspectionStore(store, serializer)
            .FindAsync(payload.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken);
        var hierarchy = await store.LoadAsync(
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind,
            DocumentId.Compose(payload.WorkflowExecutionId, payload.ActivityExecutionId),
            cancellationToken);
        var scheduler = await new GroundworkSchedulerStateStore(store, serializer)
            .FindAsync(payload.WorkflowExecutionId, cancellationToken);
        var bookmark = await new GroundworkBookmarkStateStore(store, serializer)
            .FindAsync(payload.WorkflowExecutionId, payload.BookmarkId, cancellationToken);
        var value = await new GroundworkDurableValueStateStore(store, serializer)
            .FindAsync(payload.WorkflowExecutionId, payload.DurableValueId, cancellationToken);
        var incident = await new GroundworkIncidentStateStore(store, serializer)
            .FindAsync(payload.WorkflowExecutionId, payload.IncidentId, cancellationToken);
        var liveness = await new GroundworkExecutionLivenessStateStore(store, serializer)
            .FindAsync(payload.WorkflowExecutionId, payload.OperationalStateId, cancellationToken);
        var ownership = await new GroundworkExecutionLivenessStateStore(store, serializer)
            .FindAsync(payload.WorkflowExecutionId, payload.OwnershipStateId, cancellationToken);
        var outbox = await new GroundworkRuntimePostCommitOutboxStore(store, serializer)
            .FindAsync(payload.OutboxItemId, cancellationToken);
        var marker = await store.LoadAsync(
            ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind,
            payload.CommitId,
            cancellationToken);

        var missing = new List<string>();
        if (workflow?.WorkflowExecutionId != payload.WorkflowExecutionId) missing.Add("workflow");
        if (activity?.Execution.ActivityExecutionId != payload.ActivityExecutionId) missing.Add("activity");
        if (inspection?.ActivityExecutionId != payload.ActivityExecutionId) missing.Add("inspection");
        if (hierarchy is null) missing.Add("hierarchy");
        if (scheduler?.WorkflowExecutionId != payload.WorkflowExecutionId) missing.Add("scheduler");
        if (bookmark?.BookmarkId != payload.BookmarkId) missing.Add("bookmark");
        if (value?.DurableValueId != payload.DurableValueId) missing.Add("durable-value");
        if (incident?.IncidentId != payload.IncidentId) missing.Add("incident");
        if (liveness?.OperationalStateId != payload.OperationalStateId) missing.Add("liveness");
        if (ownership?.ExecutionLease is null) missing.Add("ownership");
        if (outbox?.OutboxItemId != payload.OutboxItemId) missing.Add("outbox");
        if (marker is null) missing.Add("marker");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The child process could not rehydrate the complete runtime checkpoint bundle: {string.Join(',', missing)}.");
        }

        return new RuntimeCheckpointRestartProbeObservation(marker!.SchemaVersion, marker.Version);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}

internal sealed record RuntimeCheckpointRestartProbePayload
{
    [JsonConstructor]
    public RuntimeCheckpointRestartProbePayload(
        string commitId,
        string workflowExecutionId,
        string activityExecutionId,
        string bookmarkId,
        string durableValueId,
        string incidentId,
        string operationalStateId,
        string ownershipStateId,
        string outboxItemId)
    {
        CommitId = Require(commitId, nameof(commitId));
        WorkflowExecutionId = Require(workflowExecutionId, nameof(workflowExecutionId));
        ActivityExecutionId = Require(activityExecutionId, nameof(activityExecutionId));
        BookmarkId = Require(bookmarkId, nameof(bookmarkId));
        DurableValueId = Require(durableValueId, nameof(durableValueId));
        IncidentId = Require(incidentId, nameof(incidentId));
        OperationalStateId = Require(operationalStateId, nameof(operationalStateId));
        OwnershipStateId = Require(ownershipStateId, nameof(ownershipStateId));
        OutboxItemId = Require(outboxItemId, nameof(outboxItemId));
    }

    public string CommitId { get; }
    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }
    public string BookmarkId { get; }
    public string DurableValueId { get; }
    public string IncidentId { get; }
    public string OperationalStateId { get; }
    public string OwnershipStateId { get; }
    public string OutboxItemId { get; }

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A checkpoint probe identity is required.", parameterName)
            : value;
}

internal sealed record RuntimeCheckpointRestartProbeObservation
{
    public RuntimeCheckpointRestartProbeObservation(string schemaVersion, long documentVersion)
    {
        SchemaVersion = string.IsNullOrWhiteSpace(schemaVersion)
            ? throw new ArgumentException("A checkpoint marker schema version is required.", nameof(schemaVersion))
            : schemaVersion;
        DocumentVersion = documentVersion < 1
            ? throw new ArgumentOutOfRangeException(nameof(documentVersion), "A persisted checkpoint marker version is required.")
            : documentVersion;
    }

    public string SchemaVersion { get; }
    public long DocumentVersion { get; }
}
