using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

[assembly: EfModule(
    ContractingModule.Name,
    typeof(ContractingDbContext),
    HistoryModule = ContractingModule.HistoryModule,
    Sqlite = typeof(ContractingDbContext),
    SqlServer = typeof(ContractingDbContext),
    PostgreSql = typeof(ContractingDbContext),
    MySql = typeof(ContractingDbContext))]
[assembly: EfSchemaFamily(ContractingModule.Family, ContractingModule.Name, ContractingModule.CurrentVersion, Upcasters = [typeof(ContractingProbeUpcaster)])]

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// A synthetic EF module whose history splits one change across two versions of its schema family (spec 185, User
/// Story 5): <see cref="Initial"/> creates a stamped table with a <c>Legacy</c> column, <see cref="Expand"/> adds its
/// replacement beside it, <see cref="DropObsolete"/> drops a column of an unstamped table under an opt-out that is not
/// contracting, and <see cref="Contract"/> drops <c>Legacy</c> under an opt-out naming the family and
/// <see cref="CurrentVersion"/>, the version whose finalization makes that safe. Its migrations are written by hand and
/// provider-free, so the same set runs on SQLite, SQL Server, PostgreSQL and MySQL. Each test project that drives it compiles this file in,
/// so each carries the module in an assembly of its own.
/// </summary>
internal static class ContractingModule
{
    public const string Name = "Tests.Contracting";
    public const string HistoryModule = "ElsaContractingTests";
    public const string Family = "ContractingProbe";
    public const string EarlierVersion = "1";
    public const string CurrentVersion = "2";
    public const string RowsTable = "contracting_rows";
    public const string NotesTable = "contracting_notes";

    public const string Initial = "20260929000000_ContractingInitial";
    public const string Expand = "20260929000001_ContractingExpand";
    public const string DropObsolete = "20260929000002_ContractingDropObsolete";
    public const string Contract = "20260929000003_ContractingContract";

    /// <summary>The family's chain as this build declares it, oldest first.</summary>
    public static readonly string[] Chain = [EarlierVersion, CurrentVersion];

    public static string HistoryTable => EfMigrationsHistory.TableName(HistoryModule);

    public static string MigrationsAssembly => typeof(ContractingDbContext).Assembly.GetName().Name!;

    /// <summary>The module's context on <paramref name="connectionString"/>, bound the way a host binds it.</summary>
    public static ContractingDbContext Create(string provider, string connectionString, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ContractingDbContext>();
        Bind(builder, provider, connectionString);
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return new ContractingDbContext(builder.Options);
    }

    public static void Bind(DbContextOptionsBuilder builder, string provider, string connectionString) =>
        EfRelationalProviderBinding.Use(builder, provider, connectionString, HistoryTable, MigrationsAssembly);

    /// <summary>
    /// What an earlier release left behind: its migrations applied through <paramref name="appliedThrough"/> and, when
    /// <paramref name="finalized"/> is given, the module admitted by a gate that finalized the family there.
    /// </summary>
    public static async Task StageAsync(string provider, string connectionString, string appliedThrough, string? finalized = EarlierVersion)
    {
        await using var context = Create(provider, connectionString);
        await context.GetService<IMigrator>().MigrateAsync(appliedThrough);
        if (finalized is not null)
            await FinalizeAsync(provider, connectionString, finalized);
    }

    /// <summary>Finalizes <paramref name="version"/> for the family, as the fleet's gate would once every host reads it.</summary>
    public static async Task FinalizeAsync(string provider, string connectionString, string version, IReadOnlyList<string>? chain = null)
    {
        await using var context = Create(provider, connectionString);
        await EfSchemaFinalizationTestSupport.FinalizeAsync(new EfSchemaFinalizationStore(context), Family, chain ?? Chain, version);
    }

    /// <summary>The family's finalization record, or null while nothing has created it, or its table.</summary>
    public static async Task<SchemaFinalizationRecord?> RecordAsync(string provider, string connectionString)
    {
        await using var context = Create(provider, connectionString);
        return await EfSchemaFinalizationCheck.RecordTableExistsAsync(context)
            ? await new EfSchemaFinalizationStore(context).FindAsync(Family)
            : null;
    }

    /// <summary>
    /// The module's finalization gate as a release whose family is at <paramref name="current"/> declares it: this
    /// release, or with <see cref="EarlierVersion"/> the one before the family's change, which reads only that version.
    /// </summary>
    public static EfSchemaModuleGate Gate(string current, IEfSchemaFleet? fleet = null) =>
        new(current == CurrentVersion
                ? EfSchemaModuleFamilies.For(Name, typeof(ContractingDbContext).Assembly)
                : EfSchemaModuleFamilies.FromDeclarations(Name, [new EfSchemaFamilyDescriptor(Family, Name, current, typeof(ContractingDbContext).Assembly)]),
            fleet,
            new EfSchemaFinalizationObservations(),
            new EfSchemaFinalizationOptions());

    public static async Task<IReadOnlyList<string>> AppliedAsync(string provider, string connectionString)
    {
        await using var context = Create(provider, connectionString);
        return (await context.Database.GetAppliedMigrationsAsync()).ToArray();
    }

    /// <summary>Spec 181, FR-002: the finalization record lands in a module's baseline, beside its history table.</summary>
    public static void CreateFinalizationTables(MigrationBuilder migrationBuilder, string historyModule, string keyPrefix)
    {
        migrationBuilder.CreateTable(
            name: EfSchemaFinalization.DatabaseIdentityTableName(historyModule),
            columns: table => new
            {
                Id = table.Column<int>(nullable: false),
                DatabaseIdentity = table.Column<string>(maxLength: 64, nullable: false),
                SchemaVersion = table.Column<string>(maxLength: 32, nullable: false)
            },
            constraints: table => table.PrimaryKey($"PK_{keyPrefix}DatabaseIdentity", identity => identity.Id));
        migrationBuilder.CreateTable(
            name: EfSchemaFinalization.RecordTableName(historyModule),
            columns: table => new
            {
                Family = table.Column<string>(maxLength: 128, nullable: false),
                SchemaVersion = table.Column<string>(maxLength: 32, nullable: false),
                DatabaseIdentity = table.Column<string>(maxLength: 64, nullable: false),
                Revision = table.Column<long>(nullable: false),
                FinalizedVersion = table.Column<string>(maxLength: 32, nullable: false),
                IntentJson = table.Column<string>(nullable: true),
                HoldsJson = table.Column<string>(nullable: false),
                HistoryJson = table.Column<string>(nullable: false),
                FinishJson = table.Column<string>(nullable: true),
                FinishHistoryJson = table.Column<string>(nullable: false)
            },
            constraints: table => table.PrimaryKey($"PK_{keyPrefix}SchemaFinalization", record => record.Family));
    }

    /// <summary>
    /// Whether <paramref name="table"/> has <paramref name="column"/>, read from the engine's own catalog. The count is read
    /// as the engine returns it, since each types <c>COUNT(*)</c> differently.
    /// </summary>
    public static async Task<bool> HasColumnAsync(string provider, string connectionString, string table, string column)
    {
        await using var context = Create(provider, connectionString);
        var sql = EfRelationalProviderBinding.Normalize(provider) == "sqlite"
            ? "SELECT COUNT(*) FROM pragma_table_info({0}) WHERE name = {1}"
            : "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = {0} AND COLUMN_NAME = {1}";
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, sql, "@table", "@column");
        foreach (var (name, value) in new[] { ("@table", table), ("@column", column) })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }
}

/// <summary>
/// What a test sees of a synthetic module's migrations as EF applies them: an action registered with
/// <see cref="WhenApplying{TMigration}"/> runs as EF begins applying that migration, before any of its operations, on the
/// flow that applies it, whichever of the persistence tool, a host's migrator or <see cref="EfDatabaseMigrator"/> drives
/// it. It is scoped to the registering test's own flow, so tests running at once never see each other's, and it stays
/// registered for the rest of that flow, firing each time the flow applies the migration. A crash is therefore
/// simulated not here but by an interceptor on one run's context, <c>ContractingSeedScenarios.EndTheProcessOnceWritten</c>,
/// which the retry's context does not carry.
/// </summary>
internal static class ContractingProbe
{
    private static readonly AsyncLocal<(Type Migration, Action Action)?> Hook = new();

    public static readonly Func<EventId, LogLevel, bool> Filter = static (id, _) => id == RelationalEventId.MigrationApplying;

    public static readonly Action<EventData> Observe = static data =>
    {
        if (data is MigrationEventData applying && Hook.Value is { } hook && applying.Migration.GetType() == hook.Migration)
            hook.Action();
    };

    public static void WhenApplying<TMigration>(Action action) where TMigration : Migration => Hook.Value = (typeof(TMigration), action);

    /// <summary>Hand-written migrations carry no model snapshot to compare the running model with; and every synthetic context is observed.</summary>
    public static void Configure(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .LogTo(Filter, Observe);
}

/// <summary>The module's context as the latest migration leaves it: <c>Legacy</c> and <c>Obsolete</c> are gone.</summary>
public sealed class ContractingDbContext(DbContextOptions<ContractingDbContext> options) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => ContractingProbe.Configure(optionsBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContractingRow>(row =>
        {
            row.ToTable(ContractingModule.RowsTable);
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Id).HasMaxLength(64);
            row.Property(entity => entity.SchemaVersion).HasMaxLength(32).IsRequired();
        });
        modelBuilder.Entity<ContractingNote>(note =>
        {
            note.ToTable(ContractingModule.NotesTable);
            note.HasKey(entity => entity.Id);
            note.Property(entity => entity.Id).HasMaxLength(64);
        });
        modelBuilder.MapSchemaFinalization(ContractingModule.HistoryModule);
    }
}

public sealed class ContractingRow
{
    public string Id { get; set; } = "";

    public string SchemaVersion { get; set; } = "";

    public string? Replacement { get; set; }
}

public sealed class ContractingNote
{
    public string Id { get; set; } = "";

    public string? Text { get; set; }
}

/// <summary>The family's one step. Content is not what this module tests, so the upcast changes nothing.</summary>
[EfSchemaUpcaster(ContractingModule.EarlierVersion, ContractingModule.CurrentVersion)]
public sealed class ContractingProbeUpcaster : IEfSchemaUpcaster
{
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}

/// <summary>
/// The module's schema after each of its migrations, as a generated migration's target model describes it: the
/// expand-only scan reads the one before a migration to learn which tables a stamped family covered, and SQLite rebuilds
/// a table from it to drop a column.
/// </summary>
internal static class ContractingTargetModel
{
    public static void Build(ModelBuilder modelBuilder, string after)
    {
        var expanded = string.CompareOrdinal(after, ContractingModule.Expand) >= 0;
        var obsoleteDropped = string.CompareOrdinal(after, ContractingModule.DropObsolete) >= 0;
        var contracted = string.CompareOrdinal(after, ContractingModule.Contract) >= 0;
        modelBuilder.Entity(typeof(ContractingRow).FullName!, row =>
        {
            row.Property<string>("Id").HasMaxLength(64);
            row.Property<string>(EfSchemaVersion.ColumnName).IsRequired().HasMaxLength(32);
            if (!contracted)
                row.Property<string>("Legacy");
            if (expanded)
                row.Property<string>("Replacement");
            row.HasKey("Id");
            row.ToTable(ContractingModule.RowsTable);
        });
        modelBuilder.Entity(typeof(ContractingNote).FullName!, note =>
        {
            note.Property<string>("Id").HasMaxLength(64);
            note.Property<string>("Text");
            if (!obsoleteDropped)
                note.Property<string>("Obsolete");
            note.HasKey("Id");
            note.ToTable(ContractingModule.NotesTable);
        });
    }
}

[DbContext(typeof(ContractingDbContext))]
[Migration(ContractingModule.Initial)]
public sealed class ContractingInitialMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: ContractingModule.RowsTable,
            columns: table => new
            {
                Id = table.Column<string>(maxLength: 64, nullable: false),
                SchemaVersion = table.Column<string>(maxLength: 32, nullable: false),
                Legacy = table.Column<string>(nullable: true)
            },
            constraints: table => table.PrimaryKey($"PK_{ContractingModule.RowsTable}", row => row.Id));
        migrationBuilder.CreateTable(
            name: ContractingModule.NotesTable,
            columns: table => new
            {
                Id = table.Column<string>(maxLength: 64, nullable: false),
                Text = table.Column<string>(nullable: true),
                Obsolete = table.Column<string>(nullable: true)
            },
            constraints: table => table.PrimaryKey($"PK_{ContractingModule.NotesTable}", note => note.Id));
        ContractingModule.CreateFinalizationTables(migrationBuilder, ContractingModule.HistoryModule, "Contracting");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ContractingTargetModel.Build(modelBuilder, ContractingModule.Initial);
}

[DbContext(typeof(ContractingDbContext))]
[Migration(ContractingModule.Expand)]
public sealed class ContractingExpandMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Replacement", table: ContractingModule.RowsTable, nullable: true);

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ContractingTargetModel.Build(modelBuilder, ContractingModule.Expand);
}

/// <summary>A removal the build guard permits that is not contracting: no stamped family covers the notes table.</summary>
[DbContext(typeof(ContractingDbContext))]
[Migration(ContractingModule.DropObsolete)]
[ExpandOnlyMigrationOptOut("No schema family stamps the notes table, and no release reads the column.", "#2136", "DropColumn contracting_notes.Obsolete")]
public sealed class ContractingDropObsoleteMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Obsolete", table: ContractingModule.NotesTable);

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ContractingTargetModel.Build(modelBuilder, ContractingModule.DropObsolete);
}

/// <summary>The contracting half: no version of the family reads <c>Legacy</c> once version 2 is finalized.</summary>
[DbContext(typeof(ContractingDbContext))]
[Migration(ContractingModule.Contract)]
[ExpandOnlyMigrationOptOut(
    "Version 2 reads Replacement; once it is finalized no finalized version reads Legacy.",
    "#2136",
    "DropColumn contracting_rows.Legacy",
    SchemaFamily = ContractingModule.Family,
    FinalizedVersion = ContractingModule.CurrentVersion)]
public sealed class ContractingContractMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Legacy", table: ContractingModule.RowsTable);

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ContractingTargetModel.Build(modelBuilder, ContractingModule.Contract);
}
