using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// The feed-module fixture's database as the tests see it from outside the module: the finalization tables it creates, the
/// record a release at version 1 created, and the record as it stands. The engine is the caller's, so the same seed serves
/// every engine the host boot tests run on.
/// </summary>
/// <param name="useProvider">Points a context's options at <paramref name="connectionString"/> on the engine under test.</param>
internal sealed class FeedModuleDatabase(Action<DbContextOptionsBuilder, string> useProvider, string connectionString)
{
    /// <summary>
    /// The fixture's family. The fixture's names are restated here because the fixture is never loaded where this code
    /// runs; <see cref="FeedLoadedModuleHost.StartAsync"/> refuses a fixture whose own constants say otherwise.
    /// </summary>
    public const string Family = "FeedModuleFixtureOrders";

    /// <summary>The module's migrations-history name, which names its finalization tables.</summary>
    public const string HistoryModule = "ElsaFeedModuleFixture";

    public static readonly string[] Chain = ["1", "2"];

    /// <summary>Creates the module's finalization tables and the record a release at version 1 created, optionally held.</summary>
    public async Task SeedAsync(string? hold = null)
    {
        await using var context = Context();
        await context.Database.EnsureCreatedAsync();
        var store = new EfSchemaFinalizationStore(context);
        var created = await store.GetOrCreateAsync(Family, "1", Chain, SchemaFinalizationActor.OfOperator("release-1"));
        if (hold is not null && !(await store.PlaceHoldAsync(Family, created.Revision, null, hold, "ops@example", Chain)).Applied)
            throw new InvalidOperationException("The hold was not placed: the record changed under the seed.");
    }

    public async Task ReleaseHoldAsync()
    {
        await using var context = Context();
        var store = new EfSchemaFinalizationStore(context);
        if (!(await store.ReleaseHoldAsync(Family, (await store.FindAsync(Family))!.Revision, null, "ops@example")).Applied)
            throw new InvalidOperationException("The hold was not released: the record changed under the release.");
    }

    public async Task<SchemaFinalizationRecord> RecordAsync()
    {
        await using var context = Context();
        return (await new EfSchemaFinalizationStore(context).FindAsync(Family))!;
    }

    private SeedContext Context()
    {
        var builder = new DbContextOptionsBuilder<SeedContext>();
        useProvider(builder, connectionString);
        return new SeedContext(builder.Options);
    }

    /// <summary>The fixture module's finalization tables, mapped from the test's side to seed and read them.</summary>
    private sealed class SeedContext(DbContextOptions<SeedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.MapSchemaFinalization(HistoryModule).IndexSchemaVersionStamps();
    }
}
