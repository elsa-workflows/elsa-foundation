using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[assembly: EfModule(
    ContractingPairModule.Name,
    typeof(ContractingPairDbContext),
    HistoryModule = ContractingPairModule.HistoryModule,
    Sqlite = typeof(ContractingPairDbContext))]
[assembly: EfSchemaFamily(
    ContractingPairModule.First,
    ContractingPairModule.Name,
    ContractingModule.CurrentVersion,
    Entities = [typeof(PairFirstRow)],
    Upcasters = [typeof(ContractingProbeUpcaster)])]
[assembly: EfSchemaFamily(
    ContractingPairModule.Second,
    ContractingPairModule.Name,
    ContractingModule.CurrentVersion,
    Entities = [typeof(PairSecondRow)],
    Upcasters = [typeof(ContractingProbeUpcaster)])]

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// A synthetic EF module with two schema families, each contracted by a migration of its own, in one pending batch: what
/// the migrator seeds when several families are contracted at once (#2136). Both families are at
/// <see cref="ContractingModule.CurrentVersion"/>, after <see cref="ContractingModule.EarlierVersion"/>.
/// </summary>
internal static class ContractingPairModule
{
    public const string Name = "Tests.ContractingPair";
    public const string HistoryModule = "ElsaContractingPairTests";
    public const string First = "PairFirst";
    public const string Second = "PairSecond";
    public const string FirstTable = "pair_first";
    public const string SecondTable = "pair_second";

    public const string Initial = "20260929100000_PairInitial";
    public const string ContractFirst = "20260929100001_PairContractFirst";
    public const string ContractSecond = "20260929100002_PairContractSecond";

    public static ContractingPairDbContext Create(string connectionString, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ContractingPairDbContext>();
        EfRelationalProviderBinding.Use(
            builder,
            "Sqlite",
            connectionString,
            EfMigrationsHistory.TableName(HistoryModule),
            typeof(ContractingPairDbContext).Assembly.GetName().Name!);
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return new ContractingPairDbContext(builder.Options);
    }

    /// <summary>The module's gate as a release that reads only <see cref="ContractingModule.EarlierVersion"/> of both families declares it.</summary>
    public static EfSchemaModuleGate OlderGate() =>
        new(EfSchemaModuleFamilies.FromDeclarations(
                Name,
                new[] { First, Second }.Select(family => new EfSchemaFamilyDescriptor(family, Name, ContractingModule.EarlierVersion, typeof(ContractingPairDbContext).Assembly))),
            fleet: null,
            new EfSchemaFinalizationObservations(),
            new EfSchemaFinalizationOptions());

    /// <summary>Each family's record, keyed by family.</summary>
    public static async Task<IReadOnlyDictionary<string, SchemaFinalizationRecord>> RecordsAsync(string connectionString)
    {
        await using var context = Create(connectionString);
        return await EfSchemaFinalizationCheck.RecordTableExistsAsync(context)
            ? await new EfSchemaFinalizationStore(context).ListAsync()
            : new Dictionary<string, SchemaFinalizationRecord>();
    }

    public static async Task<IReadOnlyList<string>> AppliedAsync(string connectionString)
    {
        await using var context = Create(connectionString);
        return (await context.Database.GetAppliedMigrationsAsync()).ToArray();
    }

    public static void Build(ModelBuilder modelBuilder, string after)
    {
        Table<PairFirstRow>(modelBuilder, FirstTable, keepsLegacy: string.CompareOrdinal(after, ContractFirst) < 0);
        Table<PairSecondRow>(modelBuilder, SecondTable, keepsLegacy: string.CompareOrdinal(after, ContractSecond) < 0);
    }

    private static void Table<TRow>(ModelBuilder modelBuilder, string table, bool keepsLegacy) =>
        modelBuilder.Entity(typeof(TRow).FullName!, row =>
        {
            row.Property<string>("Id").HasMaxLength(64);
            row.Property<string>(EfSchemaVersion.ColumnName).IsRequired().HasMaxLength(32);
            if (keepsLegacy)
                row.Property<string>("Legacy");
            row.HasKey("Id");
            row.ToTable(table);
        });
}

public sealed class ContractingPairDbContext(DbContextOptions<ContractingPairDbContext> options) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => ContractingProbe.Configure(optionsBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PairFirstRow>(row =>
        {
            row.ToTable(ContractingPairModule.FirstTable);
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Id).HasMaxLength(64);
            row.Property(entity => entity.SchemaVersion).HasMaxLength(32).IsRequired();
        });
        modelBuilder.Entity<PairSecondRow>(row =>
        {
            row.ToTable(ContractingPairModule.SecondTable);
            row.HasKey(entity => entity.Id);
            row.Property(entity => entity.Id).HasMaxLength(64);
            row.Property(entity => entity.SchemaVersion).HasMaxLength(32).IsRequired();
        });
        modelBuilder.MapSchemaFinalization(ContractingPairModule.HistoryModule);
    }
}

public sealed class PairFirstRow
{
    public string Id { get; set; } = "";

    public string SchemaVersion { get; set; } = "";
}

public sealed class PairSecondRow
{
    public string Id { get; set; } = "";

    public string SchemaVersion { get; set; } = "";
}

[DbContext(typeof(ContractingPairDbContext))]
[Migration(ContractingPairModule.Initial)]
public sealed class PairInitialMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { ContractingPairModule.FirstTable, ContractingPairModule.SecondTable })
        {
            migrationBuilder.CreateTable(
                name: table,
                columns: columns => new
                {
                    Id = columns.Column<string>(maxLength: 64, nullable: false),
                    SchemaVersion = columns.Column<string>(maxLength: 32, nullable: false),
                    Legacy = columns.Column<string>(nullable: true)
                },
                constraints: constraints => constraints.PrimaryKey($"PK_{table}", row => row.Id));
        }

        ContractingModule.CreateFinalizationTables(migrationBuilder, ContractingPairModule.HistoryModule, "Pair");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ContractingPairModule.Build(modelBuilder, ContractingPairModule.Initial);
}

[DbContext(typeof(ContractingPairDbContext))]
[Migration(ContractingPairModule.ContractFirst)]
[ExpandOnlyMigrationOptOut(
    "Version 2 no longer reads Legacy.",
    "#2136",
    "DropColumn pair_first.Legacy",
    SchemaFamily = ContractingPairModule.First,
    FinalizedVersion = ContractingModule.CurrentVersion)]
public sealed class PairContractFirstMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Legacy", table: ContractingPairModule.FirstTable);

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ContractingPairModule.Build(modelBuilder, ContractingPairModule.ContractFirst);
}

[DbContext(typeof(ContractingPairDbContext))]
[Migration(ContractingPairModule.ContractSecond)]
[ExpandOnlyMigrationOptOut(
    "Version 2 no longer reads Legacy.",
    "#2136",
    "DropColumn pair_second.Legacy",
    SchemaFamily = ContractingPairModule.Second,
    FinalizedVersion = ContractingModule.CurrentVersion)]
public sealed class PairContractSecondMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Legacy", table: ContractingPairModule.SecondTable);

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ContractingPairModule.Build(modelBuilder, ContractingPairModule.ContractSecond);
}
