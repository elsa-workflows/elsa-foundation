using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class WorkflowLifecycleHandlerTests
{
    [Fact]
    public async Task Draft_get_and_replace_use_the_first_class_draft_id_and_preserve_omitted_layout()
    {
        var layout = new[] { new DesignMetadataRecord("node-1", 10, 20) };
        var drafts = new MutableDraftStore(new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = "definition-1",
            State = WorkflowDefinitionState.Empty
        }, layout);
        var update = new RecordingUpdateDraftCommand(drafts);
        var desired = new WorkflowDefinitionStateView(
            RootActivity: new ActivityNode("root", "activity-version-1", [], []));

        var replaced = await new ReplaceDraftCommandHandler(drafts, update).Handle(
            new ReplaceDraft("replace-1", "draft-1", desired, Layout: null),
            CancellationToken.None);
        var read = await new GetDraftRequestHandler(drafts).Handle(new GetDraft("draft-1"), CancellationToken.None);

        Assert.Equal("draft-1", update.Request!.DraftId);
        Assert.Equal(layout, update.Request.Layout);
        Assert.Equal("root", replaced.State.RootActivity!.NodeId);
        Assert.Equal("root", read.State.RootActivity!.NodeId);
        Assert.Equal("node-1", Assert.Single(read.Layout).NodeId);
        Assert.Equal(new DesignOperationKey("replace-1"), update.OperationKey);
    }

    [Fact]
    public async Task Definition_must_pass_through_soft_delete_before_permanent_delete_and_restore_clears_lifecycle_facts()
    {
        var definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" };
        var definitions = new MutableDefinitionStore(definition);
        var save = new RecordingSaveDefinitionCommand();
        var permanent = new RecordingPermanentDeleteCommand(definitions);
        var permanentHandler = new DeleteDefinitionPermanentlyCommandHandler(permanent);

        await Assert.ThrowsAsync<InvalidOperationException>(() => permanentHandler.Handle(
            new DeleteDefinitionPermanently("delete-before-soft-delete", definition.Id),
            CancellationToken.None));

        await new SoftDeleteDefinitionCommandHandler(definitions, save, new FixedTimeProvider()).Handle(
            new SoftDeleteDefinition("soft-delete-1", definition.Id, "cleanup"),
            CancellationToken.None);
        Assert.Equal(DateTimeOffset.UnixEpoch, definition.DeletedAt);
        Assert.Equal("cleanup", definition.DeletedReason);

        await new RestoreDefinitionCommandHandler(definitions, save).Handle(
            new RestoreDefinition("restore-1", definition.Id),
            CancellationToken.None);
        Assert.Null(definition.DeletedAt);
        Assert.Null(definition.DeletedReason);

        await new SoftDeleteDefinitionCommandHandler(definitions, save, new FixedTimeProvider()).Handle(
            new SoftDeleteDefinition("soft-delete-2", definition.Id),
            CancellationToken.None);
        await permanentHandler.Handle(
            new DeleteDefinitionPermanently("permanent-delete-1", definition.Id),
            CancellationToken.None);
        Assert.Equal(definition.Id, permanent.DefinitionId);
        Assert.Equal(new DesignOperationKey("permanent-delete-1"), permanent.OperationKey);
    }

    private sealed class MutableDraftStore(WorkflowDefinitionDraft draft, IReadOnlyCollection<DesignMetadataRecord> layout)
        : IWorkflowDefinitionDraftStore
    {
        public WorkflowDefinitionDraft Draft { get; } = draft;
        public IReadOnlyCollection<DesignMetadataRecord> Layout { get; set; } = layout;
        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinitionDraft?>(Draft);
        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinitionDraft?>(Draft);
        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowDefinitionDraft>>([Draft]);
        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult(Layout);
        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult<DraftWithLayout?>(new(Draft, Layout));
    }

    private sealed class RecordingUpdateDraftCommand(MutableDraftStore store) : IUpdateDraftCommand
    {
        public UpdateDraftRequest? Request { get; private set; }
        public DesignOperationKey? OperationKey { get; private set; }

        public Task Execute(
            DesignOperationKey operationKey,
            UpdateDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            OperationKey = operationKey;
            Request = request;
            store.Draft.State = request.State;
            store.Layout = request.Layout;
            return Task.CompletedTask;
        }
    }

    private sealed class MutableDefinitionStore(WorkflowDefinition definition) : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(definition);
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinition?>(definition);
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowDefinition>>([definition]);
    }

    private sealed class RecordingSaveDefinitionCommand : ISaveWorkflowDefinitionCommand
    {
        public Task Execute(
            DesignOperationKey operationKey,
            WorkflowDefinition definition,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingPermanentDeleteCommand(MutableDefinitionStore definitions)
        : IDeleteWorkflowDefinitionPermanentlyCommand
    {
        public string? DefinitionId { get; private set; }
        public DesignOperationKey? OperationKey { get; private set; }

        public async Task Execute(
            DesignOperationKey operationKey,
            string definitionId,
            CancellationToken cancellationToken = default)
        {
            var definition = await definitions.GetAsync(definitionId, cancellationToken);
            if (definition.DeletedAt is null)
                throw new InvalidOperationException(
                    "A workflow definition must be soft-deleted before permanent deletion.");

            OperationKey = operationKey;
            DefinitionId = definitionId;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }
}
