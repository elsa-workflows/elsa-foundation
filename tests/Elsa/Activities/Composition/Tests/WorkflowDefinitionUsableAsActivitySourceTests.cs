using Elsa.Activities.Composition.Design.Reconciliation;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Activities.Composition.Tests;

/// <summary>
/// The adapter is the single class that reaches into the Workflows Design read ports (§2.7). These tests
/// pin the discovery contract: the usable-as-activity filter, and the projection of identity, category,
/// and I/O off the authored workflow state. Uses store stubs — the reconciliation source above needs none.
/// </summary>
public sealed class WorkflowDefinitionUsableAsActivitySourceTests
{
    private const string DefinitionId = "def-1";

    [Fact]
    public async Task Read_UsableVersion_ProjectsIdentityCategoryAndMirrorsIo()
    {
        var inputs = new List<InputDefinition>();
        var outputs = new List<OutputDefinition>();
        var source = NewSource(NewDefinition("Approve Order", "Approval subflow"),
            NewVersion("v1", "1.0.0", usableAsActivity: true, category: "Reusable", inputs: inputs, outputs: outputs));

        var workflow = Assert.Single(await source.Read(default));

        Assert.Equal(DefinitionId, workflow.DefinitionId);
        Assert.Equal("v1", workflow.VersionId);
        Assert.Equal("1.0.0", workflow.Version);
        Assert.Equal("Approve Order", workflow.Name);
        Assert.Equal("Approval subflow", workflow.Description);
        Assert.Equal("Reusable", workflow.Category);
        Assert.Same(inputs, workflow.Inputs);
        Assert.Same(outputs, workflow.Outputs);
    }

    [Fact]
    public async Task Read_VersionNotUsableAsActivity_IsSkipped()
    {
        var source = NewSource(NewDefinition("Internal Only", null),
            NewVersion("v1", "1.0.0", usableAsActivity: false),
            NewVersion("v2", "2.0.0", usableAsActivity: null));

        Assert.Empty(await source.Read(default));
    }

    [Fact]
    public async Task Read_MultipleUsableVersions_YieldOnePerVersion()
    {
        var source = NewSource(NewDefinition("Approve Order", null),
            NewVersion("v1", "1.0.0", usableAsActivity: true),
            NewVersion("v2", "2.0.0", usableAsActivity: true));

        var results = (await source.Read(default)).ToList();

        Assert.Equal(["v1", "v2"], results.Select(w => w.VersionId).OrderBy(v => v));
    }

    private static WorkflowDefinitionUsableAsActivitySource NewSource(WorkflowDefinition definition, params WorkflowDefinitionVersion[] versions) =>
        new(new StubDefinitionStore(definition), new StubVersionStore(versions));

    private static WorkflowDefinition NewDefinition(string name, string? description) =>
        new() { Id = DefinitionId, Name = name, Description = description };

    private static WorkflowDefinitionVersion NewVersion(
        string id,
        string version,
        bool? usableAsActivity,
        string? category = null,
        IEnumerable<InputDefinition>? inputs = null,
        IEnumerable<OutputDefinition>? outputs = null) =>
        new(DefinitionId, version)
        {
            Id = id,
            State = new WorkflowDefinitionState(
                Variables: [],
                RootActivity: null,
                Inputs: inputs ?? [],
                Outputs: outputs ?? [],
                WorkflowActivityOptions: new WorkflowActivityOptions(usableAsActivity, null, category),
                StrategyOptions: null),
        };

    private sealed class StubDefinitionStore(WorkflowDefinition definition) : IWorkflowDefinitionStore
    {
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinition>>([definition]);

        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class StubVersionStore(WorkflowDefinitionVersion[] versions) : IWorkflowDefinitionVersionStore
    {
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(versions);

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
