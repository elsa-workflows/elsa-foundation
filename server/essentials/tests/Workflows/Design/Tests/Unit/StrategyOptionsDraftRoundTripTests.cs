using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Strategy options authoring carrier. The workflow's chosen persistence mode and incident strategy ride
/// <see cref="WorkflowDefinitionState.StrategyOptions"/>, so they must round-trip faithfully through the full-state draft
/// replace (serialize → persist → deserialize) and be carried wholesale onto the immutable version at promotion, the
/// compile source the executable compiler reads. The design stores hydrate the authored <c>State</c> on read exactly as
/// an application consumer sees it.
/// </summary>
public sealed class StrategyOptionsDraftRoundTripTests
{
    [Fact]
    public async Task Draft_replace_persists_and_reloads_the_chosen_persistence_mode()
    {
        using var host = await WorkflowsDesignTestHost.CreateAsync();
        var draftId = await SeedEmptyDraft(host);

        await Update(host, draftId, StateWithPersistenceMode("Balanced"));

        var draft = await host.GetDraftAsync(draftId);
        Assert.NotNull(draft);
        Assert.Equal("Balanced", draft!.State.StrategyOptions?.PersistenceMode);
    }

    [Fact]
    public async Task Promotion_carries_the_chosen_persistence_mode_onto_the_immutable_version()
    {
        using var host = await WorkflowsDesignTestHost.CreateAsync();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, StateWithPersistenceMode("Durable"));

        var stateHash = WorkflowDraftStateHash.Compute((await host.GetDraftAsync(draftId))!.StateSource);
        string versionId;
        using (var scope = host.Services.CreateScope())
            versionId = await scope.ServiceProvider.GetRequiredService<IPromoteDraftToVersionCommand>()
                .Execute(WorkflowsDesignTestHost.TestOperationKey, draftId, requestedVersion: null, stateHash);

        var version = await host.GetVersionAsync(versionId);
        Assert.NotNull(version);
        Assert.Equal("Durable", version!.State.StrategyOptions?.PersistenceMode);
    }

    [Fact]
    public async Task Draft_replace_preserves_the_exact_incident_strategy_reference()
    {
        using var host = await WorkflowsDesignTestHost.CreateAsync();
        var draftId = await SeedEmptyDraft(host);
        var strategy = new IncidentStrategyReference("Contoso.Operations", "vNext");
        var state = State(activities: [Node("a")]) with
        {
            StrategyOptions = new WorkflowStrategyOptions { IncidentStrategy = strategy }
        };

        await Update(host, draftId, state);

        var draft = await host.GetDraftAsync(draftId);
        Assert.Equal(strategy, draft!.State.StrategyOptions!.IncidentStrategy);
    }

    private static WorkflowDefinitionState StateWithPersistenceMode(string persistenceMode) =>
        State(activities: [Node("a")]) with
        {
            StrategyOptions = new WorkflowStrategyOptions { PersistenceMode = persistenceMode }
        };
}
