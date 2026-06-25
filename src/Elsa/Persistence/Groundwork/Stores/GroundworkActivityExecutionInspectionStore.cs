using System.Text.Json;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityExecutionInspectionStore(IDocumentStore store) : IActivityExecutionInspectionStore
{
    /// <exception cref="GroundworkActivityExecutionInspectionStoreException">Thrown when the Groundwork document store or JSON projection mapping fails.</exception>
    public async ValueTask<ActivityExecutionInspectionProjection> SaveAsync(ActivityExecutionInspectionProjection projection, CancellationToken cancellationToken = default)
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
            var content = JsonSerializer.Serialize(document, GroundworkRuntimeJson.Options);

            await store.SaveAsync(
                new SaveDocumentRequest(
                    ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
                    DocumentId.Compose(projection.WorkflowExecutionId, projection.ActivityExecutionId),
                    ElsaRuntimeStorageManifest.SchemaVersion,
                    content),
                cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new GroundworkActivityExecutionInspectionStoreException("Failed to save the activity execution inspection projection.", e);
        }

        return projection;
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
            throw new GroundworkActivityExecutionInspectionStoreException("Failed to load the activity execution inspection projection.", e);
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
            throw new GroundworkActivityExecutionInspectionStoreException("Failed to list activity execution inspection summaries.", e);
        }
    }

    private static IReadOnlyCollection<ActivityExecutionInspectionSummaryProjection> Order(IEnumerable<ActivityExecutionInspectionSummaryProjection> projections) =>
        projections
            .OrderBy(projection => projection.ExecutionSequence)
            .ThenBy(projection => projection.ScheduledAt)
            .ThenBy(projection => projection.ActivityExecutionId, StringComparer.Ordinal)
            .ToArray();

    private static ActivityExecutionInspectionProjection Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<ActivityExecutionInspectionProjectionDocument>(envelope.ContentJson, GroundworkRuntimeJson.Options)!.Projection;

    private static ActivityExecutionInspectionSummaryProjection MapSummary(DocumentEnvelope envelope)
    {
        var document = JsonSerializer.Deserialize<ActivityExecutionInspectionProjectionDocument>(envelope.ContentJson, GroundworkRuntimeJson.Options)!;
        return document.Summary ?? ActivityExecutionInspectionSummaryProjection.FromProjection(document.Projection);
    }

    private sealed record ActivityExecutionInspectionProjectionDocument(
        string WorkflowExecutionId,
        string AuthoredActivityId,
        ActivityExecutionInspectionSummaryProjection? Summary,
        ActivityExecutionInspectionProjection Projection);
}
