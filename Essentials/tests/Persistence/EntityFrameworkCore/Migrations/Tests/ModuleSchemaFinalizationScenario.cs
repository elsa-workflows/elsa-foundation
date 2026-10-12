using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// The finalization record through every real module context of one provider, against the tables each module's own
/// migrations created in one shared database (spec 181, FR-002). Shared by the SQLite suite and the container suites.
/// </summary>
internal static class ModuleSchemaFinalizationScenario
{
    private const string Family = "Probe";
    private static readonly string[] Chain = ["1.0.0", "2.0.0", "3.0.0"];
    private static readonly SchemaFinalizationMember HostA = new("host-a", "incarnation-a");
    private static readonly SchemaFinalizationMember HostB = new("host-b", "incarnation-b");

    private const int StressCreators = 8;
    private const int StressFamilies = 25;

    /// <summary>
    /// #2162: for each module context, two hosts that start together on a database no host has touched both read "no
    /// record" and "no identity", and both insert. The loser of each insert reads the winner's row and carries on with
    /// the same identity, one row of each is left, and EF logs no error. Both are held at their insert until both have
    /// reached it (<see cref="StartupInsertRace"/>), so the engine, not the scheduler, decides the race. Run it before
    /// <see cref="RunAsync"/>, while the modules' identity rows do not exist yet.
    /// </summary>
    public static async Task RunStartupRaceAsync(string provider, string connectionString, string? schema = null)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            var module = ModuleOf(type);
            var (recordTable, identityTable) = (EfSchemaFinalization.RecordTableName(module), EfSchemaFinalization.DatabaseIdentityTableName(module));
            var race = StartupInsertRace.Begin(2, recordTable, identityTable);
            await using var first = ModuleContextCatalog.Create(type, connectionString, race.Configure, schema);
            await using var second = ModuleContextCatalog.Create(type, connectionString, race.Configure, schema);

            var records = await Task.WhenAll(
                new EfSchemaFinalizationStore(first).GetOrCreateAsync(Family, Chain[0], Chain, SchemaFinalizationActor.Of(HostA)),
                new EfSchemaFinalizationStore(second).GetOrCreateAsync(Family, Chain[0], Chain, SchemaFinalizationActor.Of(HostB)));

            Assert.True(race.EveryContextWasHeldBeforeItsInsert, $"{type.Name}: the hosts were not both at their insert before either wrote.");
            Assert.Empty(race.Errors);
            var identity = Assert.Single(records.Select(record => record.DatabaseIdentity).Distinct(StringComparer.Ordinal));
            Assert.Equal(records[0].Family, records[1].Family);
            await using var reader = ModuleContextCatalog.Create(type, connectionString, schema: schema);
            var readerStore = new EfSchemaFinalizationStore(reader);
            Assert.Equal(identity, await readerStore.GetOrCreateDatabaseIdentityAsync());
            Assert.Equal(records[0].Revision, (await readerStore.FindAsync(Family))!.Revision);
            Assert.Equal(1, await RowCountAsync(reader, recordTable));
            Assert.Equal(1, await RowCountAsync(reader, identityTable));
        }
    }

    /// <summary>
    /// #2162: for each module context, <see cref="StressCreators"/> creators, each on a connection of its own, create the
    /// same <see cref="StressFamilies"/> families' records, all of them starting each family together, without a hold. Of
    /// every family's creators exactly one inserts, all of them read the same record back, EF logs no error, and the
    /// table gains one row per family. The identity is raced on the first family of a database no host has touched, and
    /// stays one row.
    /// </summary>
    public static async Task RunStartupStressAsync(string provider, string connectionString, string? schema = null)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            var module = ModuleOf(type);
            var (recordTable, identityTable) = (EfSchemaFinalization.RecordTableName(module), EfSchemaFinalization.DatabaseIdentityTableName(module));
            var race = StartupInsertRace.Record();
            var contexts = Enumerable.Range(0, StressCreators).Select(_ => ModuleContextCatalog.Create(type, connectionString, race.Configure, schema)).ToArray();
            try
            {
                var creators = contexts.Select((context, index) => (Store: new EfSchemaFinalizationStore(context), Actor: SchemaFinalizationActor.OfOperator($"creator-{index}"))).ToArray();
                var recordsBefore = await RowCountAsync(contexts[0], recordTable);
                for (var family = 0; family < StressFamilies; family++)
                {
                    var name = $"Stress{family}";
                    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var creations = creators.Select(async creator =>
                    {
                        await start.Task;
                        return await creator.Store.GetOrCreateAsync(name, Chain[0], Chain, creator.Actor);
                    }).ToArray();
                    start.SetResult();

                    var records = await Task.WhenAll(creations);
                    Assert.True(
                        records.Select(record => (record.DatabaseIdentity, record.Revision, record.History[0].Actor)).Distinct().Count() == 1,
                        $"{type.Name}: the creators of '{name}' did not all read the same record back.");
                }

                Assert.Empty(race.Errors);
                Assert.Equal(recordsBefore + StressFamilies, await RowCountAsync(contexts[0], recordTable));
                Assert.Equal(1, await RowCountAsync(contexts[0], identityTable));
            }
            finally
            {
                foreach (var context in contexts)
                    await context.DisposeAsync();
            }
        }
    }

    private static string ModuleOf(Type context) => ModuleContextCatalog.HistoryTable(context)[EfMigrationsHistory.TablePrefix.Length..];

    private static async Task<long> RowCountAsync(DbContext context, string table)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var schema = context.Model.GetEntityTypes().First(entity => entity.GetTableName() == table).GetSchema();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {sql.DelimitIdentifier(table, schema)}";
        await context.Database.OpenConnectionAsync();
        try
        {
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// For each module context: a record is created and finalized forward, a backwards finalization is refused, the
    /// database identity survives a fresh context, and of two concurrent writers exactly one wins. Every module keeps an
    /// identity of its own, although they share the database.
    /// </summary>
    public static async Task RunAsync(string provider, string connectionString, string? schema = null)
    {
        var identities = new List<string>();
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            await using var context = ModuleContextCatalog.Create(type, connectionString, schema: schema);
            var store = new EfSchemaFinalizationStore(context);
            var finalized = await EfSchemaFinalizationTestSupport.FinalizeAsync(store, Family, Chain, "2.0.0", HostA);

            var refusal = await Assert.ThrowsAsync<SchemaFinalizationRefusedException>(
                () => store.RecordIntentAsync(Family, finalized.Revision, "1.0.0", Chain, HostA));
            Assert.Equal(SchemaFinalizationRefusal.NotForward, refusal.Refusal);

            await using var first = ModuleContextCatalog.Create(type, connectionString, schema: schema);
            await using var second = ModuleContextCatalog.Create(type, connectionString, schema: schema);
            var writes = await Task.WhenAll(
                new EfSchemaFinalizationStore(first).RecordIntentAsync(Family, finalized.Revision, "3.0.0", Chain, HostA),
                new EfSchemaFinalizationStore(second).RecordIntentAsync(Family, finalized.Revision, "3.0.0", Chain, HostB));
            Assert.True(writes.Count(write => write.Applied) == 1, $"{type.Name}: expected exactly one of two concurrent writers to win.");

            await using var restarted = ModuleContextCatalog.Create(type, connectionString, schema: schema);
            var reread = await new EfSchemaFinalizationStore(restarted).FindAsync(Family);
            Assert.NotNull(reread);
            Assert.Equal("2.0.0", reread.FinalizedVersion);
            Assert.Equal(finalized.DatabaseIdentity, reread.DatabaseIdentity);
            Assert.Equal(finalized.DatabaseIdentity, await new EfSchemaFinalizationStore(restarted).GetOrCreateDatabaseIdentityAsync());
            identities.Add(finalized.DatabaseIdentity);
        }

        Assert.NotEmpty(identities);
        Assert.Equal(identities.Count, identities.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The enable-time guard finds each module's record table through the engine's own catalog, never the migrations
    /// history (spec 171, FR-069): absent before the module's migrations run, present after, on every engine.
    /// </summary>
    public static async Task AssertRecordTablesAsync(string provider, string connectionString, bool expected, string? schema = null)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            await using var context = ModuleContextCatalog.Create(type, connectionString, schema: schema);
            Assert.True(expected == await EfSchemaFinalizationCheck.RecordTableExistsAsync(context),
                $"{type.Name}: expected its finalization record table to {(expected ? "exist" : "be absent")}.");
        }
    }
}
