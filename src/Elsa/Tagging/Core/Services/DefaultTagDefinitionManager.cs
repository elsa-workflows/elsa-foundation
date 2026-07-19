using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Primitives.Identity;

namespace Elsa.Tagging.Core.Services;

public sealed class DefaultTagDefinitionManager(
    ITagDefinitionStore store,
    ITagDefinitionAuditStore auditStore,
    ITagDefinitionAuditContext auditContext,
    TimeProvider timeProvider) : ITagDefinitionManager
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

        if (!await store.TryAddAsync(definition, cancellationToken))
            throw new InvalidOperationException($"A tag definition with canonical key '{definition.CanonicalKey}' already exists.");

        await auditStore.AppendAsync(new TagDefinitionAuditRecord(
            ShortIdentityGenerator.Generate(now),
            definition.Id,
            definition.CanonicalKey,
            "created",
            now,
            auditContext.Actor,
            auditContext.CorrelationId), cancellationToken);
        return definition;
    }

    public ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return store.ListAsync(request, cancellationToken);
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
        var displayName = request.DisplayName is null ? definition.DisplayName : request.DisplayName.Trim();
        var description = request.Description ?? definition.Description;
        var color = request.Color ?? definition.Color;
        TagDefinitionConstraints.ValidateMutableFields(displayName, description, color);

        definition.DisplayName = displayName;
        definition.Description = description;
        definition.Color = color;
        definition.Status = request.Status ?? definition.Status;
        definition.UpdatedAt = timeProvider.GetUtcNow();

        var save = await store.SaveWithRevisionAsync(definition, expectedRevision, cancellationToken);
        if (save.Status == TagDefinitionSaveStatus.NotFound)
            throw new InvalidOperationException($"Tag definition '{tagDefinitionId}' was not found.");
        if (save.Status != TagDefinitionSaveStatus.Saved || save.Revision is null)
            throw new TagDefinitionConflictException(tagDefinitionId);

        await auditStore.AppendAsync(new TagDefinitionAuditRecord(
            ShortIdentityGenerator.Generate(definition.UpdatedAt.Value),
            definition.Id,
            definition.CanonicalKey,
            "updated",
            definition.UpdatedAt.Value,
            auditContext.Actor,
            auditContext.CorrelationId), cancellationToken);
        return new TagDefinitionRevisionedRecord(definition, save.Revision);
    }
}
