using System.Text.Json;
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
/// per FR-009a's copy semantics. Clone delegates to <see cref="ICreateDraftCommand"/>, so it
/// publishes the single origination event <c>OnDraftCreated</c> carrying the source version id;
/// the same id is persisted on the Draft entity's <c>SourceVersionId</c>.
/// </summary>
public sealed class CloneDraftFromVersionTests
{
    private const string ActivitiesSlotName = "Activities";
    private const string StructureKind = "test.workflow-root.structure";
    private const string StructureSchemaVersion = "1.0.0";

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

        var sourceActivities = Flatten(sourceState.RootActivity).ToList();
        var clonedActivities = Flatten(hydratedState.RootActivity).ToList();

        Assert.Equal(sourceActivities.Count, clonedActivities.Count);
        Assert.Equal(sourceActivities.Select(a => a.NodeId), clonedActivities.Select(a => a.NodeId));
        Assert.Equal(StartActivityNodeId(sourceState.RootActivity), StartActivityNodeId(hydratedState.RootActivity));
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

        var sourceIds = Flatten(sourceState.RootActivity).Select(a => a.NodeId).ToHashSet();
        var clonedIds = Flatten(hydratedState.RootActivity).Select(a => a.NodeId).ToHashSet();
        Assert.True(sourceIds.SetEquals(clonedIds));
    }

    [Fact]
    public async Task Clone_publishes_OnDraftCreated_carrying_the_source_version_id()
    {
        using var host = WorkflowsDesignTestHost.Create();

        var (versionId, definitionId) = await SeedVersion(host, StateWithActivities([Node("only", isStart: true)]), layoutRecords: []);

        var newDraftId = await Clone(host, versionId);

        var published = host.EventPublisher.LastOf<OnDraftCreated>();
        Assert.NotNull(published);
        Assert.Equal(newDraftId, published!.DraftId);
        Assert.Equal(definitionId, published.WorkflowDefinitionId);
        Assert.Equal(versionId, published.SourceVersionId);
    }

    [Fact]
    public async Task Clone_persists_the_source_version_id_on_the_Draft_entity()
    {
        using var host = WorkflowsDesignTestHost.Create();

        var (versionId, _) = await SeedVersion(host, StateWithActivities([Node("only", isStart: true)]), layoutRecords: []);

        var newDraftId = await Clone(host, versionId);

        using var ctx = host.CreateContext();
        var newDraft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == newDraftId);
        Assert.Equal(versionId, newDraft.SourceVersionId);
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
        var version = new WorkflowDefinitionVersion(definitionId, "1.0.0")
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
        RootActivity: new ActivityNode(
            NodeId: "$root",
            ActivityVersionId: "$workflow-root",
            Inputs: [],
            Outputs: [],
            ChildSlots:
            [
                new ActivityChildSlot(
                    ActivitiesSlotName,
                    activities)
            ],
            Structure: new ActivityNodeStructure(
                StructureKind,
                StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { startActivityNodeId = activities.FirstOrDefault(activity => activity.NodeId == "start")?.NodeId ?? string.Empty }))),
        Inputs: [],
        Outputs: [],
        WorkflowActivityOptions: null,
        StrategyOptions: null);

    private static ActivityNode Node(string nodeId, bool isStart = false) => new(
        NodeId: nodeId,
        ActivityVersionId: "av-1",
        Inputs: [],
        Outputs: []);

    private static IEnumerable<ActivityNode> Flatten(ActivityNode? root)
    {
        if (root is null)
            yield break;

        yield return root;
        foreach (var child in (root.ChildSlots ?? []).SelectMany(slot => slot.Activities))
        foreach (var nested in Flatten(child))
            yield return nested;
    }

    private static string? StartActivityNodeId(ActivityNode? root) =>
        root?.Structure?.Payload.TryGetProperty("startActivityNodeId", out var startActivityNodeId) == true
            ? startActivityNodeId.GetString()
            : null;
}
