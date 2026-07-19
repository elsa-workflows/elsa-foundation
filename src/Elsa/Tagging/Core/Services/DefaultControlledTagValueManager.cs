using Elsa.Primitives.Identity;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Core.Services;

public sealed class DefaultControlledTagValueManager(
    ITagDefinitionStore definitions,
    IControlledTagValueStore values,
    ITagDefinitionAuditContext auditContext,
    TimeProvider timeProvider,
    IControlledTagValueAtomicChangeStore? atomicChanges = null) : IControlledTagValueManager
{
    public async ValueTask<ControlledTagValue> CreateAsync(CreateControlledTagValueRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var definition = await definitions.FindWithRevisionAsync(request.TagDefinitionId, cancellationToken)
            ?? throw new ArgumentException($"Tag definition '{request.TagDefinitionId}' was not found.", nameof(request.TagDefinitionId));
        EnsureControlledDefinition(definition.Definition, request.TagDefinitionId);
        var atomicChangeStore = atomicChanges
            ?? throw new InvalidOperationException(
                "Controlled tag value changes require atomic catalog persistence.");
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.CanonicalKey : request.DisplayName.Trim();
        ControlledTagValueConstraints.Validate(request.TagDefinitionId, request.CanonicalKey, displayName, request.Description, request.Color, request.SortOrder);
        var now = timeProvider.GetUtcNow();
        var value = new ControlledTagValue
        {
            Id = ShortIdentityGenerator.Generate(now),
            TagDefinitionId = request.TagDefinitionId,
            CanonicalKey = request.CanonicalKey,
            DisplayName = displayName,
            Description = request.Description,
            Color = request.Color,
            SortOrder = request.SortOrder,
            CreatedAt = now
        };
        var audit = Audit(value, "created", now, null);
        for (var attempt = 0; attempt <= ControlledTagValueConstraints.MaximumValuesPerDefinition; attempt++)
        {
            var existingValues = await values.ListWithRevisionsAsync(
                new ControlledTagValueListRequest
                {
                    TagDefinitionId = request.TagDefinitionId,
                    ActiveOnly = false
                },
                cancellationToken);
            if (existingValues.Count >= ControlledTagValueConstraints.MaximumValuesPerDefinition)
            {
                throw new InvalidOperationException(
                    $"A controlled tag definition supports at most {ControlledTagValueConstraints.MaximumValuesPerDefinition} values.");
            }

            var result = await atomicChangeStore.TryAddWithinLimitAndAppendAuditAsync(
                value,
                audit,
                existingValues.Count,
                ControlledTagValueConstraints.MaximumValuesPerDefinition,
                cancellationToken);
            if (result == ControlledTagValueCreateResult.Created)
                return value;
            if (result == ControlledTagValueCreateResult.LimitReached)
            {
                throw new InvalidOperationException(
                    $"A controlled tag definition supports at most {ControlledTagValueConstraints.MaximumValuesPerDefinition} values.");
            }
            if (await values.FindByCanonicalKeyAsync(
                    value.TagDefinitionId,
                    value.CanonicalKey,
                    cancellationToken) is not null)
            {
                throw Duplicate(value);
            }
        }

        throw new InvalidOperationException("The controlled value catalog changed too frequently; retry the operation.");
    }

    public ValueTask<IReadOnlyList<ControlledTagValueRevisionedRecord>> ListWithRevisionsAsync(ControlledTagValueListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TagDefinitionId);
        return values.ListWithRevisionsAsync(request, cancellationToken);
    }

    public ValueTask<ControlledTagValueRevisionedRecord?> FindWithRevisionAsync(string controlledTagValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlledTagValueId);
        return values.FindWithRevisionAsync(controlledTagValueId, cancellationToken);
    }

    public async ValueTask<ControlledTagValueRevisionedRecord> UpdateAsync(string controlledTagValueId, UpdateControlledTagValueRequest request, string expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        var loaded = await FindWithRevisionAsync(controlledTagValueId, cancellationToken)
            ?? throw new InvalidOperationException($"Controlled tag value '{controlledTagValueId}' was not found.");
        var atomicChangeStore = atomicChanges
            ?? throw new InvalidOperationException(
                "Controlled tag value changes require atomic catalog persistence.");
        var value = loaded.Value;
        var before = ControlledTagValueAuditValues.From(value);
        var displayName = request.HasDisplayName ? request.DisplayName?.Trim() ?? string.Empty : value.DisplayName;
        var description = request.HasDescription ? request.Description : value.Description;
        var color = request.HasColor ? request.Color : value.Color;
        var status = request.HasStatus ? request.Status ?? throw new ArgumentException("Status cannot be null.", nameof(request)) : value.Status;
        var sortOrder = request.HasSortOrder ? request.SortOrder : value.SortOrder;
        ControlledTagValueConstraints.Validate(value.TagDefinitionId, value.CanonicalKey, displayName, description, color, sortOrder);
        value.DisplayName = displayName;
        value.Description = description;
        value.Color = color;
        value.Status = status;
        value.SortOrder = sortOrder;
        value.UpdatedAt = timeProvider.GetUtcNow();
        var audit = Audit(value, "updated", value.UpdatedAt.Value, before);
        var saved = await atomicChangeStore.SaveWithRevisionAndAppendAuditAsync(
            value,
            expectedRevision,
            audit,
            cancellationToken);
        if (saved.Status == TagDefinitionSaveStatus.NotFound)
            throw new InvalidOperationException($"Controlled tag value '{controlledTagValueId}' was not found.");
        if (saved.Status != TagDefinitionSaveStatus.Saved || saved.Revision is null)
            throw new TagDefinitionConflictException(controlledTagValueId);
        return new(value, saved.Revision);
    }

    private static void EnsureControlledDefinition(TagDefinition definition, string definitionId)
    {
        if (definition.Status != TagDefinitionStatus.Active)
            throw new ArgumentException($"Tag definition '{definitionId}' is retired.", nameof(definitionId));
        if (definition.ValueMode != TagValueMode.Controlled || definition.Cardinality != TagCardinality.Single)
            throw new ArgumentException($"Tag definition '{definitionId}' is not a single-valued controlled tag.", nameof(definitionId));
    }

    private ControlledTagValueAuditRecord Audit(ControlledTagValue value, string operation, DateTimeOffset occurredAt, ControlledTagValueAuditValues? before) => new(
        ShortIdentityGenerator.Generate(occurredAt), value.Id, value.TagDefinitionId, value.CanonicalKey, operation, occurredAt,
        auditContext.TenantId, auditContext.Actor, auditContext.CorrelationId, before, ControlledTagValueAuditValues.From(value));

    private static InvalidOperationException Duplicate(ControlledTagValue value) =>
        new($"A controlled value with canonical key '{value.CanonicalKey}' already exists for tag definition '{value.TagDefinitionId}'.");
}
