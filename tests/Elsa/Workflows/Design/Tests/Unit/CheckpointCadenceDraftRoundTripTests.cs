using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// ADR 0032 R5 authoring carrier. The per-workflow checkpoint cadence rides
/// <see cref="WorkflowStrategyOptions.CheckpointCadence"/> on the authored <see cref="WorkflowDefinitionState"/>, so it
/// must round-trip byte-faithfully through the full-state draft replace (serialize → StateSource → deserialize) and be
/// carried wholesale onto the immutable version at promotion — the compile source the executable compiler reads.
/// </summary>
public sealed class CheckpointCadenceDraftRoundTripTests
{
    [Fact]
    public async Task Draft_replace_persists_and_reloads_the_authored_checkpoint_cadence()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        await Update(host, draftId, StateWithCadence(new WorkflowCheckpointCadenceOptions
        {
            Mode = "Coalesced",
            MaxSegmentCheckpoints = 8
        }));

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        var cadence = DeserializeState(host, draft.StateSource).StrategyOptions?.CheckpointCadence;
        Assert.NotNull(cadence);
        Assert.Equal("Coalesced", cadence!.Mode);
        Assert.Equal(8, cadence.MaxSegmentCheckpoints);
    }

    [Fact]
    public async Task Promotion_carries_the_authored_checkpoint_cadence_onto_the_immutable_version()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, StateWithCadence(new WorkflowCheckpointCadenceOptions { Mode = "Immediate" }));

        string versionId;
        using (var scope = host.Services.CreateScope())
            versionId = await scope.ServiceProvider.GetRequiredService<IPromoteDraftToVersionCommand>().Execute(WorkflowsDesignTestHost.TestOperationKey, draftId);

        using var ctx = host.CreateContext();
        var version = await ctx.WorkflowDefinitionVersions.FirstAsync(v => v.Id == versionId);
        var cadence = DeserializeState(host, version.StateSource).StrategyOptions?.CheckpointCadence;
        Assert.NotNull(cadence);
        Assert.Equal("Immediate", cadence!.Mode);
        Assert.Null(cadence.MaxSegmentCheckpoints);
    }

    private static WorkflowDefinitionState StateWithCadence(WorkflowCheckpointCadenceOptions cadence) =>
        State(activities: [Node("a")]) with
        {
            StrategyOptions = new WorkflowStrategyOptions { CheckpointCadence = cadence }
        };

    // Raw test-host context reads do not run the entity-loading handlers that hydrate State from StateSource, so
    // round-trip through the same registered IPayloadSerializer the production loading handler uses.
    private static WorkflowDefinitionState DeserializeState(WorkflowsDesignTestHost host, string? stateSource)
    {
        Assert.False(string.IsNullOrWhiteSpace(stateSource));
        using var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPayloadSerializer>().Deserialize<WorkflowDefinitionState>(stateSource!);
    }
}
