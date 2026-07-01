using Elsa.Activities.Composition.Design.Reconciliation;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Composition.Tests;

/// <summary>
/// Behavioural tests for the Workflow-kind reconciliation source (T028). Proves the discovery filter
/// (usable-as-activity only), the emitted Workflow descriptor, the stable activity-type key, and the
/// direct I/O mirror onto the catalog row.
/// </summary>
public sealed class WorkflowActivityReconciliationSourceTests
{
    private const string DefinitionId = "def-1";

    [Fact]
    public async Task Read_UsableAsActivityVersion_EmitsWorkflowIdentityRowWithMirroredIo()
    {
        var inputs = new List<InputDefinition>();
        var outputs = new List<OutputDefinition>();
        var version = NewVersion("v1", "1.0.0", usableAsActivity: true, category: "Reusable", inputs: inputs, outputs: outputs);
        var source = NewSource(NewDefinition("Approve Order", "Approval subflow"), version);

        var model = Assert.Single(await source.Read(default));

        Assert.Equal(typeof(WorkflowIdentity).FullName, model.DescriptorType);
        Assert.Equal(new WorkflowIdentity(DefinitionId, "v1", "1.0.0"), Assert.IsType<WorkflowIdentity>(model.Descriptor));
        Assert.Equal(DefinitionId, model.ActivityTypeKey);
        Assert.Equal("1.0.0", model.Version);
        Assert.Equal("Approve Order", model.DisplayName);
        Assert.Equal("Reusable", model.Category);
        Assert.Equal("Approval subflow", model.Description);
        // I/O is mirrored straight off the authored state — same collection, not a copy.
        Assert.Same(inputs, model.Inputs);
        Assert.Same(outputs, model.Outputs);
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
    public async Task Read_MultipleUsableVersions_EmitOneRowEachSharingTheActivityTypeKey()
    {
        var source = NewSource(NewDefinition("Approve Order", null),
            NewVersion("v1", "1.0.0", usableAsActivity: true),
            NewVersion("v2", "2.0.0", usableAsActivity: true));

        var models = (await source.Read(default)).ToList();

        Assert.Equal(2, models.Count);
        Assert.All(models, m => Assert.Equal(DefinitionId, m.ActivityTypeKey));
        Assert.Equal(["1.0.0", "2.0.0"], models.Select(m => m.Version).OrderBy(v => v));
        Assert.Equal(["v1", "v2"], models.Select(m => ((WorkflowIdentity)m.Descriptor).VersionId).OrderBy(v => v));
    }

    private static WorkflowActivityReconciliationSource NewSource(WorkflowDefinition definition, params WorkflowDefinitionVersion[] versions) =>
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
