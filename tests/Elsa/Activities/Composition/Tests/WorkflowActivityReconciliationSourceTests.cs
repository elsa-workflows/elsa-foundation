using Elsa.Activities.Composition.Design.Reconciliation;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Composition.Tests;

/// <summary>
/// The reconciliation source is a pure mapper over <see cref="IUsableAsActivityWorkflowSource"/> — no
/// store dependency (that lives behind the port; see
/// <see cref="WorkflowDefinitionUsableAsActivitySourceTests"/>). These tests pin the mapping: the emitted
/// Workflow descriptor, the stable activity-type key, and the direct I/O mirror onto the catalog row.
/// </summary>
public sealed class WorkflowActivityReconciliationSourceTests
{
    [Fact]
    public async Task Read_MapsUsableWorkflowToWorkflowIdentityRowWithMirroredIo()
    {
        var inputs = new List<InputDefinition>();
        var outputs = new List<OutputDefinition>();
        var source = NewSource(new UsableAsActivityWorkflow(
            "def-1", "v1", "1.0.0", "Approve Order", "Approval subflow", "Reusable", inputs, outputs));

        var model = Assert.Single(await source.Read(default));

        Assert.Equal(typeof(WorkflowIdentity).FullName, model.DescriptorType);
        Assert.Equal(new WorkflowIdentity("def-1", "v1", "1.0.0"), Assert.IsType<WorkflowIdentity>(model.Descriptor));
        Assert.Equal("def-1", model.ActivityTypeKey);
        Assert.Equal("1.0.0", model.Version);
        Assert.Equal("Approve Order", model.DisplayName);
        Assert.Equal("Reusable", model.Category);
        Assert.Equal("Approval subflow", model.Description);
        // I/O is mirrored straight through — same collection, not a copy.
        Assert.Same(inputs, model.Inputs);
        Assert.Same(outputs, model.Outputs);
    }

    [Fact]
    public async Task Read_NoUsableWorkflows_YieldsNoRows()
    {
        Assert.Empty(await NewSource().Read(default));
    }

    [Fact]
    public async Task Read_MultipleWorkflows_MapEachRowSharingTheDefinitionActivityTypeKey()
    {
        var source = NewSource(
            new UsableAsActivityWorkflow("def-1", "v1", "1.0.0", "Approve Order", null, null, [], []),
            new UsableAsActivityWorkflow("def-1", "v2", "2.0.0", "Approve Order", null, null, [], []));

        var models = (await source.Read(default)).ToList();

        Assert.Equal(2, models.Count);
        Assert.All(models, m => Assert.Equal("def-1", m.ActivityTypeKey));
        Assert.Equal(["1.0.0", "2.0.0"], models.Select(m => m.Version).OrderBy(v => v));
        Assert.Equal(["v1", "v2"], models.Select(m => ((WorkflowIdentity)m.Descriptor).VersionId).OrderBy(v => v));
    }

    private static WorkflowActivityReconciliationSource NewSource(params UsableAsActivityWorkflow[] workflows) =>
        new(new FakeWorkflowSource(workflows));

    private sealed class FakeWorkflowSource(params UsableAsActivityWorkflow[] workflows) : IUsableAsActivityWorkflowSource
    {
        public ValueTask<IEnumerable<UsableAsActivityWorkflow>> Read(CancellationToken cancellationToken) =>
            new(workflows);
    }
}
