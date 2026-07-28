using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkUpdateDraftCommand(
    IDistributedLockProvider lockProvider,
    IDocumentStore store,
    IDesignAtomicWriter atomicWrite,
    IPayloadSerializer payloadSerializer,
    IInlineEventPublisher inlineEventPublisher,
    IDeferredEventPublisher deferredEventPublisher,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IActivityStructureService activityStructureService)
    : IUpdateDraftCommand
{
    private const string OperationKind = "workflow.draft.replace.v1";

    public async Task Execute(
        DesignOperationKey operationKey,
        UpdateDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentNullException.ThrowIfNull(request);
        var reachableNodeIds = CollectNodeIds(request.State.RootActivity);
        var activityPresentation =
            ActivityPresentationRecord.NormalizeCollection(request.ActivityPresentation)
                .Where(record => reachableNodeIds.Contains(record.NodeId))
                .ToArray();
        var requestMaterial = new UpdateDraftRequestMaterial(
            request.DraftId,
            GroundworkDesignSerialization.Execute(
                DesignPersistenceDomain.Workflow,
                OperationKind,
                "workflow definition draft",
                () => payloadSerializer.Serialize(request.State)),
            request.Layout.Select(ToMaterial).ToArray(),
            activityPresentation.Select(ToMaterial).ToArray());
        var documents = DraftDocuments();
        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(request.DraftId);
        GroundworkDesignAtomicCommandResult<UpdateDraftResult> outcome;

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        {
            outcome = await GroundworkDesignAtomicCommand.ExecuteAsync(
                atomicWrite,
                operationKey,
                OperationKind,
                requestMaterial,
                [WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind],
                async (context, token) =>
                {
                    var document = await documents.FindByIdAsync(request.DraftId, token)
                                   ?? throw new InvalidOperationException(
                                       $"Workflow definition draft '{request.DraftId}' not found");
                    var draft = document.Entity;
                    // Wholesale assign the desired state (last-writer-wins, FR-022).
                    draft.State = request.State;
                    // In-lock validation gate (see DraftValidationGate); errors are derived, never persisted.
                    var errors = await inlineEventPublisher.DeriveValidationErrorsAsync(draft, token);
                    GroundworkEntityTimestamps.StampModified(draft, clock.UtcNow);
                    await context.SaveAsync(
                        documents.ToSaveRequest(
                            draft,
                            request.Layout.ToArray(),
                            activityPresentation),
                        token);
                    return new UpdateDraftResult(draft, errors.ToArray());
                },
                GroundworkDesignDocumentSerialization.Create(payloadSerializer),
                cancellationToken);
        }

        if (outcome.ShouldPublishPostCommitOutcome)
        {
            await deferredEventPublisher.Publish(
                new OnDraftValidated(outcome.Value.Draft, outcome.Value.Errors),
                CancellationToken.None);
        }
    }

    private GroundworkWorkflowDefinitionDraftDocumentStore DraftDocuments() =>
        new(store, GroundworkDesignDocumentSerialization.Create(payloadSerializer), accessContextAccessor);

    private HashSet<string> CollectNodeIds(ActivityNode? rootActivity)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        if (rootActivity is null)
            return nodeIds;

        var stack = new Stack<ActivityNode>();
        stack.Push(rootActivity);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!nodeIds.Add(node.NodeId))
                continue;

            foreach (var child in activityStructureService
                         .ProjectChildren(node)
                         .SelectMany(slot => slot.Activities))
                stack.Push(child);
        }

        return nodeIds;
    }

    private static LayoutMaterial ToMaterial(DesignMetadataRecord record) =>
        new(
            record.NodeId,
            record.X,
            record.Y,
            record.Width,
            record.Height,
            record.AdditionalProperties?.GetRawText());

    private sealed record UpdateDraftRequestMaterial(
        string DraftId,
        string StateJson,
        IReadOnlyCollection<LayoutMaterial> Layout,
        IReadOnlyCollection<ActivityPresentationMaterial> ActivityPresentation);

    private static ActivityPresentationMaterial ToMaterial(ActivityPresentationRecord record) =>
        new(record.NodeId, record.DisplayName, record.Description);

    private sealed record ActivityPresentationMaterial(
        string NodeId,
        string? DisplayName,
        string? Description);

    private sealed record LayoutMaterial(
        string NodeId,
        double X,
        double Y,
        double? Width,
        double? Height,
        string? AdditionalPropertiesJson);

    private sealed record UpdateDraftResult(
        WorkflowDefinitionDraft Draft,
        IReadOnlyList<ValidationError> Errors);
}
