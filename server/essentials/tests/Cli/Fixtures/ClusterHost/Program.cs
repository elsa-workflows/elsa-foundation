using Elsa.Cluster.Fixtures.FeedModule;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;

// A fixture host is never started as a host. It exists to be *built*: what the tool inspects is its published output
// (a runtimeconfig, a deps file, and the module and provider-engine assemblies beside them), which is all a packaged
// host offers a tool that has no source tree. The one thing it does when run is seed a database, as the release before
// it left it: the feed module's finalization record at version 1, in tables created from the module's model, since the fixture module has no migrations at all. Run it
// before `apply`, on a database with no tables yet.
if (args is not ["seed-release-1", var connection])
    return 0;

await using var context = new FeedModuleSqliteDbContext(new DbContextOptionsBuilder<FeedModuleSqliteDbContext>().UseSqlite(connection).Options);
await context.Database.EnsureCreatedAsync();
await new EfSchemaFinalizationStore(context).GetOrCreateAsync(
    FeedModule.Family,
    FeedModule.PreviousVersion,
    [FeedModule.PreviousVersion, FeedModule.CurrentVersion],
    SchemaFinalizationActor.OfOperator("release-1"));
return 0;
