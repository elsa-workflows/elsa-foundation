using System.Text.Json;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityExecutionInspectionStore(IDocumentStore store) : IActivityExecutionInspectionStore
{
    public async ValueTask<ActivityExecutionInspectionProjection> SaveAsync(ActivityExecutionInspectionProjection projection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.ActivityExecutionId);

        var document = new ActivityExecutionInspectionProjectionDocument(
            projection.WorkflowExecutionId,
            projection.AuthoredActivityId,
            projection);
        var content = JsonSerializer.Serialize(document, GroundworkRuntimeJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
                DocumentId.Compose(projection.WorkflowExecutionId, projection.ActivityExecutionId),
                ElsaRuntimeStorageManifest.SchemaVersion,
                content),
            cancellationToken);

        return projection;
    }

    public async ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
            DocumentId.Compose(workflowExecutionId, activityExecutionId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyCollection<ActivityExecutionInspectionProjection>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
                ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                workflowExecutionId),
            cancellationToken);

        return Order(envelopes.Select(Map));
    }

    public async ValueTask<IReadOnlyCollection<ActivityExecutionInspectionProjection>> ListByAuthoredActivityIdAsync(string workflowExecutionId, string authoredActivityId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoredActivityId);

        var projections = await ListAsync(workflowExecutionId, cancellationToken);
        return projections
            .Where(projection => StringComparer.Ordinal.Equals(projection.AuthoredActivityId, authoredActivityId))
            .ToArray();
    }

    private static IReadOnlyCollection<ActivityExecutionInspectionProjection> Order(IEnumerable<ActivityExecutionInspectionProjection> projections) =>
        projections
            .OrderBy(projection => projection.ExecutionSequence)
            .ThenBy(projection => projection.ScheduledAt)
            .ThenBy(projection => projection.ActivityExecutionId, StringComparer.Ordinal)
            .ToArray();

    private static ActivityExecutionInspectionProjection Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<ActivityExecutionInspectionProjectionDocument>(envelope.ContentJson, GroundworkRuntimeJson.Options)!.Projection;

    private sealed record ActivityExecutionInspectionProjectionDocument(
        string WorkflowExecutionId,
        string AuthoredActivityId,
        ActivityExecutionInspectionProjection Projection);
}
