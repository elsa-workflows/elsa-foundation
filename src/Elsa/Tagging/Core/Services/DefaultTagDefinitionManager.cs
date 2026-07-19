using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Primitives.Identity;

namespace Elsa.Tagging.Core.Services;

public sealed class DefaultTagDefinitionManager(
    ITagDefinitionStore store,
    ITagDefinitionAuditStore auditStore,
    ITagDefinitionAuditContext auditContext,
    TimeProvider timeProvider,
    ITagDefinitionAtomicChangeStore? atomicChangeStore = null) : ITagDefinitionManager
{
    public async ValueTask<TagDefinition> CreateAsync(CreateTagDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        TagDefinitionConstraints.ValidateCanonicalKey(request.CanonicalKey, request.IsHostProvisioning);
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.CanonicalKey : request.DisplayName.Trim();
        TagDefinitionConstraints.ValidateMutableFields(displayName, request.Description, request.Color);

        var now = timeProvider.GetUtcNow();
        var definition = new TagDefinition
        {
            CanonicalKey = request.CanonicalKey,
            DisplayName = displayName,
            Description = request.Description,
            Color = request.Color,
            CreatedAt = now
        };

        var audit = Audit(definition, "created", now, before: null);
        var created = atomicChangeStore is not null
            ? await atomicChangeStore.TryAddAndAppendAuditAsync(definition, audit, cancellationToken)
            : await store.TryAddAsync(definition, cancellationToken);
        if (!created)
            throw new InvalidOperationException($"A tag definition with canonical key '{definition.CanonicalKey}' already exists.");

        if (atomicChangeStore is null)
            await auditStore.AppendAsync(audit, cancellationToken);
        return definition;
    }

    public ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return store.ListAsync(request, cancellationToken);
    }

    public ValueTask<IReadOnlyList<TagDefinitionRevisionedRecord>> ListWithRevisionsAsync(
        TagDefinitionListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return store.ListWithRevisionsAsync(request, cancellationToken);
    }

    public ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(string tagDefinitionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tagDefinitionId))
            throw new ArgumentException("A tag definition ID is required.", nameof(tagDefinitionId));
        return store.FindWithRevisionAsync(tagDefinitionId, cancellationToken);
    }

    public async ValueTask<TagDefinitionRevisionedRecord> UpdateAsync(
        string tagDefinitionId,
        UpdateTagDefinitionRequest request,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(expectedRevision))
            throw new ArgumentException("An expected revision is required.", nameof(expectedRevision));

        var loaded = await FindWithRevisionAsync(tagDefinitionId, cancellationToken)
            ?? throw new InvalidOperationException($"Tag definition '{tagDefinitionId}' was not found.");
        var definition = loaded.Definition;
        var before = TagDefinitionAuditValues.From(definition);
        var displayName = request.HasDisplayName
            ? request.DisplayName?.Trim() ?? string.Empty
            : definition.DisplayName;
        var description = request.HasDescription ? request.Description : definition.Description;
        var color = request.HasColor ? request.Color : definition.Color;
        TagDefinitionConstraints.ValidateMutableFields(displayName, description, color);

        definition.DisplayName = displayName;
        definition.Description = description;
        definition.Color = color;
        definition.Status = request.HasStatus
            ? request.Status ?? throw new ArgumentException("Status cannot be null.", nameof(request))
            : definition.Status;
        definition.UpdatedAt = timeProvider.GetUtcNow();

        var audit = Audit(definition, "updated", definition.UpdatedAt.Value, before);
        var save = atomicChangeStore is not null
            ? await atomicChangeStore.SaveWithRevisionAndAppendAuditAsync(definition, expectedRevision, audit, cancellationToken)
            : await store.SaveWithRevisionAsync(definition, expectedRevision, cancellationToken);
        if (save.Status == TagDefinitionSaveStatus.NotFound)
            throw new InvalidOperationException($"Tag definition '{tagDefinitionId}' was not found.");
        if (save.Status != TagDefinitionSaveStatus.Saved || save.Revision is null)
            throw new TagDefinitionConflictException(tagDefinitionId);

        if (atomicChangeStore is null)
            await auditStore.AppendAsync(audit, cancellationToken);
        return new TagDefinitionRevisionedRecord(definition, save.Revision);
    }

    private TagDefinitionAuditRecord Audit(
        TagDefinition definition,
        string operation,
        DateTimeOffset occurredAt,
        TagDefinitionAuditValues? before) => new(
        ShortIdentityGenerator.Generate(occurredAt),
        definition.Id,
        definition.CanonicalKey,
        operation,
        occurredAt,
        auditContext.TenantId,
        auditContext.Actor,
        auditContext.CorrelationId,
        before,
        TagDefinitionAuditValues.From(definition));
}
