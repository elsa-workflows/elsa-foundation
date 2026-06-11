using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Design.Reconciliation.Handlers;
using Elsa.Workflows.Design.Reconciliation.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation;

/// <summary>
/// Branch coverage per framework §2.23.2 for the universal reconciliation handler. Cases:
/// no sources, single source with multiple entries, multi-source aggregation, source with
/// no entries.
/// </summary>
public sealed class WorkflowVersionsReconcilingHandlerTests
{
    [Fact]
    public async Task Empty_sources_collection_contributes_nothing()
    {
        var handler = NewHandler();
        var evt = new OnWorkflowVersionsReconciling();

        await handler.Handle(evt, CancellationToken.None);

        Assert.Empty(evt.Versions);
    }

    [Fact]
    public async Task Source_with_no_entries_contributes_nothing()
    {
        var source = new StubSource("Test", "test-1", entries: []);
        var handler = NewHandler(source);
        var evt = new OnWorkflowVersionsReconciling();

        await handler.Handle(evt, CancellationToken.None);

        Assert.Empty(evt.Versions);
    }

    [Fact]
    public async Task Single_source_contributes_one_version_per_entry()
    {
        var entries = new[]
        {
            Entry("wf-a", "Workflow A", version: "1.0.0"),
            Entry("wf-b", "Workflow B", version: "2.0.0"),
        };
        var source = new StubSource("Json", "test-source", entries);
        var handler = NewHandler(source);
        var evt = new OnWorkflowVersionsReconciling();

        await handler.Handle(evt, CancellationToken.None);

        var versions = evt.Versions.ToList();
        Assert.Equal(2, versions.Count);
        Assert.Equal("wf-a", versions[0].Definition.Id);
        Assert.Equal("Workflow A", versions[0].Definition.Name);
        Assert.Equal("1.0.0", versions[0].Version);
        Assert.Equal("wf-b", versions[1].Definition.Id);
        Assert.Equal("2.0.0", versions[1].Version);
    }

    [Fact]
    public async Task Entry_without_DefinitionId_gets_a_freshly_minted_id()
    {
        var source = new StubSource("Json", "test", [Entry(null, "Anon", version: "1.0.0")]);
        var handler = NewHandler(source);
        var evt = new OnWorkflowVersionsReconciling();

        await handler.Handle(evt, CancellationToken.None);

        var contributed = Assert.Single(evt.Versions);
        Assert.False(string.IsNullOrWhiteSpace(contributed.Definition.Id));
        Assert.NotEqual(contributed.Id, contributed.Definition.Id);
    }

    [Fact]
    public async Task Multiple_sources_aggregate_their_entries()
    {
        var sourceA = new StubSource("Json", "a", [Entry("wf-a", "Workflow A", version: "1.0.0")]);
        var sourceB = new StubSource("Workflow", "b", [Entry("wf-b", "Workflow B", version: "1.0.0")]);
        var handler = NewHandler(sourceA, sourceB);
        var evt = new OnWorkflowVersionsReconciling();

        await handler.Handle(evt, CancellationToken.None);

        Assert.Equal(2, evt.Versions.Count);
        Assert.Contains(evt.Versions, v => v.Definition.Id == "wf-a");
        Assert.Contains(evt.Versions, v => v.Definition.Id == "wf-b");
    }

    private static WorkflowVersionsReconcilingHandler NewHandler(params IWorkflowReconciliationSource[] sources)
    {
        var idGenerator = new SequentialIdGenerator();
        return new WorkflowVersionsReconcilingHandler(
            new WorkflowDefinitionFactory(idGenerator),
            new WorkflowDefinitionVersionFactory(idGenerator),
            sources);
    }

    private static WorkflowVersionReconciliationModel Entry(string? definitionId, string name, string version) => new(
        DefinitionId: definitionId,
        Name: name,
        Description: null,
        Version: version,
        State: new WorkflowDefinitionState([], null, [], [], null, null)
    );

    private sealed class StubSource(string kind, string id, IEnumerable<WorkflowVersionReconciliationModel> entries) : IWorkflowReconciliationSource
    {
        public string SourceKind { get; } = kind;
        public string SourceId { get; } = id;
        private readonly IEnumerable<WorkflowVersionReconciliationModel> _entries = entries;
        public ValueTask<IEnumerable<WorkflowVersionReconciliationModel>> Read(CancellationToken cancellationToken)
            => ValueTask.FromResult(_entries);
    }

    private sealed class SequentialIdGenerator : IIdentityGenerator
    {
        private int _i;
        public string Generate() => $"gen-{Interlocked.Increment(ref _i)}";
    }
}
