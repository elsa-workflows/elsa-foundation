using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Services;

public interface IWorkflowDefinitionTagAuthorizationContext
{
    string ActorId { get; }
    string CorrelationId { get; }
    bool CanAssign { get; }
}

public sealed class WorkflowDefinitionTagApplicationService(
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionTagAuthorizationContext authorization,
    IDeferredEventPublisher eventPublisher,
    IWorkflowDefinitionTagStore? tagStore = null,
    ITagDefinitionStore? catalog = null,
    ITagDefinitionCatalogPersistence? catalogPersistence = null,
    IControlledTagValueStore? controlledValues = null,
    IControlledTagValueCatalogPersistence? controlledValuePersistence = null)
{
    public bool IsAvailable =>
        tagStore is not null
        && catalog is not null
        && catalogPersistence is not null;

    public async Task<WorkflowDefinitionTagSetView> GetAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        var definition = await GetDefinitionAsync(workflowDefinitionId, cancellationToken);
        var tagSet = await TagStore.GetAsync(workflowDefinitionId, cancellationToken);
        return ToView(tagSet, authorization.CanAssign && definition.DeletedAt is null);
    }

    public async Task<WorkflowDefinitionTagReplaceResult> ReplaceAsync(
        string workflowDefinitionId,
        string expectedRevision,
        IReadOnlyCollection<string> tagDefinitionIds,
        IReadOnlyCollection<WorkflowDefinitionControlledTagValue>? controlledTagValues = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        if (!authorization.CanAssign)
            throw new UnauthorizedAccessException("Workflow definition tag assignment permission is required.");
        var definition = await GetDefinitionAsync(workflowDefinitionId, cancellationToken);
        if (definition.DeletedAt is not null)
            throw new InvalidOperationException("Restore the workflow definition before changing its tags.");
        ArgumentNullException.ThrowIfNull(tagDefinitionIds);
        controlledTagValues ??= [];
        if (controlledTagValues.Count > 0
            && (controlledValues is null || controlledValuePersistence is null))
            throw new WorkflowDefinitionTaggingUnavailableException();
        if (tagDefinitionIds.Count > 64)
            throw new ArgumentOutOfRangeException(nameof(tagDefinitionIds), "At most 64 marker tags can be assigned to one workflow definition.");
        if (controlledTagValues.Count > 64)
            throw new ArgumentOutOfRangeException(nameof(controlledTagValues), "At most 64 controlled tag values can be assigned to one workflow definition.");

        var normalizedControlledValues = controlledTagValues
            .Select(value =>
            {
                value.Validate();
                return value;
            })
            .Distinct()
            .OrderBy(value => value.TagDefinitionId, StringComparer.Ordinal)
            .ThenBy(value => value.ControlledValueId, StringComparer.Ordinal)
            .ToArray();
        var duplicateDefinitions = normalizedControlledValues
            .GroupBy(value => value.TagDefinitionId, StringComparer.Ordinal)
            .Where(group => group.Select(value => value.ControlledValueId).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(group => group.Key)
            .ToArray();
        if (duplicateDefinitions.Length > 0)
            throw new ArgumentException(
                "A single-valued controlled tag can have only one manually asserted value.",
                nameof(controlledTagValues));

        var distinctIds = tagDefinitionIds
            .Select(id => id?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var before = await TagStore.GetAsync(workflowDefinitionId, cancellationToken);
        var beforeIds = before.Assertions
            .Select(assertion => assertion.TagDefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        var beforeManualMarkerIds = before.Assertions
            .Where(assertion =>
                StringComparer.Ordinal.Equals(
                    assertion.OriginKind,
                    WorkflowDefinitionTagOriginKinds.Manual)
                && assertion.ControlledValueId is null)
            .Select(assertion => assertion.TagDefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        var beforeManualControlledValues = before.Assertions
            .Where(assertion =>
                StringComparer.Ordinal.Equals(
                    assertion.OriginKind,
                    WorkflowDefinitionTagOriginKinds.Manual)
                && assertion.ControlledValueId is not null)
            .Select(assertion => new WorkflowDefinitionControlledTagValue(
                assertion.TagDefinitionId,
                assertion.ControlledValueId!))
            .ToHashSet();
        if (beforeManualControlledValues.Count > 0
            && (controlledValues is null || controlledValuePersistence is null))
            throw new WorkflowDefinitionTaggingUnavailableException();
        var requestedDefinitionIds = distinctIds
            .Concat(normalizedControlledValues.Select(value => value.TagDefinitionId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var catalogDefinitions = await Catalog.ListByIdsAsync(requestedDefinitionIds, cancellationToken);
        var catalogDefinitionsById = catalogDefinitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        foreach (var tagDefinitionId in distinctIds)
        {
            var definitionRecord = catalogDefinitionsById.GetValueOrDefault(tagDefinitionId)
                ?? throw new ArgumentException($"Tag definition '{tagDefinitionId}' was not found.", nameof(tagDefinitionIds));
            if (definitionRecord.Status != TagDefinitionStatus.Active
                && !beforeManualMarkerIds.Contains(tagDefinitionId))
                throw new ArgumentException($"Tag definition '{tagDefinitionId}' is retired.", nameof(tagDefinitionIds));
            if (!definitionRecord.Eligibility.HasFlag(TagDefinitionEligibility.WorkflowDefinition))
                throw new ArgumentException($"Tag definition '{tagDefinitionId}' cannot target workflow definitions.", nameof(tagDefinitionIds));
            if (definitionRecord.ValueMode != TagValueMode.Marker)
                throw new ArgumentException($"Tag definition '{tagDefinitionId}' requires a controlled value.", nameof(tagDefinitionIds));
        }

        foreach (var controlledValue in normalizedControlledValues)
        {
            var definitionRecord = catalogDefinitionsById.GetValueOrDefault(controlledValue.TagDefinitionId)
                ?? throw new ArgumentException($"Tag definition '{controlledValue.TagDefinitionId}' was not found.", nameof(controlledTagValues));
            if (definitionRecord.Status != TagDefinitionStatus.Active
                && !beforeManualControlledValues.Contains(controlledValue))
                throw new ArgumentException($"Tag definition '{controlledValue.TagDefinitionId}' is retired.", nameof(controlledTagValues));
            if (!definitionRecord.Eligibility.HasFlag(TagDefinitionEligibility.WorkflowDefinition)
                || definitionRecord.ValueMode != TagValueMode.Controlled
                || definitionRecord.Cardinality != TagCardinality.Single)
                throw new ArgumentException($"Tag definition '{controlledValue.TagDefinitionId}' does not accept a single controlled value.", nameof(controlledTagValues));

            var valueRecord = await ControlledValues.FindWithRevisionAsync(controlledValue.ControlledValueId, cancellationToken);
            var value = valueRecord?.Value;
            if (value is null
                || !StringComparer.Ordinal.Equals(value.TagDefinitionId, controlledValue.TagDefinitionId))
                throw new ArgumentException("The controlled tag value is unavailable for the requested definition.", nameof(controlledTagValues));
            var wasAlreadyAssigned = beforeManualControlledValues.Contains(controlledValue);
            if (value.Status != TagDefinitionStatus.Active && !wasAlreadyAssigned)
                throw new ArgumentException("The controlled tag value is retired.", nameof(controlledTagValues));
        }

        var result = await TagStore.ReplaceManualAsync(new(
            workflowDefinitionId,
            definition.TenantId,
            expectedRevision,
            distinctIds,
            authorization.ActorId,
            authorization.CorrelationId,
            ControlledValues: normalizedControlledValues), cancellationToken);
        if (result.Status != WorkflowDefinitionTagReplaceStatus.Saved || result.TagSet is null)
            return result;

        var afterIds = result.TagSet.Assertions.Select(x => x.TagDefinitionId).ToHashSet(StringComparer.Ordinal);
        var beforeControlledValueIds = before.Assertions
            .Select(assertion => assertion.ControlledValueId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var afterControlledValueIds = result.TagSet.Assertions
            .Select(assertion => assertion.ControlledValueId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        await eventPublisher.Publish(new WorkflowDefinitionTagsChanged(
            workflowDefinitionId,
            definition.TenantId,
            before.Revision,
            result.TagSet.Revision,
            afterIds.Except(beforeIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            beforeIds.Except(afterIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            WorkflowDefinitionTagOriginKinds.Manual,
            authorization.ActorId,
            authorization.CorrelationId,
            afterControlledValueIds.Except(beforeControlledValueIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            beforeControlledValueIds.Except(afterControlledValueIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()), cancellationToken);
        return result;
    }

    public static WorkflowDefinitionTagSetView ToView(WorkflowDefinitionTagSet tagSet, bool canAssign) =>
        new(
            tagSet.WorkflowDefinitionId,
            Quote(tagSet.Revision),
            tagSet.Assertions.Select(assertion => new WorkflowDefinitionTagAssertionView(
                assertion.TagDefinitionId,
                assertion.OriginKind,
                assertion.OriginKey,
                assertion.ControlledValueId)).ToArray(),
            canAssign);

    public static string Quote(string revision) => $"\"{revision}\"";

    public static string Unquote(string revision) =>
        revision.Length >= 2 && revision[0] == '"' && revision[^1] == '"'
            ? revision[1..^1]
            : throw new ArgumentException("A quoted If-Match revision is required.", nameof(revision));

    private IWorkflowDefinitionTagStore TagStore =>
        tagStore ?? throw new WorkflowDefinitionTaggingUnavailableException();

    private ITagDefinitionStore Catalog =>
        catalog ?? throw new WorkflowDefinitionTaggingUnavailableException();

    private IControlledTagValueStore ControlledValues =>
        controlledValues ?? throw new WorkflowDefinitionTaggingUnavailableException();

    private void EnsureAvailable()
    {
        if (!IsAvailable)
            throw new WorkflowDefinitionTaggingUnavailableException();
    }

    private async Task<WorkflowDefinition> GetDefinitionAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken) =>
        await definitionStore.FindByIdAsync(workflowDefinitionId, cancellationToken)
        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), workflowDefinitionId);
}

public sealed class WorkflowDefinitionTaggingUnavailableException()
    : ServiceUnavailableException("Workflow definition tagging is unavailable because durable tag persistence is not active.");

public sealed record WorkflowDefinitionTagsChanged(
    string WorkflowDefinitionId,
    string? TenantId,
    string PreviousRevision,
    string NewRevision,
    IReadOnlyCollection<string> AddedTagDefinitionIds,
    IReadOnlyCollection<string> RemovedTagDefinitionIds,
    string Origin,
    string ActorId,
    string CorrelationId,
    IReadOnlyCollection<string>? AddedControlledValueIds = null,
    IReadOnlyCollection<string>? RemovedControlledValueIds = null) : IEvent;
