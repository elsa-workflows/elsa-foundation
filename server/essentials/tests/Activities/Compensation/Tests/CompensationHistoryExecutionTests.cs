using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

public sealed class CompensationHistoryExecutionTests
{
    public static IEnumerable<object[]> Cases() =>
        from mode in Enum.GetValues<PersistenceMode>()
        from sqlite in new[] { false, true }
        from scenario in new[] { "recover", "confirm", "resolve", "abandon" }
        select new object[] { mode, sqlite, scenario };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Native_decisions_survive_continuation_folding_and_private_state_cleanup(PersistenceMode mode, bool sqlite, string scenario)
    {
        using var host = new CompensationHost(mode, sqlite ? CompensationStore.Sqlite() : CompensationStore.InMemory());
        var participant = scenario == "confirm" ? new Participant() : new Participant().Rejecting("confirm-order")
            .FailingTimes("cancel-delivery", scenario == "recover" ? 1 : 3);
        await using var harness = await host.StartHostAsync(participant, services =>
        {
            services.AddWorkflowRuntimeHistory();
            services.AddWorkflowStimulusRouter();
            services.Replace(ServiceDescriptor.Singleton<IPersistenceAccessContextAccessor>(new Access()));
        });
        var graph = scenario == "confirm"
            ? Scope(ScopeNodeId, false, 3, true, new Step("Reserve", Participant("reserve-inventory"), Participant("release-inventory")))
            : ReservationJourney();
        await CompensationHost.StartAsync(harness, graph);
        await harness.SweepUntilQuietAsync();
        if (scenario is "resolve" or "abandon")
        {
            var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(CompensationHost.WorkflowExecutionId));
            await OperatorActions.DecideAsync(harness, CompensationHost.WorkflowExecutionId, incident.IncidentId,
                scenario == "resolve" ? CompensationScope.Decisions.ResolveEffectAbsent : CompensationScope.Decisions.Abandon,
                "history-" + scenario, CompensationHost.DefaultPartition);
        }
        var history = harness.Services.GetRequiredService<IRuntimeHistoryStore>();
        var head = Assert.IsType<RuntimeHistoryHead>(await history.FindHeadAsync(CompensationHost.WorkflowExecutionId));
        Assert.Equal("runtime-history-v5", head.Policy.PolicyId);
        Assert.True(head.CapturedFromStart);
        Assert.False(head.HasGaps);
        foreach (var kind in CompensationHistory.Kinds) Assert.Contains(kind.ToString(), head.Policy.SupportedKinds);
        var events = new List<RuntimeHistoryEvent>();
        string? cursor = null;
        do
        {
            var page = await history.ReadPageAsync(new(CompensationHost.WorkflowExecutionId, 100, cursor));
            events.AddRange(page.Items);
            cursor = page.NextContinuationToken;
        } while (cursor is not null);
        Assert.Contains(events, e => e.Name == "CompensationRegistered");
        Assert.All(events.Where(e => e.Name.StartsWith("Compensation", StringComparison.Ordinal)), e =>
        {
            Assert.Equal(RuntimeHistoryAuthority.CommittedTransition, e.Authority);
            Assert.NotNull(e.CheckpointId);
            Assert.NotNull(e.ActivityExecutionId);
            Assert.Null(e.Payload);
            Assert.DoesNotContain(e.Metadata.Keys, key => key.Contains("operationKey", StringComparison.OrdinalIgnoreCase));
        });
        Assert.Equal(events.Count, events.Select(e => e.HistoryEventId).Distinct(StringComparer.Ordinal).Count());
        if (scenario == "confirm")
        {
            Assert.Contains(events, e => e.Name == "CompensationConfirmed");
            Assert.DoesNotContain(events, e => e.Name == "CompensationStarted");
        }
        else
        {
            foreach (var kind in new[] { "CompensationStarted", "CompensationHandlerScheduled", "CompensationHandlerFaulted" })
                Assert.Contains(events, e => e.Name == kind);
            if (scenario != "abandon") Assert.Contains(events, e => e.Name == "CompensationHandlerCompleted");
            if (scenario == "resolve") Assert.Contains(events, e => e.Name == "CompensationResolved" && e.Metadata.GetValueOrDefault("history.reasonCode") == "effect-absent");
        }
        Assert.Contains(events, e => e.Name == (scenario == "abandon" ? "CompensationAbandoned" : "CompensationCompleted"));
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new(PersistenceScope.DefaultValue));
    }
}
