using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.DraftMutationCommandTests;

/// <summary>
/// SC-017 + Unit C FR-028. <see cref="ICloneDraftFromVersionCommand"/> produces a new Draft
/// whose State and Layout are deep-equal to the source Version's, with NodeIds carrying 1:1
/// per FR-009a's copy semantics. The clone publishes <c>OnDraftClonedFromVersion</c>.
/// </summary>
public sealed class CloneDraftFromVersionTests
{
    [Fact]
    public async Task Clone_deep_copies_State_from_source_Version()
    {
        using var host = WorkflowsDesignTestHost.Create();

        var sourceState = StateWithActivities([Node("start", isStart: true), Node("step")]);
        var (versionId, definitionId) = await SeedVersion(host, sourceState, layoutRecords: []);

        var newDraftId = await Clone(host, versionId);

        using var ctx = host.CreateContext();
        var newDraft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == newDraftId);

        // Re-hydrate State from the persisted StateSource.
        var serializer = host.Services.GetRequiredService<IPayloadSerializer>();
        var hydratedState = serializer.Deserialize<WorkflowDefinitionState>(newDraft.StateSource!);

        var sourceActivities = sourceState.Activities.ToList();
        var clonedActivities = hydratedState.Activities.ToList();

        Assert.Equal(sourceActivities.Count, clonedActivities.Count);
        Assert.Equal(sourceActivities.Select(a => a.NodeId), clonedActivities.Select(a => a.NodeId));
        Assert.Equal(sourceActivities.Select(a => a.IsStart), clonedActivities.Select(a => a.IsStart));
    }

    [Fact]
    public async Task Clone_deep_copies_Layout_records_from_source_Version()
    {
        using var host = WorkflowsDesignTestHost.Create();

        var records = new List<DesignMetadataRecord>
        {
            new("start", X: 10,  Y: 20),
            new("step",  X: 100, Y: 200, Width: 150, Height: 80),
        };

        var (versionId, definitionId) = await SeedVersion(host, StateWithActivities([Node("start", isStart: true), Node("step")]), records);

        var newDraftId = await Clone(host, versionId);

        using var ctx = host.CreateContext();
        var newLayout = await ctx.WorkflowDefinitionDraftLayouts
            .FirstAsync(l => l.WorkflowDefinitionDraftId == newDraftId);

        Assert.Equal(2, newLayout.Records.Count);
        Assert.Contains(newLayout.Records, r => r.NodeId == "start" && r.X == 10 && r.Y == 20);
        Assert.Contains(newLayout.Records, r => r.NodeId == "step" && r.Width == 150);
    }

    [Fact]
    public async Task Clone_carries_NodeIds_one_to_one()
    {
        using var host = WorkflowsDesignTestHost.Create();

        var sourceState = StateWithActivities([
            Node("alpha", isStart: true),
            Node("beta"),
            Node("gamma"),
        ]);
        var (versionId, definitionId) = await SeedVersion(host, sourceState, layoutRecords: []);

        var newDraftId = await Clone(host, versionId);

        using var ctx = host.CreateContext();
        var newDraft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == newDraftId);
        var serializer = host.Services.GetRequiredService<IPayloadSerializer>();
        var hydratedState = serializer.Deserialize<WorkflowDefinitionState>(newDraft.StateSource!);

        var sourceIds = sourceState.Activities.Select(a => a.NodeId).ToHashSet();
        var clonedIds = hydratedState.Activities.Select(a => a.NodeId).ToHashSet();
        Assert.True(sourceIds.SetEquals(clonedIds));
    }

    [Fact]
    public async Task Clone_publishes_OnDraftClonedFromVersion()
    {
        using var host = WorkflowsDesignTestHost.Create();

        var (versionId, definitionId) = await SeedVersion(host, StateWithActivities([Node("only", isStart: true)]), layoutRecords: []);

        var newDraftId = await Clone(host, versionId);

        var published = host.EventPublisher.LastOf<OnDraftClonedFromVersion>();
        Assert.NotNull(published);
        Assert.Equal(newDraftId, published!.NewDraftId);
        Assert.Equal(versionId, published.SourceVersionId);
        Assert.Equal(definitionId, published.TargetDefinitionId);
    }

    private static async Task<string> Clone(WorkflowsDesignTestHost host, string sourceVersionId)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICloneDraftFromVersionCommand>()
            .Execute(sourceVersionId);
    }

    private static async Task<(string versionId, string definitionId)> SeedVersion(
        WorkflowsDesignTestHost host,
        WorkflowDefinitionState state,
        List<DesignMetadataRecord> layoutRecords)
    {
        var definitionId = Guid.NewGuid().ToString("N");
        var versionId = Guid.NewGuid().ToString("N");

        using var ctx = host.CreateContext();

        ctx.WorkflowDefinitions.Add(new WorkflowDefinition { Id = definitionId, Name = "wf" });

        // Set State on the entity; the saving handler serialises it into StateSource on save.
        // Passing pre-serialised content to the ctor is overwritten by the handler when State is null.
        var version = new WorkflowDefinitionVersion(definitionId, 1)
        {
            Id = versionId,
            State = state,
        };
        ctx.WorkflowDefinitionVersions.Add(version);

        ctx.WorkflowDefinitionVersionLayouts.Add(new WorkflowDefinitionVersionLayout
        {
            Id = Guid.NewGuid().ToString("N"),
            WorkflowDefinitionVersionId = versionId,
            Records = layoutRecords,
        });

        await ctx.SaveChangesAsync();

        return (versionId, definitionId);
    }

    private static WorkflowDefinitionState StateWithActivities(IEnumerable<ActivityNode> activities) => new(
        Variables: [],
        ActivityConnections: [],
        Activities: activities,
        Inputs: [],
        Outputs: [],
        WorkflowActivityOptions: null,
        StrategyOptions: null);

    private static ActivityNode Node(string nodeId, bool isStart = false) => new(
        NodeId: nodeId,
        ActivityVersionId: "av-1",
        Inputs: [],
        Outputs: [],
        IsContainer: false,
        IsStart: isStart,
        IsTerminal: false,
        ChildActivities: []);
}
