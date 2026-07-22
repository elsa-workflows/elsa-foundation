using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// ADR 0032 R5 authoring carrier. The per-workflow checkpoint cadence rides
/// <see cref="WorkflowStrategyOptions.CheckpointCadence"/> on the authored <see cref="WorkflowDefinitionState"/>, so it
/// must round-trip faithfully through the full-state draft replace (serialize → persist → deserialize) and be
/// carried wholesale onto the immutable version at promotion — the compile source the executable compiler reads.
/// The Groundwork stores hydrate the authored <c>State</c> on read exactly as an application consumer sees it.
/// </summary>
public sealed class CheckpointCadenceDraftRoundTripTests
{
    [Fact]
    public async Task Draft_replace_persists_and_reloads_the_authored_checkpoint_cadence()
    {
        using var host = await WorkflowsDesignTestHost.CreateAsync();
        var draftId = await SeedEmptyDraft(host);

        await Update(host, draftId, StateWithCadence(new WorkflowCheckpointCadenceOptions
        {
            Mode = "Coalesced",
            MaxSegmentCheckpoints = 8
        }));

        var draft = await host.GetDraftAsync(draftId);
        Assert.NotNull(draft);
        var cadence = draft!.State.StrategyOptions?.CheckpointCadence;
        Assert.NotNull(cadence);
        Assert.Equal("Coalesced", cadence!.Mode);
        Assert.Equal(8, cadence.MaxSegmentCheckpoints);
    }

    [Fact]
    public async Task Promotion_carries_the_authored_checkpoint_cadence_onto_the_immutable_version()
    {
        using var host = await WorkflowsDesignTestHost.CreateAsync();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, StateWithCadence(new WorkflowCheckpointCadenceOptions { Mode = "Immediate" }));

        string versionId;
        using (var scope = host.Services.CreateScope())
            versionId = await scope.ServiceProvider.GetRequiredService<IPromoteDraftToVersionCommand>().Execute(WorkflowsDesignTestHost.TestOperationKey, draftId);

        var version = await host.GetVersionAsync(versionId);
        Assert.NotNull(version);
        var cadence = version!.State.StrategyOptions?.CheckpointCadence;
        Assert.NotNull(cadence);
        Assert.Equal("Immediate", cadence!.Mode);
        Assert.Null(cadence.MaxSegmentCheckpoints);
    }

    private static WorkflowDefinitionState StateWithCadence(WorkflowCheckpointCadenceOptions cadence) =>
        State(activities: [Node("a")]) with
        {
            StrategyOptions = new WorkflowStrategyOptions { CheckpointCadence = cadence }
        };
}
