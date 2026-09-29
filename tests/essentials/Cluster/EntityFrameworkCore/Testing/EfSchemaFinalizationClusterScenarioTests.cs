using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Readability;
using Elsa.Cluster.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Xunit;

namespace Elsa.Cluster.EntityFrameworkCore.Testing;

/// <summary>
/// Spec 181's finalization gate over the durable membership provider, on a controllable clock, over whichever engine
/// the deriving class supplies: several hosts share one database, each with a gate whose fleet is the real provider and
/// whose record lives beside the membership table, exactly where a module's own would. Each test names the success
/// criterion or scenario it proves.
/// </summary>
public abstract class EfSchemaFinalizationClusterScenarioTests(EfClusterMembershipTestStore store) : IAsyncDisposable
{
    private const string Module = "ScenarioModule";
    private readonly string _family = $"GateScenario{Guid.NewGuid():N}";

    protected EfClusterMembershipConformanceFixture Fixture { get; } = new(store);

    public async ValueTask DisposeAsync()
    {
        await Fixture.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [SkippableFact]
    public async Task SC_001_upgrading_one_host_writes_nothing_new_and_upgrading_the_last_finalizes_with_no_operator_action()
    {
        var old = await StartAsync("old", "1");
        await ActivateAsync(Gate("1", old));
        var upgraded = await StartAsync("upgraded", "1", "2");
        var gate = Gate("2", upgraded);
        await ActivateAsync(gate);

        await RunAsync(gate.EvaluateAsync);
        Assert.Equal("1", await FinalizedAsync());
        Assert.Equal("1", gate.StateOf(_family)!.WriteVersion);

        // The last host is upgraded: its next report reads 2.
        old.SetReadability(Reads("1", "2"));
        await old.Membership.PublishReportAsync();
        await RunAsync(gate.EvaluateAsync);
        await RunAsync(gate.RefreshAsync);

        Assert.Equal("2", await FinalizedAsync());
        Assert.Equal("2", gate.StateOf(_family)!.WriteVersion);
    }

    [SkippableFact]
    public async Task SC_008_a_host_counted_as_expired_while_the_version_finalized_refuses_writes_at_its_next_refresh()
    {
        var partitioned = await StartAsync("partitioned", "1");
        var partitionedGate = Gate("1", partitioned);
        await ActivateAsync(partitionedGate);
        var upgraded = await StartAsync("upgraded", "1", "2");
        var gate = Gate("2", upgraded);
        await ActivateAsync(gate);
        Assert.Equal("1", await FinalizedAsync());

        await partitioned.KillAsync();
        await Fixture.AdvanceAsync(Fixture.Timings.ExpiryPeriod + Fixture.Timings.SkewAllowance + Fixture.Timings.HeartbeatInterval);
        await RunAsync(gate.EvaluateAsync);
        Assert.Equal("2", await FinalizedAsync());

        await RunAsync(partitionedGate.RefreshAsync);
        var state = partitionedGate.StateOf(_family)!;
        Assert.True(state.WritesRefused);
        Assert.Equal(("1", "2"), (state.WriteVersion, state.UnreadableFinalizedVersion));
    }

    /// <summary>
    /// The direction that looks like success: a host that joined after the evaluator last read the fleet is missing from
    /// the evaluator's cached view, so a gate that confirmed from it would finalize a version the newcomer cannot read.
    /// </summary>
    [SkippableFact]
    public async Task A_host_that_joined_after_the_evaluators_cached_read_is_counted_because_the_confirming_read_is_fresh()
    {
        var evaluator = await StartAsync("evaluator", "1", "2");
        var gate = Gate("2", evaluator);
        await HoldAsync();
        await ActivateAsync(gate);
        Assert.Single((await evaluator.Membership.ReadFleetAsync(FleetReadMode.Cached)).Members);

        await StartAsync("newcomer", "1");
        Assert.Single((await evaluator.Membership.ReadFleetAsync(FleetReadMode.Cached)).Members);
        await ReleaseAsync();
        await RunAsync(gate.EvaluateAsync);

        Assert.Equal("1", await FinalizedAsync());
    }

    [SkippableFact]
    public async Task A_member_whose_report_names_another_database_is_not_counted_for_this_one()
    {
        var evaluator = await StartAsync("evaluator", "1", "2");
        var gate = Gate("2", evaluator);
        await HoldAsync();
        await ActivateAsync(gate);
        await StartAsync("elsewhere", new ReadabilityEntry(_family, Module, ["1"], databaseIdentity: "another-database"));

        await ReleaseAsync();
        await RunAsync(gate.EvaluateAsync);

        Assert.Equal("2", await FinalizedAsync());
    }

    /// <summary>
    /// Spec 186, FR-012 and MR-001, over the durable provider on each engine, both ways: the backfill's settle condition
    /// does not hold while a counted member reports having observed an older finalized version, and names it; it holds
    /// once that member publishes the target, read fresh from the membership table. A member whose report speaks for
    /// another database is not waited for.
    /// </summary>
    [SkippableFact]
    public async Task The_backfill_settle_condition_waits_for_every_counted_member_to_report_observing_the_target()
    {
        var backfilling = await StartAsync("backfilling", Observing("2"));
        var lagging = await StartAsync("lagging", Observing("1"));
        await StartAsync("elsewhere", new ReadabilityEntry(_family, Module, ["1", "2"], databaseIdentity: "another-database", observedFinalizedVersion: "1"));
        var fleet = new ClusterSchemaFleet(backfilling.Membership);

        var waiting = await fleet.CountObservingAsync(_family, ["2"], "this-database");

        Assert.False(waiting.EveryCountedMemberReads);
        Assert.Contains("has observed [1]", Assert.Single(waiting.Blockers));

        lagging.SetReadability(Observing("2"));
        await lagging.Membership.PublishReportAsync();

        Assert.True((await fleet.CountObservingAsync(_family, ["2"], "this-database")).EveryCountedMemberReads);
    }

    private ReadabilityEntry Observing(string observed) => new(_family, Module, ["1", "2"], observedFinalizedVersion: observed);

    private EfSchemaModuleGate Gate(string current, IConformanceMember member) =>
        new(
            EfSchemaModuleFamilies.FromDeclarations(Module, [Declaration(current)]),
            new ClusterSchemaFleet(member.Membership),
            new EfSchemaFinalizationObservations(),
            new EfSchemaFinalizationOptions { IntentWaitBound = TimeSpan.FromSeconds(2), IntentPollInterval = TimeSpan.FromMilliseconds(20) });

    private EfSchemaFamilyDescriptor Declaration(string current) =>
        new(_family, Module, current, typeof(ScenarioUpcaster).Assembly)
        {
            Upcasters = current == "2" ? [new EfSchemaUpcasterDescriptor(typeof(ScenarioUpcaster), "1", "2")] : []
        };

    private Task<IConformanceMember> StartAsync(string name, params string[] readable) => StartAsync(name, Reads(readable));

    private async Task<IConformanceMember> StartAsync(string name, ReadabilityEntry reads) =>
        await Fixture.StartMemberAsync(new ConformanceMemberSetup($"{name}-{Guid.NewGuid():N}", [reads]));

    private ReadabilityEntry Reads(params string[] readable) => new(_family, Module, readable);

    private Task ActivateAsync(EfSchemaModuleGate gate) => RunAsync(gate.ActivateAsync);

    private Task RunAsync(Func<Microsoft.EntityFrameworkCore.DbContext, CancellationToken, Task> step) =>
        Fixture.WithStoreAsync(async context =>
        {
            await step(context, CancellationToken.None);
            return 0;
        });

    private Task<string> FinalizedAsync() =>
        Fixture.WithStoreAsync(async context => (await new EfSchemaFinalizationStore(context).FindAsync(_family))!.FinalizedVersion);

    private Task HoldAsync() => Fixture.WithStoreAsync(async context =>
    {
        var store = new EfSchemaFinalizationStore(context);
        var created = await store.GetOrCreateAsync(_family, "1", ["1", "2"], SchemaFinalizationActor.OfOperator("ops"));
        return await store.PlaceHoldAsync(_family, created.Revision, null, "held while the fleet is staged", "ops", ["1", "2"]);
    });

    private Task ReleaseAsync() => Fixture.WithStoreAsync(async context =>
    {
        var store = new EfSchemaFinalizationStore(context);
        return await store.ReleaseHoldAsync(_family, (await store.FindAsync(_family))!.Revision, null, "ops");
    });
}

/// <summary>The one step of the scenario family's chain, from 1 to 2: its content is the same at both versions.</summary>
[EfSchemaUpcaster("1", "2")]
public sealed class ScenarioUpcaster : IEfSchemaUpcaster
{
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
