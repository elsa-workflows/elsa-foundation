using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class AddDefinitionCommandHandlerTests
{
    [Fact]
    public async Task Creation_persists_the_supplied_authored_state_and_layout_without_requiring_a_root()
    {
        var identities = new SequentialIdentityGenerator();
        var persistence = new RecordingAddWorkflowDefinitionCommand();
        var handler = new AddDefinitionCommandHandler(
            new WorkflowDefinitionFactory(identities),
            new WorkflowDefinitionDraftFactory(identities),
            persistence);
        var layout = new WorkflowDefinitionLayoutRecordView("node-1", 10, 20, 100, 80, null);

        var result = await handler.Handle(
            new AddDefinition(
                "Rootless workflow",
                Description: null,
                InitialState: new WorkflowDefinitionStateView(RootActivity: null),
                Layout: [layout]),
            CancellationToken.None);

        Assert.NotNull(persistence.Draft);
        Assert.Null(persistence.Draft.State.RootActivity);
        Assert.Equal("Rootless workflow", persistence.Definition!.Name);
        var storedLayout = Assert.Single(persistence.Layout!);
        Assert.Equal(layout.NodeId, storedLayout.NodeId);
        Assert.Equal(layout.X, storedLayout.X);
        Assert.Equal(layout.Y, storedLayout.Y);
        Assert.Equal(persistence.Draft.Id, result.Draft!.Id);
        Assert.Equal(persistence.Draft.WorkflowDefinitionId, result.Draft.DefinitionId);
        Assert.Null(result.Draft.State.RootActivity);
        Assert.Equal(layout, Assert.Single(result.Draft.Layout));
    }

    [Fact]
    public async Task Creation_returns_the_persisted_folder_placement_without_adding_it_to_authored_state()
    {
        var identities = new SequentialIdentityGenerator();
        var persistence = new RecordingAddWorkflowDefinitionCommand();
        var handler = new AddDefinitionCommandHandler(
            new WorkflowDefinitionFactory(identities),
            new WorkflowDefinitionDraftFactory(identities),
            persistence);

        var result = await handler.Handle(
            new AddDefinition("Filed workflow", null, FolderId: "folder-1"),
            CancellationToken.None);

        Assert.Equal("folder-1", persistence.Definition!.FolderId);
        Assert.Equal("folder-1", result.Definition.FolderId);
    }

    [Fact]
    public void Authored_and_version_projection_does_not_expose_current_folder_placement()
    {
        IWorkflowDefinition authoredOrVersionDefinition = new WorkflowDefinition
        {
            Id = "definition-1",
            Name = "Historical",
            FolderId = "current-folder"
        };

        Assert.Null(authoredOrVersionDefinition.ToView().FolderId);
    }

    private sealed class RecordingAddWorkflowDefinitionCommand : IAddWorkflowDefinitionCommand
    {
        public WorkflowDefinition? Definition { get; private set; }
        public WorkflowDefinitionDraft? Draft { get; private set; }
        public IReadOnlyCollection<DesignMetadataRecord>? Layout { get; private set; }

        public Task Execute(WorkflowDefinition workflowDefinition, WorkflowDefinitionDraft draft, CancellationToken cancellation) =>
            throw new InvalidOperationException("The API must use the layout-aware creation operation.");

        public Task Execute(
            WorkflowDefinition workflowDefinition,
            WorkflowDefinitionDraft draft,
            IReadOnlyCollection<DesignMetadataRecord> layout,
            CancellationToken cancellation)
        {
            Definition = workflowDefinition;
            Draft = draft;
            Layout = layout;
            return Task.CompletedTask;
        }
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _next;
        public string Generate() => $"id-{++_next}";
    }
}
