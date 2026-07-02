using Elsa.Activities.Composition.Design.Reconciliation;
using Elsa.Activities.Composition.Tests.TestSupport;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Activities.Composition.Tests;

/// <summary>
/// The adapter is the single class that reaches into the Workflows Design read ports (§2.7). These tests
/// pin the discovery contract: the usable-as-activity filter, soft-delete exclusion, and the projection
/// of identity, category, and I/O off the authored workflow state. Store fakes/builders come from
/// <see cref="WorkflowDesignData"/> so this file carries no bespoke stub boilerplate.
/// </summary>
public sealed class WorkflowDefinitionUsableAsActivitySourceTests
{
    private const string DefinitionId = "def-1";

    [Fact]
    public async Task Read_UsableVersion_ProjectsIdentityCategoryAndMirrorsIo()
    {
        var inputs = new List<InputDefinition>();
        var outputs = new List<OutputDefinition>();
        var source = NewSource(
            WorkflowDesignData.Definition(DefinitionId, "Approve Order", "Approval subflow"),
            WorkflowDesignData.Version(DefinitionId, "v1", "1.0.0", usableAsActivity: true, category: "Reusable", inputs: inputs, outputs: outputs));

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
        var source = NewSource(
            WorkflowDesignData.Definition(DefinitionId, "Internal Only"),
            WorkflowDesignData.Version(DefinitionId, "v1", "1.0.0", usableAsActivity: false),
            WorkflowDesignData.Version(DefinitionId, "v2", "2.0.0", usableAsActivity: null));

        Assert.Empty(await source.Read(default));
    }

    [Fact]
    public async Task Read_SoftDeletedDefinition_IsExcludedEvenWhenUsable()
    {
        var source = NewSource(
            WorkflowDesignData.Definition(DefinitionId, "Approve Order", deletedAt: DateTimeOffset.UnixEpoch),
            WorkflowDesignData.Version(DefinitionId, "v1", "1.0.0", usableAsActivity: true));

        Assert.Empty(await source.Read(default));
    }

    [Fact]
    public async Task Read_MultipleUsableVersions_YieldOnePerVersion()
    {
        var source = NewSource(
            WorkflowDesignData.Definition(DefinitionId, "Approve Order"),
            WorkflowDesignData.Version(DefinitionId, "v1", "1.0.0", usableAsActivity: true),
            WorkflowDesignData.Version(DefinitionId, "v2", "2.0.0", usableAsActivity: true));

        var results = (await source.Read(default)).ToList();

        Assert.Equal(["v1", "v2"], results.Select(w => w.VersionId).OrderBy(v => v));
    }

    private static WorkflowDefinitionUsableAsActivitySource NewSource(WorkflowDefinition definition, params WorkflowDefinitionVersion[] versions) =>
        new(new StubDefinitionStore(definition), new StubVersionStore(versions));
}
