using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.ContractingModule;

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

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly TemporarySqliteDatabase _database = new("contracting");
    private ServiceProvider? _host;

    private string Connection => _database.ConnectionString;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
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
    /// the module in this database, so nothing reads what it removes, and a refusal here could never clear, since only an
    /// admitted module creates the record the check waits for.
    /// </summary>
    [Fact]
    public async Task A_fresh_database_takes_the_contracting_migration_with_the_rest_of_its_batch()
    {
        var migrator = Migrator(EfMigratePolicy.AutoMigrate);

        await migrator.InitializeAsync();

        Assert.Equal([Initial, Expand, DropObsolete, Contract], await AppliedAsync(Provider, Connection));
        Assert.NotNull(migrator.Gate);
    }

    /// <summary>Tables that exist without a database identity were migrated, but no gate-aware host has admitted the module.</summary>
    [Fact]
    public async Task A_database_whose_module_no_host_has_admitted_yet_takes_the_contracting_migration()
    {
        await StageAsync(Provider, Connection, DropObsolete, finalized: null);

        await using (var context = Create(Provider, Connection))
            await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite);

        Assert.Contains(Contract, await AppliedAsync(Provider, Connection));
    }

    /// <summary>Once a host has admitted the module, a family with no record has nothing to show its version finalized.</summary>
    [Fact]
    public async Task Once_the_module_has_been_admitted_a_family_with_no_record_is_refused()
    {
        await StageAsync(Provider, Connection, DropObsolete, finalized: null);
        await using (var context = Create(Provider, Connection))
            await new EfSchemaFinalizationStore(context).GetOrCreateDatabaseIdentityAsync();

        var refusal = await ApplyExpectingRefusalAsync();

        Assert.Equal(
            new EfContractingMigrationRefusal(Contract, Family, CurrentVersion, null, EfContractingMigrationRefusalReason.NoRecord),
            Assert.Single(refusal.Refusals));
        Assert.DoesNotContain(Contract, await AppliedAsync(Provider, Connection));
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
    /// nothing refuses the contraction, and its gate creates the family's record. The record must start at
    /// <see cref="CurrentVersion"/>, which the contraction names, not at <see cref="EarlierVersion"/>, the oldest version
    /// host-new reads, since the schema no longer serves that. So host-late, starting later on the older release, is
    /// refused rather than admitted against a schema without the column it reads.
    /// </summary>
    [Fact]
    public async Task A_contraction_applied_on_a_fresh_database_starts_its_familys_record_at_its_version_so_an_older_host_is_refused()
    {
        var fleet = new FakeFleetState();
        fleet.Add(new FakeMember("host-old").Reading(Family, EarlierVersion)).Published = true;
        var newer = Migrator(EfMigratePolicy.AutoMigrate, new FakeFleet(fleet, fleet.Add(new FakeMember("host-new").Reading(Family, Chain))));

        await newer.InitializeAsync();

        Assert.False(await HasColumnAsync(Provider, Connection, RowsTable, "Legacy"));
        var record = await RecordAsync(Provider, Connection);
        Assert.Equal(CurrentVersion, record!.FinalizedVersion);
        Assert.Equal((SchemaFinalizationTransition.Created, CurrentVersion), (record.History[0].Transition, record.History[0].Version));
        var before = await SnapshotAsync();

        var late = Gate(EarlierVersion, new FakeFleet(fleet, fleet.Add(new FakeMember("host-late").Reading(Family, EarlierVersion))));
        await using var context = Create(Provider, Connection);
        var refusal = await Assert.ThrowsAsync<EfSchemaActivationRefusedException>(() => late.ActivateAsync(context));

        Assert.Equal((EfSchemaActivationRefusal.FinalizedUnreadable, Family, CurrentVersion), (refusal.Refusal, refusal.Family, refusal.Version));
        Assert.Equal(before, await SnapshotAsync());
    }

    /// <summary>
    /// Both ways: a contraction still pending leaves the schema serving the oldest version the host reads, and the record
    /// starts there; once it has applied, the record starts at the version it names.
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
    /// A host that cannot place the version an applied contraction names cannot tell which of its versions the schema still
    /// serves, so it is refused rather than starting the record at the oldest version it reads, and creates no record.
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

        var refused = await ToolAsync("apply");

        Assert.Equal(EfToolingExitCode.Refusal, refused.ExitCode);
        var error = refused.Response.GetProperty("error");
        Assert.Equal("contracting-migration-refused", error.GetProperty("code").GetString());
        AssertNamesTheFamilyAndBothVersions(Assert.Single(error.GetProperty("details").EnumerateArray()).GetString()!, finalized: EarlierVersion);
        Assert.Equal(before, await SnapshotAsync());

        await FinalizeAsync(Provider, Connection, CurrentVersion);
        var applied = await ToolAsync("apply");

        Assert.Equal(EfToolingExitCode.Success, applied.ExitCode);
        Assert.Equal([Initial, Expand, DropObsolete, Contract], await AppliedAsync(Provider, Connection));
    }

    [Fact]
    public async Task Persistence_validate_reports_the_withheld_batch_as_pending_naming_the_family_and_version()
    {
        await StageAsync(Provider, Connection, DropObsolete);

        var run = await ToolAsync("validate");

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

    private EfModuleMigrator<ContractingDbContext> Migrator(EfMigratePolicy policy, IEfSchemaFleet? fleet = null)
    {
        var services = new ServiceCollection();
        if (fleet is not null)
            services.AddSingleton(fleet);
        services.AddDbContext<ContractingDbContext>(options => Bind(options, Provider, Connection));
        services.AddEfModuleMigrations<ContractingDbContext>(Provider);
        services.Configure<EfMigrateOptions>(options => options.Policy = policy);
        _host = services.BuildServiceProvider();
        return _host.GetRequiredService<EfModuleMigrator<ContractingDbContext>>();
    }

    private async Task<(int ExitCode, JsonElement Response)> ToolAsync(string command)
    {
        var request = new { version = 1, command, provider = Provider, selection = new { kind = "modules", modules = new[] { Name } }, connection = Connection };
        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(request, Json));
        using var output = new MemoryStream();
        var exitCode = await EfToolingHost.RunAsync(input, output, [typeof(ContractingDbContext).Assembly]);
        using var response = JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));
        return (exitCode, response.RootElement.Clone());
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
