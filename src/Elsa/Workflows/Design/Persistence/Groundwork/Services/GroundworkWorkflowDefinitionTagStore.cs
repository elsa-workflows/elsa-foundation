using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkWorkflowDefinitionTagStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor,
    TimeProvider timeProvider) : IWorkflowDefinitionTagStore
{
    public async Task<WorkflowDefinitionTagSet> GetAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        var envelope = await store.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetDocumentKind,
            workflowDefinitionId,
            cancellationToken);
        if (envelope is null)
        {
            return new(
                workflowDefinitionId,
                accessContextAccessor.Current.Scope?.Value,
                WorkflowDefinitionTagRevision.Initial,
                []);
        }

        var document = Deserialize(envelope);
        accessContextAccessor.Current.EnsureTenantScope(document.TenantId);
        return ToModel(document, envelope.Version);
    }

    public async Task<IReadOnlyCollection<WorkflowDefinitionTagSet>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowDefinitionIds);
        if (workflowDefinitionIds.Count > 100)
            throw new ArgumentOutOfRangeException(nameof(workflowDefinitionIds), "At most 100 workflow definition tag sets can be read at once.");

        var result = new List<WorkflowDefinitionTagSet>(workflowDefinitionIds.Count);
        foreach (var id in workflowDefinitionIds.Distinct(StringComparer.Ordinal))
            result.Add(await GetAsync(id, cancellationToken));
        return result;
    }

    public async Task<WorkflowDefinitionTagReplaceResult> ReplaceManualAsync(
        ReplaceWorkflowDefinitionManualTags request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CorrelationId);
        ArgumentNullException.ThrowIfNull(request.TagDefinitionIds);
        accessContextAccessor.Current.EnsureTenantScope(request.TenantId);

        if (!WorkflowDefinitionTagRevision.TryGetVersion(request.ExpectedRevision, out var expectedVersion))
            return await ConflictAsync(request.WorkflowDefinitionId, cancellationToken);

        var existingEnvelope = await store.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetDocumentKind,
            request.WorkflowDefinitionId,
            cancellationToken);
        if ((existingEnvelope?.Version ?? 0) != expectedVersion)
            return await ConflictAsync(request.WorkflowDefinitionId, cancellationToken);

        var existing = existingEnvelope is null
            ? new WorkflowDefinitionTagSetDocument(
                WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetCollection,
                request.WorkflowDefinitionId,
                request.TenantId,
                string.Empty,
                [],
                timeProvider.GetUtcNow())
            : Deserialize(existingEnvelope);
        accessContextAccessor.Current.EnsureTenantScope(existing.TenantId);
        if (!StringComparer.Ordinal.Equals(existing.TenantId, request.TenantId))
            throw new InvalidOperationException("The requested resource does not belong to the current persistence scope.");

        var tagDefinitionIds = request.TagDefinitionIds
            .Select(ValidateTagDefinitionId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var assertions = tagDefinitionIds.Select(WorkflowDefinitionTagAssertion.Manual).ToArray();
        var nextVersion = expectedVersion + 1;
        var nextRevision = WorkflowDefinitionTagRevision.FromVersion(nextVersion);
        var now = timeProvider.GetUtcNow();
        var updated = new WorkflowDefinitionTagSetDocument(
            WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetCollection,
            request.WorkflowDefinitionId,
            request.TenantId,
            MarkerProjection(tagDefinitionIds),
            assertions,
            now);
        var beforeIds = existing.Assertions
            .Where(x => StringComparer.Ordinal.Equals(x.OriginKind, WorkflowDefinitionTagOriginKinds.Manual))
            .Select(x => x.TagDefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        var afterIds = tagDefinitionIds.ToHashSet(StringComparer.Ordinal);
        var audit = new WorkflowDefinitionTagAuditDocument(
            WorkflowsDesignStorageManifest.WorkflowDefinitionTagAuditCollection,
            new(
                $"{request.WorkflowDefinitionId}:{nextVersion:D20}",
                request.WorkflowDefinitionId,
                request.TenantId,
                WorkflowDefinitionTagOriginKinds.Manual,
                request.ActorId,
                request.CorrelationId,
                request.IdempotencyId,
                WorkflowDefinitionTagRevision.FromVersion(expectedVersion),
                nextRevision,
                afterIds.Except(beforeIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                beforeIds.Except(afterIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                now));

        try
        {
            await store.SaveAllAsync(
                DocumentCommitScope.Of(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionTagAuditDocumentKind),
                [
                    Save(
                        WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetDocumentKind,
                        request.WorkflowDefinitionId,
                        updated,
                        expectedVersion),
                    Save(
                        WorkflowsDesignStorageManifest.WorkflowDefinitionTagAuditDocumentKind,
                        audit.Fact.Id,
                        audit,
                        expectedVersion: 0)
                ],
                cancellationToken);
        }
        catch (DocumentAtomicWriteException exception)
            when (exception.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound)
        {
            return await ConflictAsync(request.WorkflowDefinitionId, cancellationToken);
        }

        return new(
            WorkflowDefinitionTagReplaceStatus.Saved,
            new(request.WorkflowDefinitionId, request.TenantId, nextRevision, assertions));
    }

    private async Task<WorkflowDefinitionTagReplaceResult> ConflictAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken)
    {
        var current = await GetAsync(workflowDefinitionId, cancellationToken);
        return new(WorkflowDefinitionTagReplaceStatus.Conflict, CurrentRevision: current.Revision);
    }

    private static SaveDocumentRequest Save<T>(
        string kind,
        string id,
        T document,
        long expectedVersion) =>
        new(
            kind,
            id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, GroundworkDesignJson.Options),
            expectedVersion);

    private static WorkflowDefinitionTagSetDocument Deserialize(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<WorkflowDefinitionTagSetDocument>(envelope.ContentJson, GroundworkDesignJson.Options)
        ?? throw new InvalidOperationException($"Workflow definition tag set '{envelope.Id}' could not be deserialized.");

    private static WorkflowDefinitionTagSet ToModel(WorkflowDefinitionTagSetDocument document, long version) =>
        new(
            document.WorkflowDefinitionId,
            document.TenantId,
            WorkflowDefinitionTagRevision.FromVersion(version),
            document.Assertions);

    private static string ValidateTagDefinitionId(string tagDefinitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagDefinitionId);
        if (tagDefinitionId.Contains('|', StringComparison.Ordinal))
            throw new ArgumentException("Tag definition identities cannot contain the marker projection delimiter.", nameof(tagDefinitionId));
        return tagDefinitionId;
    }

    public static string MarkerProjection(IEnumerable<string> tagDefinitionIds) =>
        $"|{string.Join('|', tagDefinitionIds)}|";

    private sealed record WorkflowDefinitionTagSetDocument(
        string Collection,
        string WorkflowDefinitionId,
        string? TenantId,
        string MarkerProjection,
        IReadOnlyCollection<WorkflowDefinitionTagAssertion> Assertions,
        DateTimeOffset LastModifiedAt);

    private sealed record WorkflowDefinitionTagAuditDocument(
        string Collection,
        WorkflowDefinitionTagAuditFact Fact);
}
