using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Xunit;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Update;
using UpdateHandler = Elsa.Workflows.Design.Api.Endpoints.Definitions.Update.Handler;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Get;
using Elsa.Workflows.Design.Api.Endpoints.Definitions;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>
/// Behavioral tests for <see cref="UpdateHandler"/> — the DS-6 Draft mutation gate. They
/// prove the handler resolves the owning Draft, forwards the complete desired state to the single coarse
/// <see cref="IUpdateDraftCommand"/> under the resolved DraftId, preserves stored layout when the caller omits
/// it, and faults when no Draft exists. The endpoint that hosts this handler is permission-guarded by
/// construction (see <see cref="DefinitionEndpointSecurityTests"/>).
/// </summary>
public sealed class UpdateDefinitionCommandHandlerTests
{
    [Fact]
    public async Task Forwards_desired_state_to_update_draft_command_under_resolved_draft_id()
    {
        var draftStore = new StubDraftStore(new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = "def-1",
        });
        var updateCommand = new RecordingUpdateDraftCommand();
        var sender = new StubDetailsReader(DetailsFor("def-1"));
        var handler = new UpdateHandler(draftStore, updateCommand, sender);

        var state = new WorkflowDefinitionStateView();
        var result = await handler.Handle(new UpdateDefinition("update-1", "def-1", state), CancellationToken.None);

        Assert.NotNull(updateCommand.LastRequest);
        Assert.Equal("draft-1", updateCommand.LastRequest!.DraftId);
        Assert.Equal("def-1", result.Definition.Id);
        Assert.Equal(new DesignOperationKey("update-1"), updateCommand.OperationKey);
    }

    [Fact]
    public async Task Omitted_layout_preserves_the_stored_layout()
    {
        var storedLayout = new DesignMetadataRecord[] { new("node-1", 10, 20) };
        var draftStore = new StubDraftStore(
            new WorkflowDefinitionDraft { Id = "draft-1", WorkflowDefinitionId = "def-1" },
            storedLayout);
        var updateCommand = new RecordingUpdateDraftCommand();
        var handler = new UpdateHandler(draftStore, updateCommand, new StubDetailsReader(DetailsFor("def-1")));

        await handler.Handle(new UpdateDefinition("update-2", "def-1", new WorkflowDefinitionStateView(), Layout: null), CancellationToken.None);

        Assert.Same(storedLayout, updateCommand.LastRequest!.Layout);
    }

    [Fact]
    public async Task Provided_layout_overrides_the_stored_layout()
    {
        var draftStore = new StubDraftStore(
            new WorkflowDefinitionDraft { Id = "draft-1", WorkflowDefinitionId = "def-1" },
            new DesignMetadataRecord[] { new("stored", 0, 0) });
        var updateCommand = new RecordingUpdateDraftCommand();
        var handler = new UpdateHandler(draftStore, updateCommand, new StubDetailsReader(DetailsFor("def-1")));

        var layout = new WorkflowDefinitionLayoutRecordView[] { new("incoming", 5, 6, null, null, null) };
        await handler.Handle(new UpdateDefinition("update-3", "def-1", new WorkflowDefinitionStateView(), layout), CancellationToken.None);

        var forwarded = Assert.Single(updateCommand.LastRequest!.Layout);
        Assert.Equal("incoming", forwarded.NodeId);
        Assert.False(draftStore.LayoutWasRead);
    }

    [Fact]
    public async Task Missing_draft_throws()
    {
        var handler = new UpdateHandler(
            new StubDraftStore(draft: null),
            new RecordingUpdateDraftCommand(),
            new StubDetailsReader(DetailsFor("def-1")));

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.Handle(new UpdateDefinition("update-missing", "missing", new WorkflowDefinitionStateView()), CancellationToken.None));
    }

    private static WorkflowDefinitionDetailsView DetailsFor(string id) =>
        new(new WorkflowDefinitionView(id, "name", null, default, default), null, []);

    private sealed class StubDraftStore(WorkflowDefinitionDraft? draft, IReadOnlyCollection<DesignMetadataRecord>? layout = null)
        : IWorkflowDefinitionDraftStore
    {
        public bool LayoutWasRead { get; private set; }

        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(draft);

        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default)
        {
            LayoutWasRead = true;
            return Task.FromResult(layout ?? []);
        }

        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(draft);

        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionDraft>>(draft is null ? [] : [draft]);

        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default)
        {
            LayoutWasRead = true;
            return Task.FromResult<DraftWithLayout?>(draft is null ? null : new DraftWithLayout(draft, layout ?? []));
        }
    }

    private sealed class RecordingUpdateDraftCommand : IUpdateDraftCommand
    {
        public UpdateDraftRequest? LastRequest { get; private set; }
        public DesignOperationKey? OperationKey { get; private set; }

        public Task Execute(
            DesignOperationKey operationKey,
            UpdateDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            OperationKey = operationKey;
            LastRequest = request;
            return Task.CompletedTask;
        }
    }

    private sealed class StubDetailsReader(WorkflowDefinitionDetailsView? details = null) : IWorkflowDefinitionDetailsReader
    {
        public Task<WorkflowDefinitionDetailsView> ReadAsync(string definitionId, CancellationToken cancellationToken) =>
            Task.FromResult(details ?? throw new InvalidOperationException("This test did not expect the details view to be read."));
    }
}
