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
    IWorkflowDefinitionTagStore tagStore,
    ITagDefinitionStore catalog,
    IWorkflowDefinitionTagAuthorizationContext authorization,
    IDeferredEventPublisher eventPublisher)
{
    public async Task<WorkflowDefinitionTagSetView> GetAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var definition = await GetDefinitionAsync(workflowDefinitionId, cancellationToken);
        var tagSet = await tagStore.GetAsync(workflowDefinitionId, cancellationToken);
        return ToView(tagSet, authorization.CanAssign && definition.DeletedAt is null);
    }

    public async Task<WorkflowDefinitionTagReplaceResult> ReplaceAsync(
        string workflowDefinitionId,
        string expectedRevision,
        IReadOnlyCollection<string> tagDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.CanAssign)
            throw new UnauthorizedAccessException("Workflow definition tag assignment permission is required.");
        var definition = await GetDefinitionAsync(workflowDefinitionId, cancellationToken);
        if (definition.DeletedAt is not null)
            throw new InvalidOperationException("Restore the workflow definition before changing its tags.");
        ArgumentNullException.ThrowIfNull(tagDefinitionIds);
        if (tagDefinitionIds.Count > 64)
            throw new ArgumentOutOfRangeException(nameof(tagDefinitionIds), "At most 64 marker tags can be assigned to one workflow definition.");

        var distinctIds = tagDefinitionIds
            .Select(id => id?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var tagDefinitionId in distinctIds)
        {
            var definitionRecord = await catalog.FindWithRevisionAsync(tagDefinitionId, cancellationToken)
                ?? throw new ArgumentException($"Tag definition '{tagDefinitionId}' was not found.", nameof(tagDefinitionIds));
            if (definitionRecord.Definition.Status != TagDefinitionStatus.Active)
                throw new ArgumentException($"Tag definition '{tagDefinitionId}' is retired.", nameof(tagDefinitionIds));
            if (!definitionRecord.Definition.Eligibility.HasFlag(TagDefinitionEligibility.WorkflowDefinition))
                throw new ArgumentException($"Tag definition '{tagDefinitionId}' cannot target workflow definitions.", nameof(tagDefinitionIds));
        }

        var before = await tagStore.GetAsync(workflowDefinitionId, cancellationToken);
        var result = await tagStore.ReplaceManualAsync(new(
            workflowDefinitionId,
            definition.TenantId,
            expectedRevision,
            distinctIds,
            authorization.ActorId,
            authorization.CorrelationId), cancellationToken);
        if (result.Status != WorkflowDefinitionTagReplaceStatus.Saved || result.TagSet is null)
            return result;

        var beforeIds = before.Assertions.Select(x => x.TagDefinitionId).ToHashSet(StringComparer.Ordinal);
        var afterIds = result.TagSet.Assertions.Select(x => x.TagDefinitionId).ToHashSet(StringComparer.Ordinal);
        await eventPublisher.Publish(new WorkflowDefinitionTagsChanged(
            workflowDefinitionId,
            definition.TenantId,
            before.Revision,
            result.TagSet.Revision,
            afterIds.Except(beforeIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            beforeIds.Except(afterIds, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            WorkflowDefinitionTagOriginKinds.Manual,
            authorization.ActorId,
            authorization.CorrelationId), cancellationToken);
        return result;
    }

    public static WorkflowDefinitionTagSetView ToView(WorkflowDefinitionTagSet tagSet, bool canAssign) =>
        new(
            tagSet.WorkflowDefinitionId,
            Quote(tagSet.Revision),
            tagSet.Assertions.Select(assertion => new WorkflowDefinitionTagAssertionView(
                assertion.TagDefinitionId,
                assertion.OriginKind,
                assertion.OriginKey)).ToArray(),
            canAssign);

    public static string Quote(string revision) => $"\"{revision}\"";

    public static string Unquote(string revision) =>
        revision.Length >= 2 && revision[0] == '"' && revision[^1] == '"'
            ? revision[1..^1]
            : throw new ArgumentException("A quoted If-Match revision is required.", nameof(revision));

    private async Task<WorkflowDefinition> GetDefinitionAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken) =>
        await definitionStore.FindByIdAsync(workflowDefinitionId, cancellationToken)
        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), workflowDefinitionId);
}

public sealed record WorkflowDefinitionTagsChanged(
    string WorkflowDefinitionId,
    string? TenantId,
    string PreviousRevision,
    string NewRevision,
    IReadOnlyCollection<string> AddedTagDefinitionIds,
    IReadOnlyCollection<string> RemovedTagDefinitionIds,
    string Origin,
    string ActorId,
    string CorrelationId) : IEvent;
