using System.Text.Json;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityExecutionInspectionStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer) : IActivityExecutionInspectionStore, IActivityExecutionInspectionWriter
{
    /// <exception cref="GroundworkActivityExecutionInspectionStoreException">Thrown when the Groundwork document store or JSON projection mapping fails.</exception>
    public async ValueTask SaveAsync(ActivityExecutionInspectionProjection projection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.ActivityExecutionId);

        try
        {
            var document = new ActivityExecutionInspectionProjectionDocument(
                projection.WorkflowExecutionId,
                projection.AuthoredActivityId,
                ActivityExecutionInspectionSummaryProjection.FromProjection(projection),
                projection);
            var (schemaVersion, content) = serializer.Serialize(ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind, document);

            await store.SaveAsync(
                new SaveDocumentRequest(
                    ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
                    DocumentId.Compose(projection.WorkflowExecutionId, projection.ActivityExecutionId),
                    schemaVersion,
                    content),
                cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GroundworkActivityExecutionInspectionStoreException($"Failed to save the activity execution inspection projection for workflow execution '{projection.WorkflowExecutionId}' and activity execution '{projection.ActivityExecutionId}'.", e);
        }
    }

    /// <exception cref="GroundworkActivityExecutionInspectionStoreException">Thrown when the Groundwork document store or JSON projection mapping fails.</exception>
    public async ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);

        try
        {
            var envelope = await store.LoadAsync(
                ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
                DocumentId.Compose(workflowExecutionId, activityExecutionId),
                cancellationToken);

            return envelope is null ? null : Map(envelope);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GroundworkActivityExecutionInspectionStoreException($"Failed to load the activity execution inspection projection for workflow execution '{workflowExecutionId}' and activity execution '{activityExecutionId}'.", e);
        }
    }

    /// <exception cref="GroundworkActivityExecutionInspectionStoreException">Thrown when the Groundwork document store or JSON projection mapping fails.</exception>
    public async ValueTask<IReadOnlyCollection<ActivityExecutionInspectionSummaryProjection>> ListSummariesAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        try
        {
            var envelopes = await store.QueryAsync(
                new DocumentStoreQuery(
                    ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
                    ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                    workflowExecutionId),
                cancellationToken);

            return Order(envelopes.Select(MapSummary));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GroundworkActivityExecutionInspectionStoreException($"Failed to list activity execution inspection summaries for workflow execution '{workflowExecutionId}'.", e);
        }
    }

    private static IReadOnlyCollection<ActivityExecutionInspectionSummaryProjection> Order(IEnumerable<ActivityExecutionInspectionSummaryProjection> projections) =>
        projections
            .OrderBy(projection => projection.ExecutionSequence)
            .ThenBy(projection => projection.ScheduledAt)
            .ThenBy(projection => projection.ActivityExecutionId, StringComparer.Ordinal)
            .ToArray();

    private ActivityExecutionInspectionProjection Map(DocumentEnvelope envelope) =>
        serializer.Deserialize<ActivityExecutionInspectionProjectionDocument>(envelope).Projection;

    private ActivityExecutionInspectionSummaryProjection MapSummary(DocumentEnvelope envelope)
    {
        // The summary fast path reads a fragment of ContentJson directly, so it is only valid for
        // current-version documents; older versions go through Map so the upcaster chain applies.
        if (serializer.IsCurrentVersion(envelope))
        {
            using var document = JsonDocument.Parse(envelope.ContentJson);
            if (document.RootElement.TryGetProperty("summary", out var summaryElement) && summaryElement.ValueKind is not JsonValueKind.Null)
                return serializer.DeserializeElement<ActivityExecutionInspectionSummaryProjection>(summaryElement);
        }

        return ActivityExecutionInspectionSummaryProjection.FromProjection(Map(envelope));
    }

    private sealed record ActivityExecutionInspectionProjectionDocument(
        string WorkflowExecutionId,
        string AuthoredActivityId,
        ActivityExecutionInspectionSummaryProjection? Summary,
        ActivityExecutionInspectionProjection Projection);
}
