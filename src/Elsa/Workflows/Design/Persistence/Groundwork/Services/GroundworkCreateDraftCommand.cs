using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkCreateDraftCommand(
    IIdentityGenerator identityGenerator,
    IDistributedLockProvider lockProvider,
    IDocumentStore store,
    GroundworkDesignAtomicWrite atomicWrite,
    IPayloadSerializer payloadSerializer,
    IInlineEventPublisher inlineEventPublisher,
    IDeferredEventPublisher deferredEventPublisher,
    ISystemClock clock,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : ICreateDraftCommand
{
    private const string OperationKind = "workflow.draft.create.v1";

    public async Task<string> Execute(
        DesignOperationKey operationKey,
        string workflowDefinitionId,
        WorkflowDefinitionState? initialState = null,
        IReadOnlyCollection<DesignMetadataRecord>? initialLayout = null,
        string? sourceVersionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        var draftId = identityGenerator.Generate();
        var state = initialState ?? EmptyState();
        var layout = initialLayout?.ToArray() ?? [];
        var requestMaterial = new CreateDraftRequestMaterial(
            workflowDefinitionId,
            GroundworkDesignSerialization.Execute(
                DesignPersistenceDomain.Workflow,
                OperationKind,
                "workflow definition draft",
                () => payloadSerializer.Serialize(state)),
            layout.Select(ToMaterial).ToArray(),
            sourceVersionId);
        var draft = new WorkflowDefinitionDraft
        {
            Id = draftId,
            WorkflowDefinitionId = workflowDefinitionId,
            SourceVersionId = sourceVersionId,
            State = state
        };

        var documents = DraftDocuments();
        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);
        GroundworkDesignAtomicCommandResult<CreateDraftResult> outcome;

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
                    // In-lock validation gate (see DraftValidationGate); errors are derived, never persisted.
                    var errors = await inlineEventPublisher.DeriveValidationErrorsAsync(draft, token);
                    GroundworkEntityTimestamps.StampAdded(draft, clock.UtcNow);
                    await context.SaveAsync(
                        documents.ToSaveRequest(draft, layout) with { ExpectedVersion = 0 },
                        token);
                    return new CreateDraftResult(draft, errors.ToArray());
                },
                GroundworkDesignDocumentSerialization.Create(payloadSerializer),
                cancellationToken);
        }

        if (outcome.ShouldPublishPostCommitOutcome)
        {
            var committed = outcome.Value;
            await deferredEventPublisher.Publish(
                new OnDraftCreated(
                    committed.Draft.Id,
                    committed.Draft.WorkflowDefinitionId,
                    committed.Draft.SourceVersionId),
                CancellationToken.None);
            await deferredEventPublisher.Publish(
                new OnDraftValidated(committed.Draft, committed.Errors),
                CancellationToken.None);
        }

        return outcome.Value.Draft.Id;
    }

    private GroundworkWorkflowDefinitionDraftDocumentStore DraftDocuments() =>
        new(store, GroundworkDesignDocumentSerialization.Create(payloadSerializer), accessContextAccessor);

    private static WorkflowDefinitionState EmptyState() => new(
        Variables: [],
        RootActivity: null,
        Inputs: [],
        Outputs: [],
        StrategyOptions: null);

    private static LayoutMaterial ToMaterial(DesignMetadataRecord record) =>
        new(
            record.NodeId,
            record.X,
            record.Y,
            record.Width,
            record.Height,
            record.AdditionalProperties?.GetRawText());

    private sealed record CreateDraftRequestMaterial(
        string WorkflowDefinitionId,
        string StateJson,
        IReadOnlyCollection<LayoutMaterial> Layout,
        string? SourceVersionId);

    private sealed record LayoutMaterial(
        string NodeId,
        double X,
        double Y,
        double? Width,
        double? Height,
        string? AdditionalPropertiesJson);

    private sealed record CreateDraftResult(
        WorkflowDefinitionDraft Draft,
        IReadOnlyList<ValidationError> Errors);
}
