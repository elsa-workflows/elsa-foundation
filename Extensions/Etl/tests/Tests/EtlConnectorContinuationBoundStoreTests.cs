using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Connector_store_rejects_continuation_policy_above_supported_ceiling()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with
        {
            Http = defaults.Http with
            {
                MaxContinuationBytes = EtlHttpPolicy.MaximumSupportedContinuationBytes + 1
            }
        };
        var admission = ConnectorAdmission("continuation-store-over-bound", new EtlScope("tenant-a", "continuation-store-over-bound"), pipeline, ["shard-a"]);

        var result = await store.AdmitConnectorAsync(admission);

        Assert.Equal(EtlStoreStatus.Invalid, result.Status);
        Assert.Empty(await context.ConnectorRuns.AsNoTracking().ToListAsync());
        Assert.Empty(await context.ConnectorPartitions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Connector_store_accepts_continuation_policy_at_supported_ceiling()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var defaults = EtlV2TestPipelines.Create();
        var pipeline = defaults with
        {
            Http = defaults.Http with { MaxContinuationBytes = EtlHttpPolicy.MaximumSupportedContinuationBytes }
        };
        var admission = ConnectorAdmission("continuation-store-boundary", new EtlScope("tenant-a", "continuation-store-boundary"), pipeline, ["shard-a"]);

        var result = await store.AdmitConnectorAsync(admission);

        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        Assert.Single(await context.ConnectorRuns.AsNoTracking().ToListAsync());
        Assert.Empty(await context.ConnectorPartitions.AsNoTracking().ToListAsync());
    }
}
