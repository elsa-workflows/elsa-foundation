using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Terminal_run_rejects_source_operations_without_changing_journal_or_accounting(bool succeeded)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var runtime = database.CreateStore(context, new MutableTimeProvider());
        var store = (IEtlConnectorRuntimeStore)runtime;
        var admission = ConnectorAdmission("source-terminal", new("tenant-a", "source-terminal"), httpShardIds: ["shard-a"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));

        var begun = await store.BeginConnectorHttpSourceAsync(lease, Guid.NewGuid().ToString("N"));
        Assert.Equal(EtlStoreStatus.Applied, begun.Status);
        var permit = Assert.IsType<EtlHttpSourceFetchAttempt>(begun.Permit);
        if (succeeded)
        {
            var page = RetainedPage(lease, "completed-source-page", 23, 0);
            Assert.Equal(EtlStoreStatus.Applied,
                (await store.CaptureConnectorHttpSourceAsync(lease, permit, page, permit.StartedAt)).Status);
            var attached = await store.AttachHttpPageAsync(lease, page);
            Assert.Equal(EtlStoreStatus.Applied, attached.Status);
            Assert.Equal(EtlStoreStatus.Applied, (await store.AdvanceEmptyHttpPageAsync(attached.Lease!)).Status);
        }
        else
            Assert.Equal(EtlStoreStatus.Applied, (await runtime.CancelAsync(admission.Invocation)).Status);
        Assert.Equal(succeeded ? EtlRunStatus.Succeeded : EtlRunStatus.Cancelled,
            (await runtime.FindRunAsync(lease.Scope, lease.RunId))!.Status);
        var before = await ReadSourceTerminalSnapshotAsync(context);

        foreach (var result in new[]
        {
            await store.BeginConnectorHttpSourceAsync(lease, Guid.NewGuid().ToString("N")),
            await store.CompleteConnectorHttpSourceAsync(lease, permit, new(500, null, false)),
            await store.CaptureConnectorHttpSourceAsync(lease, permit,
                RetainedPage(lease, "late-source-page", 23, 1), permit.StartedAt)
        })
        {
            Assert.Equal(EtlStoreStatus.Terminal, result.Status);
            Assert.Null(result.Fetch);
            Assert.Null(result.Permit);
        }
        Assert.Equal(before, await ReadSourceTerminalSnapshotAsync(context));
    }

    private static async Task<string> ReadSourceTerminalSnapshotAsync(EtlRuntimeDbContext context)
    {
        context.ChangeTracker.Clear();
        // Scalar EF rows have no navigation properties. Compare the full retained state, including
        // the original result/outbox, so a refused operation cannot silently alter another column.
        return JsonSerializer.Serialize(new
        {
            Run = await context.Runs.AsNoTracking().SingleAsync(),
            Connector = await context.ConnectorRuns.AsNoTracking().SingleAsync(),
            Partition = await context.ConnectorPartitions.AsNoTracking().SingleAsync(),
            Request = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync(),
            Pages = await context.ConnectorHttpPages.AsNoTracking().OrderBy(row => row.PageSequence).ToArrayAsync(),
            Terminal = await context.Terminals.AsNoTracking().SingleAsync(),
            Outbox = await context.Outbox.AsNoTracking().SingleAsync()
        });
    }
}
