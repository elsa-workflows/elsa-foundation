using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.EntityFrameworkCore.Testing;
using Elsa.Cluster.InProcess;
using Elsa.Cluster.Readability;
using Elsa.Cluster.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// Spec 181, User Story 3 and SC-005: a host that composes only the in-process membership default is a cluster of one,
/// so its families finalize at the module's activation, unless a hold applies. The database holds the module's tables
/// and its finalization record, and no membership table: the finalization record is a fact about the database, and the
/// fleet is a fact about nothing but this process.
/// </summary>
public sealed class SingleHostFinalizationTests : IAsyncLifetime
{
    private const string Module = "SingleHostModule";
    private readonly string _family = $"SingleHost{Guid.NewGuid():N}";
    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-single-host-{Guid.NewGuid():N}.db");
    private readonly IClusterMembership _membership;

    public SingleHostFinalizationTests()
    {
        var readability = new ConformanceReadabilitySource([new ReadabilityEntry(_family, Module, ["1", "2"])]);
        _membership = new InProcessClusterMembership(
            Options.Create(new ClusterMembershipOptions { HostId = $"single-{Guid.NewGuid():N}" }),
            [readability],
            TimeProvider.System);
    }

    public async Task InitializeAsync()
    {
        await using var context = Context();
        await context.Database.EnsureCreatedAsync();
        // The version-1 release created the record: every row in the database is at 1.
        await new EfSchemaFinalizationStore(context).GetOrCreateAsync(_family, "1", ["1", "2"], SchemaFinalizationActor.OfOperator("release-1"));
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _file, _file + "-journal", _file + "-wal", _file + "-shm" })
            File.Delete(file);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_host_alone_upgraded_to_2_finalizes_2_at_activation_and_creates_no_membership_table()
    {
        var gate = Gate();

        await using (var context = Context())
            await gate.ActivateAsync(context);

        Assert.Equal(ClusterProviderKind.InProcess, _membership.ProviderKind);
        Assert.Equal("2", (await RecordAsync()).FinalizedVersion);
        Assert.Equal("2", gate.StateOf(_family)!.WriteVersion);
        Assert.Equal(
            [EfSchemaFinalization.DatabaseIdentityTableName(Module), EfSchemaFinalization.RecordTableName(Module)],
            await TablesAsync());
    }

    [Fact]
    public async Task A_host_alone_with_a_hold_finalizes_nothing_and_its_status_names_the_hold()
    {
        await using (var context = Context())
        {
            var store = new EfSchemaFinalizationStore(context);
            await store.PlaceHoldAsync(_family, (await store.FindAsync(_family))!.Revision, null, "canary of 2", "ops@example", ["1", "2"]);
        }

        var gate = Gate();
        await using (var context = Context())
            await gate.ActivateAsync(context);

        Assert.Equal("1", (await RecordAsync()).FinalizedVersion);
        Assert.Equal("1", gate.StateOf(_family)!.WriteVersion);
        await using var reading = Context();
        var pending = Assert.Single(Assert.Single(await gate.ReadStatusAsync(reading)).Pending);
        var hold = Assert.Single(pending.HeldBy);
        Assert.Equal(("2", "canary of 2", "ops@example"), (pending.Version, hold.Reason, hold.PlacedBy));
    }

    private EfSchemaModuleGate Gate() =>
        new(
            EfSchemaModuleFamilies.FromDeclarations(Module,
            [
                new EfSchemaFamilyDescriptor(_family, Module, "2", typeof(ScenarioUpcaster).Assembly)
                {
                    Upcasters = [new EfSchemaUpcasterDescriptor(typeof(ScenarioUpcaster), "1", "2")]
                }
            ]),
            new ClusterSchemaFleet(_membership),
            new EfSchemaFinalizationObservations(),
            new EfSchemaFinalizationOptions());

    private async Task<SchemaFinalizationRecord> RecordAsync()
    {
        await using var context = Context();
        return (await new EfSchemaFinalizationStore(context).FindAsync(_family))!;
    }

    private async Task<string[]> TablesAsync()
    {
        await using var context = Context();
        return await context.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name")
            .ToArrayAsync();
    }

    private SingleHostContext Context() =>
        new(new DbContextOptionsBuilder<SingleHostContext>().UseSqlite($"Data Source={_file}").Options);

    /// <summary>A module context that maps nothing but the finalization record, as every module context does.</summary>
    private sealed class SingleHostContext(DbContextOptions<SingleHostContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.MapSchemaFinalization(Module).IndexSchemaVersionStamps();
    }
}
