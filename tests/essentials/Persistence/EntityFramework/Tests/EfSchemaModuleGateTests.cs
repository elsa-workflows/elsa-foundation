using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.SchemaGate;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The finalization gate of one module (spec 181): admission, evaluation, refresh and what it lets a host write, on a
/// file database shared by every member a test runs, with a fleet the test controls. Each finalization is checked from
/// the record itself, and each refusal to have changed nothing, since a gate that reported "pending" while the record
/// said otherwise would look exactly like one that works.
/// </summary>
public sealed class EfSchemaModuleGateTests : IAsyncLifetime
{
    private readonly TemporarySqliteDatabase database = new("schema-gate");
    private readonly FakeFleetState fleet = new();
    private readonly List<DbContext> contexts = [];

    public async Task InitializeAsync() => await Context().Database.EnsureCreatedAsync();

    public async Task DisposeAsync()
    {
        foreach (var context in contexts)
            await context.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task A_host_alone_creates_the_record_at_its_oldest_version_and_finalizes_its_current_version_at_activation()
    {
        var gate = Gate(Families("3"), Fleet("host-a", "1", "2", "3"));

        await gate.ActivateAsync(Context());

        var record = await RecordAsync();
        Assert.Equal("3", record.FinalizedVersion);
        Assert.Equal(SchemaFinalizationTransition.Created, record.History[0].Transition);
        Assert.Equal("1", record.History[0].Version);
        Assert.Equal([SchemaFinalizationTransition.Created, SchemaFinalizationTransition.IntentRecorded, SchemaFinalizationTransition.Finalized],
            record.History.Select(entry => entry.Transition));
        Assert.Equal("host-a", record.History[^1].Actor.Member!.HostId);
        Assert.Equal("3", gate.StateOf(Family)!.WriteVersion);
    }

    [Fact]
    public async Task A_hold_keeps_a_host_alone_at_the_version_it_holds_and_the_status_names_the_hold()
    {
        await HoldAsync(version: null, "canary of 3");
        var gate = Gate(Families("3"), Fleet("host-a", "1", "2", "3"));

        await gate.ActivateAsync(Context());

        Assert.Equal("1", (await RecordAsync()).FinalizedVersion);
        Assert.Equal("1", gate.StateOf(Family)!.WriteVersion);
        var status = (await gate.ReadStatusAsync(Context())).Single(family => family.Family == Family);
        Assert.All(status.Pending, pending => Assert.Equal("canary of 3", Assert.Single(pending.HeldBy).Reason));
        Assert.Equal(["2", "3"], status.Pending.Select(pending => pending.Version));
    }

    [Fact]
    public async Task A_version_one_counted_member_cannot_read_stays_pending_and_every_writer_keeps_the_old_version()
    {
        var old = Gate(Families("1"), Fleet("host-old", "1"));
        await old.ActivateAsync(Context());
        var upgraded = Gate(Families("2"), Fleet("host-new", "1", "2"));

        await upgraded.ActivateAsync(Context());
        await upgraded.EvaluateAsync(Context());

        var record = await RecordAsync();
        Assert.Equal("1", record.FinalizedVersion);
        Assert.DoesNotContain(record.History, entry => entry.Transition == SchemaFinalizationTransition.IntentRecorded);
        Assert.Equal("1", upgraded.StateOf(Family)!.WriteVersion);
        Assert.Equal("1", old.StateOf(Family)!.WriteVersion);
        var pending = Assert.Single((await upgraded.ReadStatusAsync(Context())).Single(family => family.Family == Family).Pending);
        Assert.Contains("host-old", Assert.Single(pending.Blockers!));
    }

    [Fact]
    public async Task Once_the_last_member_reads_the_version_the_next_evaluation_finalizes_it_and_a_refresh_switches_every_writer()
    {
        var lagging = Fleet("host-b", "1");
        await Gate(Families("1"), lagging).ActivateAsync(Context());
        var first = Gate(Families("2"), Fleet("host-a", "1", "2"));
        await first.ActivateAsync(Context());
        Assert.Equal("1", (await RecordAsync()).FinalizedVersion);

        // host-b is upgraded in place: its next report reads 2.
        lagging.Self.Reading(Family, "1", "2");
        await first.EvaluateAsync(Context());
        await first.RefreshAsync(Context());

        Assert.Equal("2", (await RecordAsync()).FinalizedVersion);
        Assert.Equal("2", first.StateOf(Family)!.WriteVersion);
    }

    [Fact]
    public async Task Several_pending_versions_finalize_in_one_step_on_the_newest_every_counted_member_reads()
    {
        var old = Fleet("host-old", "1");
        await Gate(Families("1"), old).ActivateAsync(Context());
        await Gate(Families("2"), Fleet("host-mid", "1", "2")).ActivateAsync(Context());
        var newest = Gate(Families("3"), Fleet("host-new", "1", "2", "3"));
        await newest.ActivateAsync(Context());
        Assert.Equal("1", (await RecordAsync()).FinalizedVersion);

        old.Self.Live = false;
        await newest.EvaluateAsync(Context());

        // 3 is past host-mid's current version, so the step lands on 2, never beyond a counted member.
        Assert.Equal("2", (await RecordAsync()).FinalizedVersion);
    }

    [Fact]
    public async Task A_host_that_cannot_read_the_finalized_version_is_refused_and_changes_nothing()
    {
        await Gate(Families("3"), Fleet("host-new", "1", "2", "3")).ActivateAsync(Context());
        var before = await SnapshotAsync();

        var refusal = await Assert.ThrowsAsync<EfSchemaActivationRefusedException>(
            () => Gate(Families("2"), Fleet("host-rolled-back", "1", "2")).ActivateAsync(Context()));

        Assert.Equal(EfSchemaActivationRefusal.FinalizedUnreadable, refusal.Refusal);
        Assert.Equal((Module, Family, "3"), (refusal.Module, refusal.Family, refusal.Version));
        Assert.Equal(["1", "2"], refusal.ReadableVersions);
        Assert.Contains("restore a database backup", refusal.Message);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task A_host_that_reads_the_finalized_version_is_admitted_so_the_refusal_is_not_blanket()
    {
        await Gate(Families("2"), Fleet("host-a", "1", "2")).ActivateAsync(Context());

        var gate = Gate(Families("3"), Fleet("host-b", "1", "2", "3"));
        await gate.ActivateAsync(Context());

        Assert.NotNull(gate.StateOf(Family));
    }

    [Fact]
    public async Task A_host_that_cannot_read_the_completion_version_is_refused_although_it_reads_the_finalized_one()
    {
        // Created at 1, so complete at 1, then finalized at 3: rows at 1 may still exist.
        await StoreRecordAsync("1", ["1", "2", "3"]);
        await EfSchemaFinalizationTestSupport.FinalizeAsync(new EfSchemaFinalizationStore(Context()), Family, ["1", "2", "3"], "3", new("host-x", "i"));
        var before = await SnapshotAsync();

        var retired = EfSchemaModuleFamilies.FromDeclarations(Module, [Declaration(Family, "2-3") with { Entities = [typeof(GateRow)] }]);
        var refusal = await Assert.ThrowsAsync<EfSchemaActivationRefusedException>(() => Gate(retired, Fleet("host-retired", "2", "3")).ActivateAsync(Context()));

        Assert.Equal(EfSchemaActivationRefusal.CompletionUnreadable, refusal.Refusal);
        Assert.Equal("1", refusal.Version);
        Assert.Equal(before, await SnapshotAsync());
        await Gate(Families("3"), Fleet("host-full", "1", "2", "3")).ActivateAsync(Context());
    }

    /// <summary>
    /// Spec 186, User Story 6, acceptance 2; SC-006: once the backfill has recorded the family complete past the version a
    /// release retired, that release activates. The test above is the other direction.
    /// </summary>
    [Fact]
    public async Task A_host_whose_readable_set_starts_at_the_completion_version_activates()
    {
        await StoreRecordAsync("1", ["1", "2", "3"]);
        var finalized = await EfSchemaFinalizationTestSupport.FinalizeAsync(new EfSchemaFinalizationStore(Context()), Family, ["1", "2", "3"], "3", new("host-x", "i"));
        var at = DateTimeOffset.UtcNow;
        await new EfSchemaFinalizationStore(Context()).RecordCompletionAsync(Family, finalized.Revision, "2", at, at, ["1", "2", "3"], new("host-x", "i"));

        var retired = EfSchemaModuleFamilies.FromDeclarations(Module, [Declaration(Family, "2-3") with { Entities = [typeof(GateRow)] }]);
        var gate = Gate(retired, Fleet("host-retired", "2", "3"));
        await gate.ActivateAsync(Context());

        Assert.Equal("3", gate.StateOf(Family)!.WriteVersion);
    }

    [Fact]
    public async Task An_intent_left_by_a_crashed_evaluator_is_resolved_by_the_next_evaluation()
    {
        var gate = Gate(Families("2"), Fleet("host-a", "1", "2"));
        await HoldAsync(null, "until the crash is staged");
        await gate.ActivateAsync(Context());
        var record = await RecordAsync();
        record = (await new EfSchemaFinalizationStore(Context()).ReleaseHoldAsync(Family, record.Revision, null, "ops")).Record;
        await new EfSchemaFinalizationStore(Context()).RecordIntentAsync(Family, record.Revision, "2", ["1", "2"], new("host-crashed", "gone"));

        await gate.EvaluateAsync(Context());

        record = await RecordAsync();
        Assert.Equal("2", record.FinalizedVersion);
        Assert.Null(record.Intent);
        Assert.Equal("host-a", record.History[^1].Actor.Member!.HostId);
    }

    [Fact]
    public async Task Two_evaluators_racing_finalize_once_and_record_one_intent()
    {
        await HoldAsync(null, "so both admit at 1");
        var first = Gate(Families("2"), Fleet("host-a", "1", "2"));
        var second = Gate(Families("2"), Fleet("host-b", "1", "2"));
        await first.ActivateAsync(Context());
        await second.ActivateAsync(Context());
        await new EfSchemaFinalizationStore(Context()).ReleaseHoldAsync(Family, (await RecordAsync()).Revision, null, "ops");

        await Task.WhenAll(first.EvaluateAsync(Context()), second.EvaluateAsync(Context()));

        var record = await RecordAsync();
        Assert.Equal("2", record.FinalizedVersion);
        Assert.Equal(1, record.History.Count(entry => entry.Transition == SchemaFinalizationTransition.IntentRecorded));
        Assert.Equal(1, record.History.Count(entry => entry.Transition == SchemaFinalizationTransition.Finalized));
    }

    [Fact]
    public async Task A_hold_placed_while_an_intent_is_being_confirmed_abandons_it_and_nothing_finalizes()
    {
        await HoldAsync(null, "so the host admits at 1");
        var evaluating = Fleet("host-a", "1", "2");
        var gate = Gate(Families("2"), evaluating);
        await gate.ActivateAsync(Context());
        await new EfSchemaFinalizationStore(Context()).ReleaseHoldAsync(Family, (await RecordAsync()).Revision, null, "ops");
        evaluating.BeforeCount = async count =>
        {
            // The confirming read, after the intent is durable: an operator places a hold right then.
            var current = await RecordAsync();
            if (current.Intent is not null)
                await new EfSchemaFinalizationStore(Context()).PlaceHoldAsync(Family, current.Revision, null, "canary", "ops", ["1", "2"]);
        };

        await gate.EvaluateAsync(Context());

        var record = await RecordAsync();
        Assert.Equal("1", record.FinalizedVersion);
        Assert.Null(record.Intent);
        Assert.Contains(record.History, entry => entry.Transition == SchemaFinalizationTransition.IntentAbandoned);
    }

    [Fact]
    public async Task A_member_whose_liveness_expires_mid_evaluation_is_not_counted_and_refuses_writes_once_it_refreshes()
    {
        var expiring = Fleet("host-old", "1");
        var old = Gate(Families("1"), expiring);
        await old.ActivateAsync(Context());
        var evaluating = Fleet("host-new", "1", "2");
        var upgraded = Gate(Families("2"), evaluating);
        await upgraded.ActivateAsync(Context());
        // host-old is partitioned: every reader judges it expired from the confirming read on, while it keeps running.
        evaluating.BeforeCount = _ =>
        {
            expiring.Self.Live = false;
            return Task.CompletedTask;
        };

        await upgraded.EvaluateAsync(Context());
        Assert.Equal("2", (await RecordAsync()).FinalizedVersion);

        // SC-008: the partitioned host finds out at its next refresh and refuses every write to the family.
        await old.RefreshAsync(Context());
        var state = old.StateOf(Family)!;
        Assert.True(state.WritesRefused);
        Assert.Equal("2", state.UnreadableFinalizedVersion);
    }

    [Fact]
    public async Task A_lapsed_member_keeps_its_write_version_and_adopts_no_newer_one_until_it_is_admitted_again()
    {
        var lapsing = Fleet("host-a", "1", "2");
        var lapsed = Gate(Families("2"), lapsing);
        await HoldAsync(null, "so the host admits at 1");
        await lapsed.ActivateAsync(Context());
        Assert.Equal("1", lapsed.StateOf(Family)!.WriteVersion);
        lapsing.Self.Lapsed = true;
        lapsing.Self.Live = false;
        await new EfSchemaFinalizationStore(Context()).ReleaseHoldAsync(Family, (await RecordAsync()).Revision, null, "ops");
        await Gate(Families("2"), Fleet("host-b", "1", "2")).ActivateAsync(Context());
        Assert.Equal("2", (await RecordAsync()).FinalizedVersion);

        await lapsed.RefreshAsync(Context());
        Assert.Equal("1", lapsed.StateOf(Family)!.WriteVersion);

        // It rejoins as a new incarnation; the next refresh admits it again, publish first, and only then adopts 2.
        lapsing.Self.Lapsed = false;
        lapsing.Self.Live = true;
        lapsing.Self.Incarnation = "rejoined";
        var publishes = lapsing.Publishes;
        await lapsed.RefreshAsync(Context());
        Assert.True(lapsing.Publishes > publishes);
        Assert.Equal("2", lapsed.StateOf(Family)!.WriteVersion);
    }

    [Fact]
    public async Task Without_a_fleet_the_gate_creates_the_record_and_never_finalizes_past_it()
    {
        var gate = Gate(Families("3"), fleet: null);

        await gate.ActivateAsync(Context());
        await gate.EvaluateAsync(Context());

        Assert.Equal("1", (await RecordAsync()).FinalizedVersion);
        Assert.Equal("1", gate.StateOf(Family)!.WriteVersion);
    }

    [Fact]
    public async Task A_host_whose_report_cannot_be_published_is_refused_before_it_reads_the_record()
    {
        var unpublished = Fleet("host-a", "1", "2");
        unpublished.FailPublish = true;

        var refusal = await Assert.ThrowsAsync<EfSchemaActivationRefusedException>(() => Gate(Families("2"), unpublished).ActivateAsync(Context()));

        Assert.Equal(EfSchemaActivationRefusal.ReportNotPublished, refusal.Refusal);
        Assert.Null(await new EfSchemaFinalizationStore(Context()).FindAsync(Family));
    }

    [Fact]
    public async Task The_report_names_the_database_only_once_the_record_is_read_and_names_none_while_a_second_database_is_being_read()
    {
        var observations = new EfSchemaFinalizationObservations();
        var gate = Gate(Families("2"), Fleet("host-a", "1", "2"), observations);
        Assert.Equal(EfSchemaFamilyObservation.None, observations.Find(Family));

        await gate.ActivateAsync(Context());
        var identity = (await RecordAsync()).DatabaseIdentity;
        Assert.Equal(new EfSchemaFamilyObservation(identity, "2", true), observations.Find(Family));

        // A second database: named nowhere while it is being read, and afterwards neither, since the host now serves both.
        observations.BeginActivation([Family]);
        Assert.Null(observations.Find(Family).DatabaseIdentity);
        observations.Observe(Family, "another-database", "1");
        observations.EndActivation([Family]);
        Assert.Equal(new EfSchemaFamilyObservation(null, null, true), observations.Find(Family));
    }

    /// <summary>
    /// Spec 183's FR-019, amended 2026-09-30: the module is active in the report from admission until its gate stops, and a
    /// gate that is refused, or has not yet admitted, never made it so. It is the fact the backfill's settle condition
    /// counts on (spec 186, FR-012). Stopping forgets what was read, since no gate of the host writes there any more.
    /// </summary>
    [Fact]
    public async Task The_family_is_active_from_the_moment_its_gate_admits_the_module_until_the_gate_stops()
    {
        var observations = new EfSchemaFinalizationObservations();
        var gate = Gate(Families("2"), Fleet("host-a", "1", "2"), observations);
        Assert.False(observations.Find(Family).ModuleActive);

        await gate.ActivateAsync(Context());
        Assert.True(observations.Find(Family).ModuleActive);

        gate.Deactivate();
        gate.Deactivate();
        Assert.Equal(EfSchemaFamilyObservation.None, observations.Find(Family));
    }

    [Fact]
    public async Task A_module_the_gate_refuses_is_never_active()
    {
        var observations = new EfSchemaFinalizationObservations();
        await Gate(Families("3"), Fleet("host-new", "1", "2", "3")).ActivateAsync(Context());
        var refused = Gate(Families("2"), Fleet("host-rolled-back", "1", "2"), observations);

        await Assert.ThrowsAsync<EfSchemaActivationRefusedException>(() => refused.ActivateAsync(Context()));

        Assert.False(observations.Find(Family).ModuleActive);
    }

    /// <summary>
    /// An activation that throws after the module was reported active, here at its first refresh, leaves no migrator with
    /// a gate to stop, so the gate ends the activity itself: a shell that failed to start writes nothing. A later
    /// activation is active again, so what was forgotten does not stay forgotten.
    /// </summary>
    [Fact]
    public async Task An_activation_whose_first_refresh_fails_leaves_the_module_inactive_and_a_later_activation_active()
    {
        var observations = new EfSchemaFinalizationObservations();
        var failing = new FailsReadsWhenArmed();
        var first = Fleet("host-a", "1", "2");
        // Evaluation counts the fleet once the module is admitted; from there the store fails, and only the refresh throws.
        first.BeforeCount = _ =>
        {
            failing.Armed = true;
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gate(Families("2"), first, observations).ActivateAsync(Context(failing)));

        Assert.Equal(EfSchemaFamilyObservation.None, observations.Find(Family));

        failing.Armed = false;
        await Gate(Families("2"), Fleet("host-a", "1", "2"), observations).ActivateAsync(Context());
        Assert.True(observations.Find(Family).ModuleActive);
    }

    [Fact]
    public async Task A_cancelled_activation_leaves_the_module_inactive_and_a_later_activation_active()
    {
        var observations = new EfSchemaFinalizationObservations();
        using var cancellation = new CancellationTokenSource();
        var cancelling = Fleet("host-a", "1", "2");
        cancelling.BeforeCount = _ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Gate(Families("2"), cancelling, observations).ActivateAsync(Context(), cancellation.Token));

        Assert.Equal(EfSchemaFamilyObservation.None, observations.Find(Family));

        await Gate(Families("2"), Fleet("host-a", "1", "2"), observations).ActivateAsync(Context());
        Assert.True(observations.Find(Family).ModuleActive);
    }

    /// <summary>What a gate that has stopped reads afterwards, a refresh on demand among it, does not bring back what stopping forgot.</summary>
    [Fact]
    public async Task A_gate_that_has_stopped_records_nothing_more_it_reads()
    {
        var observations = new EfSchemaFinalizationObservations();
        var gate = Gate(Families("2"), Fleet("host-a", "1", "2"), observations);
        await gate.ActivateAsync(Context());
        gate.Deactivate();

        await gate.RefreshAsync(Context());

        Assert.Equal(EfSchemaFamilyObservation.None, observations.Find(Family));
    }

    /// <summary>A stopped gate that is admitted again by its next refresh, because its member rejoined, does not bring its activity back.</summary>
    [Fact]
    public async Task A_gate_that_has_stopped_is_not_made_active_again_by_a_refresh_that_admits_it_again()
    {
        var observations = new EfSchemaFinalizationObservations();
        var member = Fleet("host-a", "1", "2");
        var gate = Gate(Families("2"), member, observations);
        await gate.ActivateAsync(Context());
        gate.Deactivate();
        member.Self.Incarnation = "rejoined";

        await gate.RefreshAsync(Context());

        Assert.Equal(EfSchemaFamilyObservation.None, observations.Find(Family));
    }

    /// <summary>
    /// The observations' own rules, which every gate of a host shares: each gate is one owner, so a family is active until
    /// the last owner has stopped, whichever way the owners came and went.
    /// </summary>
    [Fact]
    public void A_family_is_active_until_the_last_gate_that_activated_it_has_stopped_however_many_activated_it()
    {
        var observations = new EfSchemaFinalizationObservations();
        var (shellA, shellB) = (Gate(Families("2"), fleet: null), Gate(Families("2"), fleet: null));

        observations.Observe(Family, "database-a", "1");
        Assert.False(observations.Find(Family).ModuleActive);

        observations.Activate(shellA, "database-a", [Family]);
        observations.Activate(shellA, "database-a", [Family]);
        observations.Activate(shellB, "database-a", [Family]);
        Assert.True(observations.Find(Family).ModuleActive);

        observations.Deactivate(shellA, [Family]);
        Assert.True(observations.Find(Family).ModuleActive);

        observations.Deactivate(shellB, [Family]);
        Assert.False(observations.Find(Family).ModuleActive);
    }

    /// <summary>
    /// Two tenants of one host in two databases: the one whose shell stopped is not held active by the one that runs, and is
    /// forgotten with what was read there, so the entry that names the running tenant's database does not apply to the
    /// stopped tenant's, where this host writes nothing.
    /// </summary>
    [Fact]
    public void A_tenant_that_stopped_is_not_active_because_a_tenant_in_another_database_is()
    {
        var observations = new EfSchemaFinalizationObservations();
        var (running, stopped) = (Gate(Families("2"), fleet: null), Gate(Families("2"), fleet: null));
        foreach (var (gate, database) in new[] { (running, "database-x"), (stopped, "database-y") })
        {
            observations.Observe(Family, database, "2");
            observations.Activate(gate, database, [Family]);
        }

        // Both run: the entry speaks for every database, and is active while any gate is.
        Assert.Equal(new EfSchemaFamilyObservation(null, "2", true), observations.Find(Family));

        observations.Deactivate(stopped, [Family]);

        Assert.Equal(new EfSchemaFamilyObservation("database-x", "2", true), observations.Find(Family));

        observations.Deactivate(running, [Family]);

        Assert.Equal(EfSchemaFamilyObservation.None, observations.Find(Family));
    }

    /// <summary>
    /// An activation counts as active from the moment it publishes its report, before it reads the record, since it is about
    /// to write and may do so before a later report says so.
    /// </summary>
    [Fact]
    public async Task An_activation_is_active_in_the_report_it_publishes_before_reading_its_record()
    {
        var observations = new EfSchemaFinalizationObservations();
        var fleet = Fleet("host-a", "1", "2");
        var activeWhenPublishing = new List<bool>();
        fleet.BeforePublish = () =>
        {
            activeWhenPublishing.Add(observations.Find(Family).ModuleActive);
            return Task.CompletedTask;
        };

        await Gate(Families("2"), fleet, observations).ActivateAsync(Context());

        // Every publish of the activation, the one before the record is read among them.
        Assert.NotEmpty(activeWhenPublishing);
        Assert.All(activeWhenPublishing, Assert.True);
    }

    [Fact]
    public void An_activation_in_progress_is_active_until_it_ends_and_leaves_nothing_active_when_it_read_no_record()
    {
        var observations = new EfSchemaFinalizationObservations();

        observations.BeginActivation([Family]);
        Assert.True(observations.Find(Family).ModuleActive);

        observations.EndActivation([Family]);
        Assert.Equal(EfSchemaFamilyObservation.None, observations.Find(Family));
    }

    /// <summary>A database whose gate never got as far as being active, since its activation was refused, is not forgotten: it stays counted where it was read.</summary>
    [Fact]
    public void A_database_no_gate_activated_is_not_forgotten_when_another_gates_stops()
    {
        var observations = new EfSchemaFinalizationObservations();
        var gate = Gate(Families("2"), fleet: null);
        observations.Observe(Family, "database-refused", "1");
        observations.Observe(Family, "database-x", "2");
        observations.Activate(gate, "database-x", [Family]);

        observations.Deactivate(gate, [Family]);

        Assert.Equal(new EfSchemaFamilyObservation("database-refused", "1", false), observations.Find(Family));
    }

    /// <summary>
    /// A shell or host that stops, or a partial generation CShells disposes, stops the loop mid-round: that ends it, and
    /// is never reported as a failure that would mask the reason the container was disposed.
    /// </summary>
    [Fact]
    public async Task Stopping_the_loop_mid_round_ends_it_without_an_exception()
    {
        var gate = Gate(Families("1"), Fleet("host-a", "1"));
        await gate.ActivateAsync(Context());
        using var stopping = new CancellationTokenSource();
        var inRound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var loop = gate.RunAsync(async (_, token) =>
        {
            inRound.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }, stopping.Token);
        await inRound.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await stopping.CancelAsync();

        await loop.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(loop.IsCompletedSuccessfully);
    }

    private FakeFleet Fleet(string hostId, params string[] readable)
    {
        var member = fleet.Add(new FakeMember(hostId).Reading(Family, readable).Reading(OtherFamily, "1"));
        return new FakeFleet(fleet, member);
    }

    private GateContext Context(params IInterceptor[] interceptors)
    {
        var context = SchemaGate.Context(database.ConnectionString, gates: null, interceptors);
        contexts.Add(context);
        return context;
    }

    /// <summary>Fails every read once armed, as a store that has gone away mid-activation does.</summary>
    private sealed class FailsReadsWhenArmed : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            Armed ? throw new InvalidOperationException("The store failed.") : ValueTask.FromResult(result);
    }

    private async Task<SchemaFinalizationRecord> RecordAsync() =>
        await new EfSchemaFinalizationStore(Context()).FindAsync(Family) ?? throw new InvalidOperationException("No record.");

    private Task<SchemaFinalizationRecord> StoreRecordAsync(string initial, string[] chain) =>
        new EfSchemaFinalizationStore(Context()).GetOrCreateAsync(Family, initial, chain, SchemaFinalizationActor.OfOperator("ops"));

    private async Task<SchemaFinalizationRecord> HoldAsync(string? version, string reason)
    {
        var created = await StoreRecordAsync("1", ["1", "2", "3"]);
        return (await new EfSchemaFinalizationStore(Context()).PlaceHoldAsync(Family, created.Revision, version, reason, "ops", ["1", "2", "3"])).Record;
    }

    /// <summary>Every row of the database's tables, as text, so a refusal can be shown to have written nothing.</summary>
    private async Task<string> SnapshotAsync()
    {
        await using var context = SchemaGate.Context(database.ConnectionString);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        var rows = new List<string>();
        foreach (var table in tables)
        {
            await using var select = connection.CreateCommand();
            select.CommandText = $"SELECT * FROM \"{table}\"";
            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add(table + ":" + string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetValue(index)?.ToString())));
        }

        return string.Join("\n", rows.Order(StringComparer.Ordinal));
    }
}
