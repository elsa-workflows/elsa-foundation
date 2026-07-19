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

            var existing = await LoadByLogicalIdentityAsync(
                projection.WorkflowExecutionId,
                projection.ActivityExecutionId,
                cancellationToken);
            var result = await SaveDocumentAsync(
                DocumentId.Compose(projection.WorkflowExecutionId, projection.ActivityExecutionId),
                document,
                cancellationToken,
                existing?.Version ?? 0);
            if (result.Status != DocumentStoreWriteStatus.Saved)
            {
                if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                    await LoadByLogicalIdentityAsync(projection.WorkflowExecutionId, projection.ActivityExecutionId, cancellationToken);
                throw new InvalidOperationException($"Groundwork rejected activity execution inspection projection '{projection.ActivityExecutionId}' in workflow execution '{projection.WorkflowExecutionId}' with status '{result.Status}'.");
            }
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
            return (await LoadByLogicalIdentityAsync(workflowExecutionId, activityExecutionId, cancellationToken))?.Projection;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GroundworkActivityExecutionInspectionStoreException($"Failed to load the activity execution inspection projection for workflow execution '{workflowExecutionId}' and activity execution '{activityExecutionId}'.", e);
        }
    }

    /// <exception cref="GroundworkActivityExecutionInspectionStoreException">Thrown when the Groundwork document store or JSON projection mapping fails.</exception>
    public async ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(
        ActivityExecutionInspectionSummaryPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            var result = await BoundedStore.QueryAsync(
                new DocumentQuery(
                    DocumentKind,
                    ElsaRuntimeStorageManifest.PageActivityExecutionInspectionSummariesQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                        ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                        query.WorkflowExecutionId))],
                    [
                        new DocumentQueryOrder(
                            ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField),
                        new DocumentQueryOrder(
                            ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryScheduledAtField),
                        new DocumentQueryOrder(
                            ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField)
                    ],
                    take: query.Limit,
                    continuation: query.ContinuationToken),
                cancellationToken);

            return new ActivityExecutionInspectionSummaryPage(
                query,
                result.Documents.Select(MapSummary).ToArray(),
                result.TotalCount,
                result.NextContinuation);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GroundworkActivityExecutionInspectionStoreException($"Failed to list activity execution inspection summaries for workflow execution '{query.WorkflowExecutionId}'.", e);
        }
    }

    private ActivityExecutionInspectionSummaryProjection MapSummary(DocumentEnvelope envelope)
    {
        // The summary fast path reads a fragment of ContentJson directly, so it is only valid for
        // current-version documents; a non-current envelope goes through the serializer so version policy
        // rejects it consistently.
        if (Serializer.IsCurrentVersion(envelope))
        {
            using var document = JsonDocument.Parse(envelope.ContentJson);
            if (document.RootElement.TryGetProperty("summary", out var summaryElement) && summaryElement.ValueKind is not JsonValueKind.Null)
                return Serializer.DeserializeElement<ActivityExecutionInspectionSummaryProjection>(summaryElement);
        }

        return ActivityExecutionInspectionSummaryProjection.FromProjection(
            Serializer.Deserialize<ActivityExecutionInspectionProjectionDocument>(envelope).Projection);
    }

    private async ValueTask<LoadedActivityExecutionInspectionProjection?> LoadByLogicalIdentityAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, DocumentId.Compose(workflowExecutionId, activityExecutionId), cancellationToken);
        if (envelope is null)
            return null;

        var projection = Serializer.Deserialize<ActivityExecutionInspectionProjectionDocument>(envelope).Projection;
        if (!StringComparer.Ordinal.Equals(projection.WorkflowExecutionId, workflowExecutionId)
            || !StringComparer.Ordinal.Equals(projection.ActivityExecutionId, activityExecutionId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for activity execution inspection projection '{activityExecutionId}' in workflow execution '{workflowExecutionId}'.");
        }

        return new LoadedActivityExecutionInspectionProjection(projection, envelope.Version);
    }

    private sealed record ActivityExecutionInspectionProjectionDocument(
        string WorkflowExecutionId,
        string AuthoredActivityId,
        string? ExecutionScopeId,
        ActivityExecutionAttemptLineage? Attempt,
        ActivityExecutionInspectionSummaryProjection? Summary,
        ActivityExecutionInspectionProjection Projection);

    private sealed record LoadedActivityExecutionInspectionProjection(ActivityExecutionInspectionProjection Projection, long Version);
}
