using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkWorkflowDefinitionTagStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor,
    TimeProvider timeProvider,
    IBoundedDocumentStore? boundedStore = null) : IWorkflowDefinitionTagStore
{
    private IBoundedDocumentStore BoundedStore => boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
        "Workflow-definition tag queries require an admitted bounded document-store runtime.");

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

        var definitionIds = workflowDefinitionIds.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var definitionId in definitionIds)
            ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        if (definitionIds.Length == 0)
            return [];

        var query = new DocumentQuery(
            WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetDocumentKind,
            WorkflowsDesignStorageManifest.FindWorkflowDefinitionTagSetsByDefinitionIdsQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.In(
                WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetWorkflowDefinitionIdField,
                definitionIds))],
            [],
            skip: 0,
            take: definitionIds.Length);
        var found = (await BoundedStore.QueryAsync(query, cancellationToken)).Documents
            .Select(envelope =>
            {
                var document = Deserialize(envelope);
                accessContextAccessor.Current.EnsureTenantScope(document.TenantId);
                return ToModel(document, envelope.Version);
            })
            .ToDictionary(tagSet => tagSet.WorkflowDefinitionId, StringComparer.Ordinal);

        return definitionIds
            .Select(definitionId => found.GetValueOrDefault(definitionId) ?? new WorkflowDefinitionTagSet(
                definitionId,
                accessContextAccessor.Current.Scope?.Value,
                WorkflowDefinitionTagRevision.Initial,
                []))
            .ToArray();
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

        var workflowDefinitionEnvelope = await store.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            request.WorkflowDefinitionId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Workflow definition '{request.WorkflowDefinitionId}' does not exist.");
        var workflowDefinitionDocument = GroundworkWorkflowDefinitionDocuments.Deserialize(workflowDefinitionEnvelope);
        accessContextAccessor.Current.EnsureTenantScope(workflowDefinitionDocument.Entity.TenantId);
        if (!StringComparer.Ordinal.Equals(workflowDefinitionDocument.Entity.TenantId, request.TenantId))
            throw new InvalidOperationException("The requested resource does not belong to the current persistence scope.");
        if (workflowDefinitionDocument.Entity.DeletedAt is not null)
            throw new InvalidOperationException("Restore the workflow definition before changing its tags.");

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
        var controlledValues = (request.ControlledValues ?? [])
            .Select(value =>
            {
                value.Validate();
                ValidateControlledValue(value);
                return value;
            })
            .Distinct()
            .OrderBy(value => value.TagDefinitionId, StringComparer.Ordinal)
            .ThenBy(value => value.ControlledValueId, StringComparer.Ordinal)
            .ToArray();
        if (controlledValues.GroupBy(value => value.TagDefinitionId, StringComparer.Ordinal)
            .Any(group => group.Select(value => value.ControlledValueId).Distinct(StringComparer.Ordinal).Skip(1).Any()))
            throw new ArgumentException("A single-valued controlled tag can have only one manual value.", nameof(request.ControlledValues));
        if (tagDefinitionIds.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(request.TagDefinitionIds), "At most 64 marker tags can be assigned to one workflow definition.");
        var retainedAssertions = existing.Assertions
            .Where(assertion => !StringComparer.Ordinal.Equals(assertion.OriginKind, WorkflowDefinitionTagOriginKinds.Manual))
            .ToArray();
        var assertions = retainedAssertions
            .Concat(tagDefinitionIds.Select(WorkflowDefinitionTagAssertion.Manual))
            .Concat(controlledValues.Select(WorkflowDefinitionTagAssertion.Manual))
            .OrderBy(assertion => assertion.TagDefinitionId, StringComparer.Ordinal)
            .ThenBy(assertion => assertion.ControlledValueId, StringComparer.Ordinal)
            .ThenBy(assertion => assertion.OriginKind, StringComparer.Ordinal)
            .ToArray();
        var markerProjection = MarkerProjection(assertions
            .Where(assertion => assertion.ControlledValueId is null)
            .Select(assertion => assertion.TagDefinitionId)
            .Distinct(StringComparer.Ordinal));
        var controlledProjection = ControlledProjection(assertions
            .Where(assertion => assertion.ControlledValueId is not null)
            .Select(assertion => new WorkflowDefinitionControlledTagValue(assertion.TagDefinitionId, assertion.ControlledValueId!)));
        if (markerProjection.Length > WorkflowsDesignStorageManifest.WorkflowDefinitionMarkerProjectionLength)
            throw new ArgumentOutOfRangeException(
                nameof(request.TagDefinitionIds),
                $"The encoded marker assignment cannot exceed {WorkflowsDesignStorageManifest.WorkflowDefinitionMarkerProjectionLength} characters.");
        if (controlledProjection.Length > WorkflowsDesignStorageManifest.WorkflowDefinitionMarkerProjectionLength)
            throw new ArgumentOutOfRangeException(
                nameof(request.ControlledValues),
                $"The encoded controlled assignment cannot exceed {WorkflowsDesignStorageManifest.WorkflowDefinitionMarkerProjectionLength} characters.");
        var nextVersion = expectedVersion + 1;
        var nextRevision = WorkflowDefinitionTagRevision.FromVersion(nextVersion);
        var now = timeProvider.GetUtcNow();
        var updated = new WorkflowDefinitionTagSetDocument(
            WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetCollection,
            request.WorkflowDefinitionId,
            request.TenantId,
            markerProjection,
            controlledProjection,
            assertions,
            now);
        var beforeIds = existing.Assertions
            .Where(x => StringComparer.Ordinal.Equals(x.OriginKind, WorkflowDefinitionTagOriginKinds.Manual)
                && x.ControlledValueId is null)
            .Select(x => x.TagDefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        var afterIds = tagDefinitionIds.ToHashSet(StringComparer.Ordinal);
        var beforeControlledValueIds = existing.Assertions
            .Select(assertion => assertion.ControlledValueId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var afterControlledValueIds = controlledValues
            .Select(value => value.ControlledValueId)
            .ToHashSet(StringComparer.Ordinal);
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
                now,
                afterControlledValueIds.Except(beforeControlledValueIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                beforeControlledValueIds.Except(afterControlledValueIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));

        try
        {
            await store.SaveAllAsync(
                DocumentCommitScope.Of(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetDocumentKind,
                    WorkflowsDesignStorageManifest.WorkflowDefinitionTagAuditDocumentKind),
                [
                    GroundworkWorkflowDefinitionDocuments.Save(
                        workflowDefinitionDocument.Entity,
                        markerProjection,
                        workflowDefinitionEnvelope.Version,
                        controlledProjection),
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

    public static string MarkerProjection(IEnumerable<string> tagDefinitionIds)
    {
        var ids = tagDefinitionIds.ToArray();
        return ids.Length == 0 ? "|" : $"|{string.Join('|', ids)}|";
    }

    public static string ControlledProjection(IEnumerable<WorkflowDefinitionControlledTagValue> controlledValues)
    {
        var values = controlledValues
            .GroupBy(value => value.TagDefinitionId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group =>
            {
                var valueIds = group
                    .Select(value => value.ControlledValueId)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var tokens = valueIds.Select(valueId => $"{group.Key}={valueId}").ToList();
                if (valueIds.Length > 1)
                    tokens.Add($"{group.Key}={WorkflowDefinitionTagEffectiveReducer.ConflictProjectionValue}");
                return tokens;
            })
            .ToArray();
        return values.Length == 0 ? "|" : $"|{string.Join('|', values)}|";
    }

    public static string ControlledProjectionToken(string tagDefinitionId, string controlledValueId)
    {
        ValidateTagDefinitionId(tagDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlledValueId);
        if (controlledValueId.Contains('|', StringComparison.Ordinal) || controlledValueId.Contains('=', StringComparison.Ordinal))
            throw new ArgumentException("Controlled value identities cannot contain projection delimiters.", nameof(controlledValueId));
        if (StringComparer.Ordinal.Equals(
                controlledValueId,
                WorkflowDefinitionTagEffectiveReducer.ConflictProjectionValue))
            throw new ArgumentException("The controlled value identity is reserved.", nameof(controlledValueId));
        return $"|{tagDefinitionId}={controlledValueId}|";
    }

    public static string ControlledConflictProjectionToken(string tagDefinitionId)
    {
        ValidateTagDefinitionId(tagDefinitionId);
        return $"|{tagDefinitionId}={WorkflowDefinitionTagEffectiveReducer.ConflictProjectionValue}|";
    }

    private static void ValidateControlledValue(WorkflowDefinitionControlledTagValue controlledValue) =>
        _ = ControlledProjectionToken(controlledValue.TagDefinitionId, controlledValue.ControlledValueId);

    private sealed record WorkflowDefinitionTagSetDocument(
        string Collection,
        string WorkflowDefinitionId,
        string? TenantId,
        string MarkerProjection,
        string ControlledProjection,
        IReadOnlyCollection<WorkflowDefinitionTagAssertion> Assertions,
        DateTimeOffset LastModifiedAt);

    private sealed record WorkflowDefinitionTagAuditDocument(
        string Collection,
        WorkflowDefinitionTagAuditFact Fact);
}
