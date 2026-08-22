using System.Text.Json;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Api;

public sealed class WorkflowDefinitionVersionDetailsTests
{
    private const string DefinitionId = "wf-definition-1";
    private const string VersionId = "wf-version-1";

    private readonly WorkflowDefinitionVersion _version = new(DefinitionId, "1.0.0")
    {
        Id = VersionId,
        Definition = new WorkflowDefinition
        {
            Id = DefinitionId,
            Name = "Order Fulfillment",
            Description = "Routes orders through fulfillment."
        },
        State = new WorkflowDefinitionState([], null, [], [], null)
    };

    [Fact]
    public async Task Details_include_version_layout_records()
    {
        var layout = new WorkflowDefinitionVersionLayout
        {
            Id = "layout-1",
            WorkflowDefinitionVersionId = VersionId,
            Records =
            [
                new DesignMetadataRecord(
                    "node-1",
                    120,
                    240,
                    180,
                    96,
                    JsonSerializer.Deserialize<JsonElement>("""{"label":"Start"}"""))
            ]
        };
        var handler = NewHandler(layout);

        var result = await handler.Handle(new GetVersion(VersionId), CancellationToken.None);

        var record = Assert.Single(result.Layout);
        Assert.Equal("node-1", record.NodeId);
        Assert.Equal(120, record.X);
        Assert.Equal(240, record.Y);
        Assert.Equal(180, record.Width);
        Assert.Equal(96, record.Height);
        Assert.Equal("Start", record.AdditionalProperties?.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Details_use_empty_layout_when_no_layout_was_saved()
    {
        var handler = NewHandler(layout: null);

        var result = await handler.Handle(new GetVersion(VersionId), CancellationToken.None);

        Assert.Empty(result.Layout);
    }

    [Fact]
    public async Task Synthetic_draft_version_ids_are_rejected_before_store_access()
    {
        var store = new StubVersionStore(_version);
        var layout = new StubLayoutStore(layout: null);

        await Assert.ThrowsAsync<ArgumentException>(() => new GetVersionRequestHandler(store, layout).Handle(
            new GetVersion("draft:synthetic"),
            CancellationToken.None));

        Assert.Equal(0, store.GetWithDefinitionCalls);
        Assert.Equal(0, layout.Calls);
    }

    private GetVersionRequestHandler NewHandler(WorkflowDefinitionVersionLayout? layout) =>
        new(new StubVersionStore(_version), new StubLayoutStore(layout));

    private sealed class StubVersionStore(WorkflowDefinitionVersion version) : IWorkflowDefinitionVersionStore
    {
        public int GetWithDefinitionCalls { get; private set; }

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(version);

        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersion?>(version);

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            CountAndReturn();

        private Task<WorkflowDefinitionVersion> CountAndReturn()
        {
            GetWithDefinitionCalls++;
            return Task.FromResult(version);
        }

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersion?>(version);

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>([version]);

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class StubLayoutStore(WorkflowDefinitionVersionLayout? layout) : IWorkflowDefinitionVersionLayoutStore
    {
        public int Calls { get; private set; }

        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default) =>
            CountAndReturn();

        private Task<WorkflowDefinitionVersionLayout?> CountAndReturn()
        {
            Calls++;
            return Task.FromResult(layout);
        }
    }
}
