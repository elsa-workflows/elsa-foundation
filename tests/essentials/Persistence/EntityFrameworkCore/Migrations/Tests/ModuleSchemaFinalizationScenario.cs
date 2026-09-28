using Elsa.Persistence.EntityFramework.SchemaFinalization;
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
            var created = await store.GetOrCreateAsync(Family, "1.0.0", Chain, SchemaFinalizationActor.OfOperator("provider-test"));
            var intended = Applied(type, await store.RecordIntentAsync(Family, created.Revision, "2.0.0", Chain, HostA));
            var finalized = Applied(type, await store.CommitIntentAsync(Family, intended.Revision, Chain, HostB));

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
            Assert.Equal(created.DatabaseIdentity, reread.DatabaseIdentity);
            Assert.Equal(created.DatabaseIdentity, await new EfSchemaFinalizationStore(restarted).GetOrCreateDatabaseIdentityAsync());
            identities.Add(created.DatabaseIdentity);
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

    private static SchemaFinalizationRecord Applied(Type context, SchemaFinalizationWrite write)
    {
        Assert.True(write.Applied, $"{context.Name}: an uncontended write lost its compare-and-set.");
        return write.Record;
    }
}
