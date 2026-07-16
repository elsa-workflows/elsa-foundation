using System.Text.Json;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityExecutionInspectionStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind, boundedStore), IActivityExecutionInspectionStore, IActivityExecutionInspectionWriter
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
                projection.ExecutionScopeId,
                projection.Attempt,
                ActivityExecutionInspectionSummaryProjection.FromProjection(projection),
                projection);

            await SaveDocumentAsync(DocumentId.Compose(projection.WorkflowExecutionId, projection.ActivityExecutionId), document, cancellationToken);
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
            return await LoadDocumentAsync<ActivityExecutionInspectionProjectionDocument, ActivityExecutionInspectionProjection>(
                DocumentId.Compose(workflowExecutionId, activityExecutionId), document => document.Projection, cancellationToken);
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
            var envelopes = (await BoundedStore.QueryAsync(
                new DocumentQuery(
                    DocumentKind,
                    ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                        ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                        workflowExecutionId))]),
                cancellationToken)).Documents;

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

    private ActivityExecutionInspectionSummaryProjection MapSummary(DocumentEnvelope envelope)
    {
        // The summary fast path reads a fragment of ContentJson directly, so it is only valid for
        // current-version documents; older versions go through the full projection so the upcaster chain applies.
        if (Serializer.IsCurrentVersion(envelope))
        {
            using var document = JsonDocument.Parse(envelope.ContentJson);
            if (document.RootElement.TryGetProperty("summary", out var summaryElement) && summaryElement.ValueKind is not JsonValueKind.Null)
                return Serializer.DeserializeElement<ActivityExecutionInspectionSummaryProjection>(summaryElement);
        }

        return ActivityExecutionInspectionSummaryProjection.FromProjection(
            Serializer.Deserialize<ActivityExecutionInspectionProjectionDocument>(envelope).Projection);
    }

    private sealed record ActivityExecutionInspectionProjectionDocument(
        string WorkflowExecutionId,
        string AuthoredActivityId,
        string? ExecutionScopeId,
        ActivityExecutionAttemptLineage? Attempt,
        ActivityExecutionInspectionSummaryProjection? Summary,
        ActivityExecutionInspectionProjection Projection);
}
