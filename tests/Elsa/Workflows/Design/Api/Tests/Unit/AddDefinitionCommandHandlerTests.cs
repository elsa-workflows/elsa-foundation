using Elsa.Primitives.Contracts;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Xunit;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Add;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class AddDefinitionCommandHandlerTests
{
    [Fact]
    public async Task Creation_persists_the_supplied_authored_state_and_layout_without_requiring_a_root()
    {
        var identities = new SequentialIdentityGenerator();
        var persistence = new RecordingAddWorkflowDefinitionCommand();
        var handler = new Endpoint(
            new WorkflowDefinitionFactory(identities),
            new WorkflowDefinitionDraftFactory(identities),
            persistence);
        var layout = new WorkflowDefinitionLayoutRecordView("node-1", 10, 20, 100, 80, null);

        var result = await handler.HandleAsync(
            new AddDefinition(
                "create-request-1",
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
        Assert.Equal(new DesignOperationKey("create-request-1"), persistence.OperationKey);
    }

    private sealed class RecordingAddWorkflowDefinitionCommand : IAddWorkflowDefinitionCommand
    {
        public WorkflowDefinition? Definition { get; private set; }
        public WorkflowDefinitionDraft? Draft { get; private set; }
        public IReadOnlyCollection<DesignMetadataRecord>? Layout { get; private set; }

        public DesignOperationKey? OperationKey { get; private set; }

        public Task<WorkflowDefinitionCreated> Execute(
            DesignOperationKey operationKey,
            WorkflowDefinition workflowDefinition,
            WorkflowDefinitionDraft draft,
            CancellationToken cancellation) =>
            throw new InvalidOperationException("The API must use the layout-aware creation operation.");

        public Task<WorkflowDefinitionCreated> Execute(
            DesignOperationKey operationKey,
            WorkflowDefinition workflowDefinition,
            WorkflowDefinitionDraft draft,
            IReadOnlyCollection<DesignMetadataRecord> layout,
            CancellationToken cancellation)
        {
            OperationKey = operationKey;
            Definition = workflowDefinition;
            Draft = draft;
            Layout = layout;
            return Task.FromResult(new WorkflowDefinitionCreated(workflowDefinition.Id, draft.Id));
        }
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _next;
        public string Generate() => $"id-{++_next}";
    }
}
