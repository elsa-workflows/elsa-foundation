using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1196: one claim compacts every shard of a continuous run, because retention budgets are per run.</summary>
public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task A_claim_compacts_every_shard_of_the_run_so_peers_history_cannot_exhaust_a_shared_budget()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(EtlV1CompatibilityFixture.FindRepositoryRoot(),
            "Extensions", "Etl", "tests", "Shared", "Fixtures", "Continuous1192", "runtime.db"), Path.Combine(directory, "etl-runtime.db"));
        await using var database = new EtlRuntimeTestDatabase(directory);
        await using var context = database.CreateContext();
        await EfDatabaseMigrator.ApplyAsync(context, EtlRuntimeSqliteDbContext.ExpectedProviderName);
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var before = await context.ConnectorPartitions.AsNoTracking().OrderBy(row => row.PartitionOrdinal)
            .Select(row => new { row.CurrentRequestSequence, row.PageBytes }).ToListAsync();
        var inputBefore = await context.ConnectorRuns.AsNoTracking().Select(row => row.RetainedInputBytes).SingleAsync();

        // The fixture's shard a (request 3) and shard b (request 2) both carry spec 1192 history; claim just one.
        var lease = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
            new EtlScope("tenant-fixture", "compaction-1192"), "compaction-1192", "one-claim"));

        context.ChangeTracker.Clear();
        var partitions = await context.ConnectorPartitions.AsNoTracking().OrderBy(row => row.PartitionOrdinal).ToListAsync();
        Assert.Equal([2L, 1L], partitions.Select(row => row.CompactedPageCount));
        Assert.All(partitions, row => Assert.True(row.PageBytes < before[row.PartitionOrdinal].PageBytes));
        var pages = await context.ConnectorHttpPages.AsNoTracking().OrderBy(row => row.PartitionOrdinal).ThenBy(row => row.PageSequence)
            .Select(row => new { row.PartitionOrdinal, row.PageSequence }).ToListAsync();
        Assert.Equal([(0, 2L), (1, 1L)], pages.Select(row => (row.PartitionOrdinal, row.PageSequence)));
        var inputAfter = await context.ConnectorRuns.AsNoTracking().Select(row => row.RetainedInputBytes).SingleAsync();
        Assert.Equal(partitions.Sum(row => row.PageBytes), inputAfter);
        Assert.True(inputAfter < inputBefore);
        Assert.Contains(lease.PartitionOrdinal, new[] { 0, 1 });
    }
}
