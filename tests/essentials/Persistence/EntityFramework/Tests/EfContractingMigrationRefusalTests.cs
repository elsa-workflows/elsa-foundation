using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.ContractingModule;
using static Elsa.Persistence.EntityFramework.Tests.ContractingSeedScenarios;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Spec 185, FR-024 and User Story 5, SC-006: a pending contracting migration is refused, naming its family and the
/// version it waits for, wherever a module's migrations are applied or validated — <see cref="EfModuleMigrator{TContext}"/>
/// at Prepare under both policies, and <c>dotnet elsa persistence apply</c> and <c>validate</c> — while the family is
/// finalized below that version in the target database, and applies once it reaches it. Every refusal is checked to have
/// run nothing, since a refusal that half-applied its batch would look exactly like one that works.
/// </summary>
public sealed class EfContractingMigrationRefusalTests : IAsyncLifetime
{
    private const string Provider = "Sqlite";

    private readonly TemporarySqliteDatabase _database = new("contracting");
    private readonly List<ServiceProvider> _hosts = [];

    private string Connection => _database.ConnectionString;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
            await host.DisposeAsync();
        await _database.DisposeAsync();
    }

    // --- EfModuleMigrator at Prepare (User Story 5, scenarios 1, 3 and 5) ---------------------------------------------

    [Fact]
    public async Task Under_automigrate_the_migrator_refuses_the_contracting_migration_until_its_family_is_finalized_and_then_applies_it()
    {
        await StageAsync(Provider, Connection, DropObsolete);
        var migrator = Migrator(EfMigratePolicy.AutoMigrate);
        var before = await SnapshotAsync();

        var refusal = await Assert.ThrowsAsync<EfContractingMigrationRefusedException>(() => migrator.InitializeAsync());

        AssertNamesTheFamilyAndBothVersions(refusal, finalized: EarlierVersion);
        Assert.Null(migrator.Gate);
        Assert.Equal(before, await SnapshotAsync());

        await FinalizeAsync(Provider, Connection, CurrentVersion);
        await migrator.InitializeAsync();

        Assert.Equal([Initial, Expand, DropObsolete, Contract], await AppliedAsync(Provider, Connection));
        Assert.False(await HasColumnAsync(Provider, Connection, RowsTable, "Legacy"));
        Assert.NotNull(migrator.Gate);
    }

    /// <summary>
    /// Scenario 5: the context's pending migrations apply through one <c>MigrateAsync</c>, so the safe migrations queued
    /// ahead of the contracting one are withheld with it rather than applied ahead of the refusal.
    /// </summary>
    [Fact]
    public async Task The_refusal_withholds_the_whole_pending_batch_including_the_safe_migrations_queued_ahead_of_it()
    {
        await StageAsync(Provider, Connection, Initial);
        var before = await SnapshotAsync();

        var refusal = await Assert.ThrowsAsync<EfContractingMigrationRefusedException>(() => Migrator(EfMigratePolicy.AutoMigrate).InitializeAsync());

        Assert.Equal([Expand, DropObsolete, Contract], refusal.Pending);
        Assert.Equal([Initial], await AppliedAsync(Provider, Connection));
        Assert.False(await HasColumnAsync(Provider, Connection, RowsTable, "Replacement"));
        Assert.True(await HasColumnAsync(Provider, Connection, NotesTable, "Obsolete"));
        Assert.Equal(before, await SnapshotAsync());
    }

    /// <summary>
    /// Under <c>Validate</c> the migrator applies nothing either way; what changes is what it reports. Before finalization
    /// it names the family and version the batch waits for, rather than pointing at an <c>apply</c> that would refuse too.
    /// After, the batch is only pending, and once applied out of process the module activates.
    /// </summary>
    [Fact]
    public async Task Under_validate_the_migrator_names_the_family_and_version_the_batch_waits_for_and_once_finalized_reports_it_only_as_pending()
    {
        await StageAsync(Provider, Connection, DropObsolete);
        var migrator = Migrator(EfMigratePolicy.Validate);

        AssertNamesTheFamilyAndBothVersions(
            await Assert.ThrowsAsync<EfContractingMigrationRefusedException>(() => migrator.InitializeAsync()),
            finalized: EarlierVersion);

        await FinalizeAsync(Provider, Connection, CurrentVersion);
        // Exactly the pending signal, not the contracting refusal that derives from it.
        var pending = await Assert.ThrowsAsync<EfPendingMigrationsException>(() => migrator.InitializeAsync());
        Assert.Contains(Contract, pending.Message, StringComparison.Ordinal);

        await using (var context = Create(Provider, Connection))
            await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite);
        await migrator.InitializeAsync();

        Assert.NotNull(migrator.Gate);
        Assert.False(await HasColumnAsync(Provider, Connection, RowsTable, "Legacy"));
    }

    // --- Where there is nothing to protect, and what is not contracting --------------------------------------------

    /// <summary>
    /// A fresh install of a release that carries a contracting migration applies it with the rest: no host has admitted
    /// the module in this database, so nothing reads what it removes, and the migrator creates the family's record at the
    /// version the contraction names before the contraction runs, which the gate then admits the module against.
    /// </summary>
    [Fact]
    public async Task A_fresh_database_takes_the_contracting_migration_with_the_rest_of_its_batch()
    {
        var migrator = Migrator(EfMigratePolicy.AutoMigrate);

        await migrator.InitializeAsync();

        Assert.Equal([Initial, Expand, DropObsolete, Contract], await AppliedAsync(Provider, Connection));
        AssertSeeded(await RecordAsync(Provider, Connection), MachineMigrator);
        Assert.NotNull(migrator.Gate);
    }

    /// <summary>
    /// Tables that exist without a database identity were migrated, but no gate-aware host has admitted the module: nothing
    /// is pending before the contraction, so the record is created at once and the contraction runs.
    /// </summary>
    [Fact]
    public async Task A_database_whose_module_no_host_has_admitted_yet_takes_the_contracting_migration()
    {
        await StageAsync(Provider, Connection, DropObsolete, finalized: null);

        await using (var context = Create(Provider, Connection))
            await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite);

        Assert.Contains(Contract, await AppliedAsync(Provider, Connection));
        AssertSeeded(await RecordAsync(Provider, Connection), MachineMigrator);
    }

    // --- The migrator seeds first where no host has admitted the module (#2136) ------------------------------------

    [Fact]
    public Task Persistence_apply_on_a_fresh_database_creates_the_record_before_the_contraction_so_an_older_release_starting_next_is_refused() =>
        ApplyThenAnOlderReleaseStartsAsync(Provider, Connection);

    /// <summary>A host, named by its member in the fleet, whose process ends between the seed and the contraction.</summary>
    [Fact]
    public Task A_host_that_crashes_between_the_seed_and_the_contraction_leaves_an_older_release_refused_and_the_next_start_completes()
    {
        var fleet = new FakeFleetState();
        var host = fleet.Add(new FakeMember("host-new").Reading(Family, Chain));
        return ACrashBetweenTheSeedAndTheContractionAsync(
            Provider,
            Connection,
            interceptors => Migrator(EfMigratePolicy.AutoMigrate, new FakeFleet(fleet, host), interceptors).InitializeAsync(),
            EfContractingMigrationCheck.MigratorHostIdPrefix + host.HostId);
    }

    [Fact]
    public Task An_older_release_whose_gate_creates_the_record_first_keeps_the_contraction_from_running() =>
        AnOlderReleaseThatCreatesTheRecordFirstKeepsTheContractionFromRunningAsync(Provider, Connection);

    [Fact]
    public Task An_older_release_that_starts_after_the_seed_is_refused_and_the_contraction_runs() =>
        AnOlderReleaseThatStartsAfterTheSeedIsRefusedAndTheContractionRunsAsync(Provider, Connection);

    /// <summary>
    /// Two families contracted by two migrations of one pending batch: each family's record is created, in the order of its
    /// contracting migration, before the first contraction runs. Neither contraction runs while a family lacks its record,
    /// and the second family is not refused for want of one once the first seed has created the database identity.
    /// </summary>
    [Fact]
    public async Task Several_contracted_families_are_each_seeded_in_migration_order_before_any_contraction_runs()
    {
        IReadOnlyDictionary<string, SchemaFinalizationRecord>? atFirstContraction = null;
        ContractingProbe.WhenApplying<PairContractFirstMigration>(
            () => atFirstContraction = Task.Run(() => ContractingPairModule.RecordsAsync(Connection)).GetAwaiter().GetResult());
        var created = new RecordsCreated();

        await using (var context = ContractingPairModule.Create(Connection, created))
            await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite);

        Assert.Equal([ContractingPairModule.First, ContractingPairModule.Second], created.Families);
        Assert.Equal([ContractingPairModule.First, ContractingPairModule.Second], atFirstContraction!.Keys.Order(StringComparer.Ordinal));
        Assert.All(atFirstContraction.Values, record => AssertSeeded(record, MachineMigrator));
        Assert.Equal(
            [ContractingPairModule.Initial, ContractingPairModule.ContractFirst, ContractingPairModule.ContractSecond],
            await ContractingPairModule.AppliedAsync(Connection));
    }

    /// <summary>A racing older gate that leaves the families below their versions refuses the whole batch: no contraction of either runs.</summary>
    [Fact]
    public async Task A_racing_gate_that_leaves_the_families_below_their_versions_keeps_every_contraction_from_running()
    {
        await using (var context = ContractingPairModule.Create(Connection, new BeforeFirstSave(async () =>
                     {
                         await using var older = ContractingPairModule.Create(Connection);
                         await ContractingPairModule.OlderGate().ActivateAsync(older);
                     })))
        {
            var refusal = await Assert.ThrowsAsync<EfContractingMigrationRefusedException>(() => EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite));

            Assert.Equal([ContractingPairModule.ContractFirst, ContractingPairModule.ContractSecond], refusal.Refusals.Select(entry => entry.Migration));
            Assert.All(refusal.Refusals, entry => Assert.Equal((EfContractingMigrationRefusalReason.NotFinalized, EarlierVersion), (entry.Reason, entry.FinalizedVersion)));
            Assert.Equal([ContractingPairModule.Initial], refusal.Applied);
        }

        Assert.Equal([ContractingPairModule.Initial], await ContractingPairModule.AppliedAsync(Connection));
    }

    /// <summary>
    /// A build whose own chain cannot place the version its contracting migration names seeds nothing: it is refused
    /// before anything runs, so no record is created at a version it cannot place and nothing is migrated.
    /// </summary>
    [Fact]
    public async Task A_contracting_migration_whose_version_the_build_cannot_place_is_refused_before_anything_runs_and_seeds_nothing()
    {
        var readsOnlyTheEarlierVersion = Gate(EarlierVersion).Families;

        await using (var context = Create(Provider, Connection))
        {
            var refusal = await Assert.ThrowsAsync<EfContractingMigrationRefusedException>(
                () => EfContractingMigrationCheck.MigrateAsync(context, host: null, readsOnlyTheEarlierVersion, CancellationToken.None));

            Assert.Equal(
                new EfContractingMigrationRefusal(Contract, Family, CurrentVersion, null, EfContractingMigrationRefusalReason.UnknownVersion),
                Assert.Single(refusal.Refusals));
            Assert.Empty(refusal.Applied);
        }

        Assert.Empty(await AppliedAsync(Provider, Connection));
        Assert.Null(await RecordAsync(Provider, Connection));
    }

    /// <summary>
    /// The first step applies what comes before the contraction and nothing else, and never reverts: EF's own targeted
    /// migrate reverts every applied migration after its target, which a concurrent migrator may have applied by the time
    /// EF's lock is taken. Both ways, on a database already at its last migration: EF's sets out to revert the
    /// contraction, which these hand-written migrations refuse for want of a <c>Down</c> that a generated one would run.
    /// </summary>
    [Fact]
    public async Task The_step_before_the_contraction_never_reverts_what_another_migrator_applied_past_it()
    {
        await StageAsync(Provider, Connection, Contract, finalized: null);

        await using (var context = Create(Provider, Connection))
            await EfContractingMigrationCheck.MigrateBeforeAsync(context, Contract, CancellationToken.None);
        Assert.Equal([Initial, Expand, DropObsolete, Contract], await AppliedAsync(Provider, Connection));

        await using (var context = Create(Provider, Connection))
        {
            var revert = await Assert.ThrowsAsync<NotSupportedException>(() => context.GetService<IMigrator>().MigrateAsync(DropObsolete));
            Assert.Contains("'Down' method", revert.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Once the module has been admitted, which a family's record shows, a family with no record has nothing to show its
    /// version finalized, and the whole batch is refused: here the second of two families, the first admitted at its version.
    /// </summary>
    [Fact]
    public async Task Once_the_module_has_been_admitted_a_family_with_no_record_is_refused()
    {
        await using (var context = ContractingPairModule.Create(Connection))
        {
            await context.GetService<IMigrator>().MigrateAsync(ContractingPairModule.Initial);
            await EfSchemaFinalizationTestSupport.FinalizeAsync(new EfSchemaFinalizationStore(context), ContractingPairModule.First, Chain, CurrentVersion);
        }

        await using (var context = ContractingPairModule.Create(Connection))
        {
            var refusal = await Assert.ThrowsAsync<EfContractingMigrationRefusedException>(() => EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite));
            Assert.Equal(
                new EfContractingMigrationRefusal(ContractingPairModule.ContractSecond, ContractingPairModule.Second, CurrentVersion, null, EfContractingMigrationRefusalReason.NoRecord),
                Assert.Single(refusal.Refusals));
        }

        Assert.Equal([ContractingPairModule.Initial], await ContractingPairModule.AppliedAsync(Connection));
    }

    /// <summary>
    /// A database identity with no record is an admission, or a seed, that ended before its first record: nothing has read
    /// the module's rows, so the next apply seeds again and completes, rather than refusing a family for want of a record.
    /// </summary>
    [Fact]
    public async Task A_seed_that_ended_after_the_identity_and_before_the_record_is_completed_by_the_next_apply()
    {
        await using (var context = Create(Provider, Connection, new EndTheProcessOnceWritten(EfSchemaFinalization.DatabaseIdentityTableName(HistoryModule))))
            await Assert.ThrowsAsync<ProcessEndedException>(() => EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite));
        Assert.Null(await RecordAsync(Provider, Connection));

        await using (var context = Create(Provider, Connection))
            await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite);

        Assert.Contains(Contract, await AppliedAsync(Provider, Connection));
        AssertSeeded(await RecordAsync(Provider, Connection), MachineMigrator);
    }

    /// <summary>A finalized version older than anything this build's chain reads is below the version the migration names.</summary>
    [Fact]
    public async Task A_family_finalized_at_a_version_older_than_this_builds_chain_is_refused()
    {
        await StageAsync(Provider, Connection, DropObsolete, finalized: null);
        await FinalizeAsync(Provider, Connection, "0", chain: ["0", EarlierVersion, CurrentVersion]);

        var refusal = await ApplyExpectingRefusalAsync();

        Assert.Equal(
            new EfContractingMigrationRefusal(Contract, Family, CurrentVersion, "0", EfContractingMigrationRefusalReason.NotFinalized),
            Assert.Single(refusal.Refusals));
    }

    /// <summary>
    /// Scenario 4: an opt-out that names no family and version is not contracting, so the check does not hold it back even
    /// while the family is below the version a contracting migration beside it names.
    /// </summary>
    [Fact]
    public async Task An_opt_out_that_names_no_family_is_not_held_back_and_only_the_contracting_migration_is_named()
    {
        await StageAsync(Provider, Connection, Expand);
        await using var context = Create(Provider, Connection);

        Assert.Null(await EfContractingMigrationCheck.FindRefusalAsync(context, [DropObsolete]));
        var refusal = await EfContractingMigrationCheck.FindRefusalAsync(context, [DropObsolete, Contract]);

        Assert.Equal([Contract], refusal!.Refusals.Select(entry => entry.Migration));
    }

    /// <summary>The check judges what would run: a contracting migration already applied is never refused again.</summary>
    [Fact]
    public async Task A_contracting_migration_already_applied_is_not_judged_again()
    {
        await StageAsync(Provider, Connection, Contract);

        await using var context = Create(Provider, Connection);
        await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite, EfMigratePolicy.Validate);
        await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite);
    }

    // --- What the family's record starts at once a contraction has applied (spec 181's record creation) -----------

    /// <summary>
    /// Three hosts and a database no host has run the module in. host-old runs the release before the family's change, so
    /// it reads only <see cref="EarlierVersion"/>, and it is live in the fleet: the family cannot finalize past that
    /// version while it is. host-new runs the contracting release and is the first to run the module on this database, so
    /// nothing refuses the contraction, and its migrator creates the family's record before it, as host-new's migrator.
    /// The record must start at <see cref="CurrentVersion"/>, which the contraction names, not at
    /// <see cref="EarlierVersion"/>, the oldest version host-new reads, since the schema no longer serves that. So
    /// host-late, starting later on the older release, is refused rather than admitted against a schema without the
    /// column it reads.
    /// </summary>
    [Fact]
    public async Task A_contraction_applied_on_a_fresh_database_starts_its_familys_record_at_its_version_so_an_older_host_is_refused()
    {
        var fleet = new FakeFleetState();
        fleet.Add(new FakeMember("host-old").Reading(Family, EarlierVersion)).Published = true;
        var newer = Migrator(EfMigratePolicy.AutoMigrate, new FakeFleet(fleet, fleet.Add(new FakeMember("host-new").Reading(Family, Chain))));

        await newer.InitializeAsync();

        Assert.False(await HasColumnAsync(Provider, Connection, RowsTable, "Legacy"));
        AssertSeeded(await RecordAsync(Provider, Connection), EfContractingMigrationCheck.MigratorHostIdPrefix + "host-new");
        var before = await SnapshotAsync();

        var late = Gate(EarlierVersion, new FakeFleet(fleet, fleet.Add(new FakeMember("host-late").Reading(Family, EarlierVersion))));
        await using var context = Create(Provider, Connection);
        var refusal = await Assert.ThrowsAsync<EfSchemaActivationRefusedException>(() => late.ActivateAsync(context));

        Assert.Equal((EfSchemaActivationRefusal.FinalizedUnreadable, Family, CurrentVersion), (refusal.Refusal, refusal.Family, refusal.Version));
        Assert.Equal(before, await SnapshotAsync());
    }

    /// <summary>
    /// The gate's own floor, for a contraction applied outside Elsa's migrator, as SQL from <c>dotnet elsa persistence
    /// script</c> is, so no record was created before it. Both ways: a contraction still pending leaves the schema serving
    /// the oldest version the host reads, and the record starts there; once it has applied, the record starts at the
    /// version it names.
    /// </summary>
    [Theory]
    [InlineData(DropObsolete, EarlierVersion)]
    [InlineData(Contract, CurrentVersion)]
    public async Task A_record_created_where_no_host_has_admitted_the_module_starts_at_the_version_an_applied_contraction_names(string appliedThrough, string startsAt)
    {
        await StageAsync(Provider, Connection, appliedThrough, finalized: null);

        await using (var context = Create(Provider, Connection))
            await Gate(CurrentVersion).ActivateAsync(context);

        Assert.Equal(startsAt, (await RecordAsync(Provider, Connection))!.FinalizedVersion);
    }

    /// <summary>
    /// A host that cannot place the version a contraction applied outside Elsa's migrator names cannot tell which of its
    /// versions the schema still serves, so it is refused rather than starting the record at the oldest version it reads,
    /// and creates no record.
    /// </summary>
    [Fact]
    public async Task A_host_whose_chain_does_not_read_the_version_an_applied_contraction_names_is_refused_and_creates_no_record()
    {
        await StageAsync(Provider, Connection, Contract, finalized: null);

        await using var context = Create(Provider, Connection);
        var refusal = await Assert.ThrowsAsync<EfSchemaActivationRefusedException>(() => Gate(EarlierVersion).ActivateAsync(context));

        Assert.Equal((EfSchemaActivationRefusal.ContractedUnreadable, Family, CurrentVersion), (refusal.Refusal, refusal.Family, refusal.Version));
        Assert.Contains($"removed what every version before '{CurrentVersion}' reads, and this host reads only [{EarlierVersion}]", refusal.Message, StringComparison.Ordinal);
        Assert.Null(await RecordAsync(Provider, Connection));
    }

    // --- dotnet elsa persistence (spec 171's apply and validate) -------------------------------------------------

    [Fact]
    public async Task Persistence_apply_refuses_the_batch_as_a_refusal_naming_the_family_and_version_and_applies_it_once_finalized()
    {
        await StageAsync(Provider, Connection, Expand);
        var before = await SnapshotAsync();

        var refused = await ToolAsync(Provider, Connection, "apply");

        Assert.Equal(EfToolingExitCode.Refusal, refused.ExitCode);
        var error = refused.Response.GetProperty("error");
        Assert.Equal("contracting-migration-refused", error.GetProperty("code").GetString());
        AssertNamesTheFamilyAndBothVersions(Assert.Single(error.GetProperty("details").EnumerateArray()).GetString()!, finalized: EarlierVersion);
        Assert.Equal(before, await SnapshotAsync());

        await FinalizeAsync(Provider, Connection, CurrentVersion);
        var applied = await ToolAsync(Provider, Connection, "apply");

        Assert.Equal(EfToolingExitCode.Success, applied.ExitCode);
        Assert.Equal([Initial, Expand, DropObsolete, Contract], await AppliedAsync(Provider, Connection));
    }

    [Fact]
    public async Task Persistence_validate_reports_the_withheld_batch_as_pending_naming_the_family_and_version()
    {
        await StageAsync(Provider, Connection, DropObsolete);

        var run = await ToolAsync(Provider, Connection, "validate");

        Assert.Equal(EfToolingExitCode.NegativeResult, run.ExitCode);
        var error = run.Response.GetProperty("error");
        Assert.Equal("pending-migrations", error.GetProperty("code").GetString());
        AssertNamesTheFamilyAndBothVersions(Assert.Single(error.GetProperty("details").EnumerateArray()).GetString()!, finalized: EarlierVersion);
    }

    private static void AssertNamesTheFamilyAndBothVersions(EfContractingMigrationRefusedException refusal, string finalized)
    {
        Assert.Equal(Name, refusal.Module);
        Assert.Equal(
            new EfContractingMigrationRefusal(Contract, Family, CurrentVersion, finalized, EfContractingMigrationRefusalReason.NotFinalized),
            Assert.Single(refusal.Refusals));
        AssertNamesTheFamilyAndBothVersions(refusal.Message, finalized);
    }

    private static void AssertNamesTheFamilyAndBothVersions(string message, string finalized) =>
        Assert.Contains(
            $"'{Contract}' removes what schema family '{Family}' reads before '{CurrentVersion}', and '{Family}' is finalized at " +
            $"'{finalized}' in this database, not at '{CurrentVersion}' or later",
            message,
            StringComparison.Ordinal);

    private async Task<EfContractingMigrationRefusedException> ApplyExpectingRefusalAsync()
    {
        await using var context = Create(Provider, Connection);
        return await Assert.ThrowsAsync<EfContractingMigrationRefusedException>(() => EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite));
    }

    /// <summary>The family of every finalization record the context it is added to creates, in the order it creates them.</summary>
    private sealed class RecordsCreated : SaveChangesInterceptor
    {
        public List<string> Families { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Families.AddRange(eventData.Context!.ChangeTracker.Entries<EfSchemaFinalizationRecordRow>()
                .Where(entry => entry.State == Microsoft.EntityFrameworkCore.EntityState.Added)
                .Select(entry => entry.Entity.Family));
            return ValueTask.FromResult(result);
        }
    }

    private EfModuleMigrator<ContractingDbContext> Migrator(EfMigratePolicy policy, IEfSchemaFleet? fleet = null, IInterceptor[]? interceptors = null)
    {
        var services = new ServiceCollection();
        if (fleet is not null)
            services.AddSingleton(fleet);
        services.AddDbContext<ContractingDbContext>(options => Bind(options.AddInterceptors(interceptors ?? []), Provider, Connection));
        services.AddEfModuleMigrations<ContractingDbContext>(Provider);
        services.Configure<EfMigrateOptions>(options => options.Policy = policy);
        var host = services.BuildServiceProvider();
        _hosts.Add(host);
        return host.GetRequiredService<EfModuleMigrator<ContractingDbContext>>();
    }

    /// <summary>Every table's definition and every row, as text: what a refusal must leave exactly as it found it.</summary>
    private async Task<string> SnapshotAsync()
    {
        await using var connection = new SqliteConnection(Connection);
        await connection.OpenAsync();
        var tables = new List<(string Name, string Sql)>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'table' ORDER BY name";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        var lines = tables.Select(table => table.Sql).ToList();
        foreach (var (name, _) in tables)
        {
            await using var select = connection.CreateCommand();
            select.CommandText = $"SELECT * FROM \"{name}\"";
            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                lines.Add(name + ":" + string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetValue(index)?.ToString())));
        }

        return string.Join("\n", lines.Order(StringComparer.Ordinal));
    }
}
