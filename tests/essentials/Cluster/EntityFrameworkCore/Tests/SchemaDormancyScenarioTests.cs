using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Core.Services;
using Elsa.Cluster.EntityFrameworkCore.Testing;
using Elsa.Cluster.InProcess;
using Elsa.Cluster.Readability;
using Elsa.Cluster.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Primitives.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// Spec 182 end to end, on one host with the in-process membership: a family whose build is at version 2 against a
/// database finalized at 1, a store whose rows carry a <c>Currency</c> only version 2 can hold, a request handler that asks
/// the shared dormancy check where it accepts that field (FR-002), and the real finalization gate, write check and
/// dormancy source underneath. Each scenario is checked in both directions, because the dangerous one is the request that
/// looks like it succeeded while its data was dropped.
/// </summary>
public sealed class SchemaDormancyScenarioTests : IAsyncLifetime
{
    private const string Module = "DormancyModule";
    private const string Feature = "OrdersApi";
    private static readonly string[] Chain = ["1", "2"];
    private readonly string _family = $"Orders{Guid.NewGuid():N}";
    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-dormancy-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly EfSchemaFinalizationGates _gates = new();
    private readonly EfSchemaFinalizationObservations _observations = new();
    private readonly ServiceProvider _services;
    private readonly ClusterSchemaFleet _fleet;
    private readonly EfSchemaModuleGate _gate;
    private readonly SchemaDormancyCheck _check;
    private TaskCompletionSource? _heldRead;

    public SchemaDormancyScenarioTests()
    {
        _services = new ServiceCollection().AddSingleton(_gates).AddScoped(_ => Context()).BuildServiceProvider();
        var declaration = new EfSchemaFamilyDescriptor(_family, Module, "2", typeof(ScenarioUpcaster).Assembly)
        {
            Upcasters = [new EfSchemaUpcasterDescriptor(typeof(ScenarioUpcaster), "1", "2")],
            Rewriter = typeof(OrderRewriter)
        };
        // The host's report carries what its gate observed, as EfSchemaReadabilitySource makes it do for a declared family.
        var membership = new InProcessClusterMembership(
            Options.Create(new ClusterMembershipOptions { HostId = $"dormancy-{Guid.NewGuid():N}" }),
            [new ObservedReadability(declaration, _observations)],
            _time);
        _fleet = new ClusterSchemaFleet(membership);
        _gate = new EfSchemaModuleGate(
            EfSchemaModuleFamilies.FromDeclarations(Module, [declaration]),
            _fleet,
            _observations,
            new EfSchemaFinalizationOptions { EvaluationInterval = TimeSpan.FromSeconds(30), RefreshInterval = TimeSpan.FromSeconds(15) },
            _time);
        _check = new SchemaDormancyCheck(Options.Create(new SchemaDormancyOptions()), new EfObservedSchemaFinalization(_gates));
    }

    private SchemaVersionRequirement NeedsTwo => new(_family, "2");

    public async Task InitializeAsync()
    {
        await using var context = Context();
        await context.Database.EnsureCreatedAsync();
        // The version-1 release created the record: every row in the database is at 1.
        await Store(context).GetOrCreateAsync(_family, "1", Chain, SchemaFinalizationActor.OfOperator("release-1"));
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _file, _file + "-journal", _file + "-wal", _file + "-shm" })
            File.Delete(file);
    }

    /// <summary>User Story 2, acceptance 1 to 3, and User Story 4, acceptance 2; SC-001.</summary>
    [Fact]
    public async Task While_held_a_request_that_sets_a_version_2_field_is_refused_and_saves_nothing_one_without_it_is_saved_at_1_and_after_finalization_the_field_is_saved_and_reads_back()
    {
        await HoldAsync("canary of 2");
        await ActivateAsync();

        var refusal = await Assert.ThrowsAsync<SchemaDormancyRefusedException>(() => PutAsync("with-currency", "EUR"));
        Assert.Equal((Feature, _family, "1", "2"), (refusal.FeatureId, refusal.Family, refusal.WriteVersion, refusal.RequiredVersion));
        Assert.Contains("held by an operator", refusal.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("canary of 2", refusal.Reason, StringComparison.Ordinal);
        Assert.Empty(await RowsAsync());

        await PutAsync("without-currency", null);
        Assert.Equal([("without-currency", "1", null)], await RowsAsync());

        await ReleaseAsync();
        await EvaluateAndRefreshAsync();
        await PutAsync("with-currency", "EUR");

        Assert.Equal([("with-currency", "2", "EUR"), ("without-currency", "1", null)], await RowsAsync());
    }

    /// <summary>User Story 1's reason, from the gate's status, on an operator surface only (FR-008, FR-011).</summary>
    [Fact]
    public async Task While_held_the_operator_reason_names_the_hold_and_who_placed_it()
    {
        await HoldAsync("canary of 2");
        await ActivateAsync();

        var unmet = Assert.Single(_check.Evaluate([NeedsTwo]).Unmet);
        var status = Assert.Single(await _check.ReadStatusAsync(), family => family.Family == _family);

        Assert.Equal(SchemaDormancyKind.Held, unmet.Kind);
        var reason = SchemaDormancyReasons.ForOperator(unmet, status);
        Assert.Contains("canary of 2", reason, StringComparison.Ordinal);
        Assert.Contains("ops@example", reason, StringComparison.Ordinal);
        Assert.Contains("finalized at '1'", reason, StringComparison.Ordinal);
    }

    /// <summary>User Story 2, acceptance 4, and User Story 5; SC-005 and FR-017: the store is the backstop.</summary>
    [Fact]
    public async Task A_unit_of_work_in_which_one_row_needs_dormant_data_writes_no_row_and_the_same_rows_without_it_are_written_at_1()
    {
        await HoldAsync("canary of 2");
        await ActivateAsync();

        // Engine code that never asked the check: the store refuses the whole unit of work, and drops nothing to make it fit.
        var refusal = await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => SaveAsync(new Order("plain", null), new Order("priced", "EUR")));
        Assert.Equal((_family, "1", "2"), (refusal.Family, refusal.WriteVersion, refusal.RequiredVersion));
        Assert.Empty(await RowsAsync());

        await SaveAsync(new Order("plain", null), new Order("priced", null));
        Assert.Equal([("plain", "1", null), ("priced", "1", null)], await RowsAsync());
    }

    /// <summary>User Story 4, acceptance 1; SC-004 and FR-021: a single host finalizes at activation, so nothing is dormant.</summary>
    [Fact]
    public async Task A_single_host_with_no_hold_serves_the_first_request_that_needs_version_2()
    {
        await ActivateAsync();

        Assert.True(_check.Evaluate([NeedsTwo]).IsAvailable);
        await PutAsync("first", "EUR");
        Assert.Equal([("first", "2", "EUR")], await RowsAsync());
    }

    /// <summary>User Story 3; SC-003 and FR-019: no restart and no shell reload, only the gate's own background refresh.</summary>
    [Fact]
    public async Task A_dormant_feature_is_served_within_one_evaluation_interval_of_the_hold_being_released_in_the_same_process()
    {
        await HoldAsync("canary of 2");
        await ActivateAsync();
        var process = Environment.ProcessId;
        await Assert.ThrowsAsync<SchemaDormancyRefusedException>(() => PutAsync("before", "EUR"));
        using var stopping = new CancellationTokenSource();
        // Called directly, the loop returns at its first wait, with its timer already on the fake clock.
        var loop = _gate.RunAsync(WithContextAsync, stopping.Token);
        try
        {
            await ReleaseAsync();
            _time.Advance(TimeSpan.FromSeconds(30));
            await WaitUntilAsync(() => _check.Evaluate([NeedsTwo]).IsAvailable);

            await PutAsync("after", "EUR");
            Assert.Equal([("after", "2", "EUR")], await RowsAsync());
            Assert.Equal(process, Environment.ProcessId);
            Assert.Same(_gate, _gates.FindForContext(typeof(DormancyContext)));
        }
        finally
        {
            await stopping.CancelAsync();
            await loop;
        }
    }

    /// <summary>
    /// FR-014, both ways: within the bound the view stands and the request is refused without a read; past it, the check
    /// reads the record again and serves what another host finalized meanwhile.
    /// </summary>
    [Fact]
    public async Task Before_refusing_the_check_rereads_a_view_older_than_the_bound_and_serves_what_another_host_finalized()
    {
        await HoldAsync("canary of 2");
        await ActivateAsync();
        await ReleaseAsync();
        await FinalizeElsewhereAsync("2");

        await Assert.ThrowsAsync<SchemaDormancyRefusedException>(() => PutAsync("fresh-view", "EUR"));
        Assert.Equal("1", _gate.StateOf(_family)!.WriteVersion);

        _time.Advance(new SchemaDormancyOptions().RefreshBound);
        await PutAsync("stale-view", "EUR");

        Assert.Equal("2", _gate.StateOf(_family)!.WriteVersion);
        Assert.Equal([("stale-view", "2", "EUR")], await RowsAsync());
    }

    [Fact]
    public async Task Concurrent_refreshes_on_demand_share_one_read_per_bound()
    {
        await ActivateAsync();
        var bound = TimeSpan.FromSeconds(2);
        _time.Advance(bound);

        // The first read is held open until every caller has asked, so they all find the view stale and contend.
        _heldRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = Enumerable.Range(0, 4).Select(_ => _gate.RefreshIfOlderThanAsync(bound)).ToArray();
        _heldRead.SetResult();
        var reads = await Task.WhenAll(refreshes);

        Assert.Equal(1, reads.Count(read => read));
        Assert.False(await _gate.RefreshIfOlderThanAsync(bound));
    }

    /// <summary>User Story 6; FR-005, FR-016 and SC-007: finalization alone is not enough for a query over version 2's data.</summary>
    [Fact]
    public async Task A_query_that_needs_completeness_is_refused_after_finalization_until_the_finish_record_names_2_and_then_answers()
    {
        await SaveAsync(new Order("old", null));
        await ActivateAsync();
        var needsComplete = new SchemaVersionRequirement(_family, "2", requiresCompleteness: true);

        Assert.True(_check.Evaluate([NeedsTwo]).IsAvailable, "Version 2 is finalized at activation on a single host.");
        var refusal = await Assert.ThrowsAsync<SchemaDormancyRefusedException>(() => _check.EnsureAvailableAsync([needsComplete], "OrdersByCurrency").AsTask());
        Assert.Contains("existing records", refusal.Reason, StringComparison.Ordinal);

        await CompleteElsewhereAsync("2");
        _time.Advance(new SchemaDormancyOptions().RefreshBound);

        await _check.EnsureAvailableAsync([needsComplete], "OrdersByCurrency");
        Assert.Equal("2", _check.Observe().Single(family => family.Family == _family).CompletionVersion);
    }

    /// <summary>
    /// Spec 186, User Story 2 and User Story 7; SC-007, FR-012 and FR-017: spec 182's User Story 6 with the backfill running
    /// instead of a stub, on a single host with the in-process membership. The case that looks like success comes first:
    /// every row is already at 2 after the upgrade pass, yet the query stays dormant, with the upgrade-in-progress reason,
    /// until the verification pass after the settle margin records the completion; then it answers from every row. No
    /// membership table is created.
    /// </summary>
    [Fact]
    public async Task With_the_backfill_running_a_query_that_needs_completeness_is_served_only_once_the_finish_record_names_2()
    {
        await SaveAsync(new Order("old-1", null), new Order("old-2", null));
        await ActivateAsync();
        // The gate's status carries the backfill's once it is built over the gate (spec 186, FR-021).
        var backfill = new EfSchemaBackfill(_gate, _fleet, new EfSchemaBackfillOptions(), _time);
        var needsComplete = new SchemaVersionRequirement(_family, "2", requiresCompleteness: true);

        await backfill.RunOnceAsync(WithScopeAsync);

        Assert.All(await RowsAsync(), row => Assert.Equal(("2", "EUR"), (row.SchemaVersion, row.Currency)));
        var refusal = await Assert.ThrowsAsync<SchemaDormancyRefusedException>(() => _check.EnsureAvailableAsync([needsComplete], "OrdersByCurrency").AsTask());
        Assert.Contains("existing records", refusal.Reason, StringComparison.Ordinal);
        var unmet = Assert.Single(_check.Evaluate([needsComplete]).Unmet);
        var status = Assert.Single(await _check.ReadStatusAsync(), family => family.Family == _family);
        Assert.Contains("The backfill is settling towards '2'", SchemaDormancyReasons.ForOperator(unmet, status), StringComparison.Ordinal);

        _time.Advance(TimeSpan.FromSeconds(34));
        await backfill.RunOnceAsync(WithScopeAsync);
        Assert.False(_check.Evaluate([needsComplete]).IsAvailable);

        _time.Advance(TimeSpan.FromSeconds(1));
        await backfill.RunOnceAsync(WithScopeAsync);

        await _check.EnsureAvailableAsync([needsComplete], "OrdersByCurrency");
        Assert.Equal("2", _check.Observe().Single(family => family.Family == _family).CompletionVersion);
        Assert.Equal(["EUR", "EUR"], (await RowsAsync()).Select(row => row.Currency));
        await using var context = Context();
        var tables = await context.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'").ToListAsync();
        Assert.DoesNotContain(tables, table => table.Contains("cluster", StringComparison.OrdinalIgnoreCase) || table.Contains("member", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Edge case "A host that cannot read the finalized version": not dormancy, and never available.</summary>
    [Fact]
    public async Task A_family_finalized_at_a_version_this_build_cannot_read_is_reported_as_refusing_writes_and_its_requirement_stays_unmet()
    {
        await ActivateAsync();
        await using (var context = Context())
        {
            var store = Store(context);
            var record = (await store.FindAsync(_family))!;
            var intended = await store.RecordIntentAsync(_family, record.Revision, "3", ["1", "2", "3"], new SchemaFinalizationMember("newer", "n"));
            await store.CommitIntentAsync(_family, intended.Record.Revision, ["1", "2", "3"], new SchemaFinalizationMember("newer", "n"));
        }

        await RefreshAsync();

        var observed = _check.Observe().Single(family => family.Family == _family);
        Assert.True(observed.WritesRefused);
        Assert.Equal(SchemaDormancyKind.WritesRefused, Assert.Single(_check.Evaluate([NeedsTwo]).Unmet).Kind);
    }

    /// <summary>The API's handler: a request that sets <c>Currency</c> needs version 2, and asks before anything is written (FR-002).</summary>
    private async Task PutAsync(string id, string? currency)
    {
        if (currency is not null)
            await _check.EnsureAvailableAsync([NeedsTwo], Feature);
        await SaveAsync(new Order(id, currency));
    }

    /// <summary>
    /// The store, in one unit of work: a row that carries a currency is in version 2's format, and one without is written in
    /// the format of the write version the gate holds this host to.
    /// </summary>
    private async Task SaveAsync(params Order[] orders)
    {
        await using var context = Context();
        var writeVersion = _gate.StateOf(_family)?.WriteVersion ?? "1";
        foreach (var order in orders)
            context.Orders.Add(new OrderRow { Id = order.Id, Currency = order.Currency, SchemaVersion = order.Currency is null ? writeVersion : "2" });
        await context.SaveChangesAsync();
    }

    private async Task<(string Id, string SchemaVersion, string? Currency)[]> RowsAsync()
    {
        await using var context = Context();
        return (await context.Orders.AsNoTracking().ToListAsync())
            .OrderBy(row => row.Id, StringComparer.Ordinal)
            .Select(row => (row.Id, row.SchemaVersion, row.Currency))
            .ToArray();
    }

    /// <summary>What the module's migrator does once the schema is current: admit, give the gate its contexts, register it.</summary>
    private async Task ActivateAsync()
    {
        await using (var context = Context())
            await _gate.ActivateAsync(context);
        _gate.UseContexts(WithContextAsync);
        _gates.Register(typeof(DormancyContext), _gate);
    }

    private Task EvaluateAndRefreshAsync() => WithContextAsync(async context =>
    {
        await _gate.EvaluateAsync(context);
        await _gate.RefreshAsync(context);
    }, CancellationToken.None);

    private Task RefreshAsync() => WithContextAsync(context => _gate.RefreshAsync(context), CancellationToken.None);

    private Task HoldAsync(string reason) => WithStoreAsync(async store =>
        await store.PlaceHoldAsync(_family, (await store.FindAsync(_family))!.Revision, null, reason, "ops@example", Chain));

    /// <summary>What the CLI's release does: it writes the record directly.</summary>
    private Task ReleaseAsync() => WithStoreAsync(async store =>
        await store.ReleaseHoldAsync(_family, (await store.FindAsync(_family))!.Revision, null, "ops@example"));

    /// <summary>Another host's evaluator finalizing <paramref name="version"/>, which this host has not observed yet.</summary>
    private Task FinalizeElsewhereAsync(string version) => WithStoreAsync(async store =>
    {
        var member = new SchemaFinalizationMember("elsewhere", "e");
        var intended = await store.RecordIntentAsync(_family, (await store.FindAsync(_family))!.Revision, version, Chain, member);
        await store.CommitIntentAsync(_family, intended.Record.Revision, Chain, member);
    });

    /// <summary>The post-finalization backfill recording that no row below <paramref name="version"/> remains (spec 186, FR-014).</summary>
    private Task CompleteElsewhereAsync(string version) => WithStoreAsync(async store =>
        await store.RecordCompletionAsync(
            _family, (await store.FindAsync(_family))!.Revision, version, _time.GetUtcNow(), _time.GetUtcNow(), Chain, new SchemaFinalizationMember("backfill", "b")));

    private async Task WithStoreAsync(Func<EfSchemaFinalizationStore, Task> action)
    {
        await using var context = Context();
        await action(Store(context));
    }

    private async Task WithContextAsync(Func<DbContext, Task> action, CancellationToken cancellationToken)
    {
        if (_heldRead is { } held)
            await held.Task;
        await using var context = Context();
        await action(context);
    }

    private EfSchemaFinalizationStore Store(DbContext context) => new(context, _time);

    private async Task WithScopeAsync(Func<EfSchemaBackfillScope, Task> action, CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        await action(new EfSchemaBackfillScope(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<DormancyContext>()));
    }

    private static Task WaitUntilAsync(Func<bool> condition) =>
        Polling.UntilAsync(condition, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(20), "The gate's background refresh did not end the dormancy.");

    private DormancyContext Context()
    {
        var builder = new DbContextOptionsBuilder<DormancyContext>()
            .UseSqlite($"Data Source={_file}")
            .UseApplicationServiceProvider(_services);
        EfSchemaWriteGateInterceptor.EnsureAdded(builder);
        return new DormancyContext(builder.Options);
    }

    private sealed record Order(string Id, string? Currency);

    /// <summary>
    /// The family's rewriter (spec 186, FR-004): a row an older writer left at 1 carries no currency, and version 2's write
    /// path fills the projection with the currency every version-1 order implicitly had.
    /// </summary>
    private sealed class OrderRewriter(DormancyContext context, EfSchemaFinalizationGates gates) : IEfSchemaRowRewriter
    {
        public async ValueTask<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, CancellationToken cancellationToken = default)
        {
            var id = (string)row.Key[0]!;
            var stored = await context.Orders.SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
            if (stored is null)
                return EfSchemaRewriteOutcome.Missing;
            var gate = gates.FindModuleGate(typeof(DormancyContext))!;
            var chain = gate.Families.Chains.Single();
            EfSchemaVersion.EnsureReadable(chain, stored.SchemaVersion);
            if (chain.IsAtOrAfter(stored.SchemaVersion, row.TargetVersion))
                return EfSchemaRewriteOutcome.AlreadyCurrent;
            stored.SchemaVersion = gate.StateOf(chain.Family)!.WriteVersion;
            stored.Currency ??= "EUR";
            await context.SaveChangesAsync(cancellationToken);
            return EfSchemaRewriteOutcome.Rewritten;
        }
    }

    /// <summary>The host's readability for the one scenario family, with the database and finalized version its gate observed.</summary>
    private sealed class ObservedReadability(EfSchemaFamilyDescriptor declaration, EfSchemaFinalizationObservations observations) : IMemberReportSource<ReadabilitySection>
    {
        public ValueTask<ReadabilitySection> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(EfSchemaReadabilitySource.Read([declaration], observations: observations));
    }

    private sealed class OrderRow
    {
        public string Id { get; set; } = "";

        public string SchemaVersion { get; set; } = "";

        /// <summary>A member only version 2 of the family can hold.</summary>
        public string? Currency { get; set; }
    }

    private sealed class DormancyContext(DbContextOptions<DormancyContext> options) : DbContext(options)
    {
        public DbSet<OrderRow> Orders => Set<OrderRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrderRow>(row =>
            {
                row.ToTable("dormancy_orders");
                row.HasKey(entity => entity.Id);
                row.Property(entity => entity.SchemaVersion).HasMaxLength(32).IsRequired();
            });
            modelBuilder.MapSchemaFinalization(Module).IndexSchemaVersionStamps();
        }
    }
}
