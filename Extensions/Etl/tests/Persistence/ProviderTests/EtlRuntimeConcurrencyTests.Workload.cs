using System.Collections.Concurrent;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Tests;
using Microsoft.EntityFrameworkCore;
using static Elsa.Etl.Tests.EtlRuntimeStoreContract;

namespace Elsa.Etl.Persistence.ProviderTests;

public abstract partial class EtlRuntimeConcurrencyTests
{
    [SkippableFact]
    public async Task Four_writers_complete_32_mixed_runs_with_exact_accounting_and_stale_fence_refusal()
    {
        await using var database = await EtlProviderRuntimeDatabase.CreateTypedAsync(fixture);
        var clock = new MutableTimeProvider();
        var scope = new EtlScope("tenant-workload", "partition-workload");
        var files = new Dictionary<string, EtlRunAdmission>(StringComparer.Ordinal);
        var connectors = new Dictionary<string, EtlConnectorRunAdmission>(StringComparer.Ordinal);
        await using (var context = database.CreateContext())
        {
            var store = ((IEtlRuntimeTestDatabase)database).CreateStore(context, clock);
            var pipeline = EtlTestPipelines.Create() with { Limits = EtlTestPipelines.Create().Limits with { PartitionCount = 2, MaxBatchRecords = 2 } };
            for (var index = 0; index < 16; index++)
            {
                var file = CreateAdmission($"file-{index:D2}", scope, pipeline);
                var connector = EtlRuntimeFixtures.ConnectorAdmission($"connector-{index:D2}", scope, httpShardIds: ["first", "second"]);
                files.Add(file.Invocation.RunId, file);
                connectors.Add(connector.Invocation.RunId, connector);
                Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(file)).Status);
                Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitConnectorAsync(connector)).Status);
            }
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var token = cancellation.Token;
        var conflicts = new ConcurrentQueue<string>();
        var completed = new ConcurrentQueue<string>();
        var delivered = new ConcurrentQueue<string>();
        var deliveredResults = new ConcurrentDictionary<string, (EtlTerminalResult Result, string PayloadHash)>(StringComparer.Ordinal);
        var staleRefusals = 0;
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 4).Select(WorkAsync));
        }
        finally
        {
            output.WriteLine($"Preparation/acceptance observed native conflicts: {conflicts.Count}.");
            foreach (var conflict in conflicts.Distinct()) output.WriteLine("Observed workload native conflict: " + conflict);
        }
        Assert.Equal(32, completed.Count);
        Assert.Equal(32, completed.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(64, staleRefusals);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(DeliverAsync));
        Assert.Equal(32, delivered.Count);
        Assert.Equal(32, delivered.Distinct(StringComparer.Ordinal).Count());

        await using var observer = database.CreateContext();
        var runs = await observer.Runs.AsNoTracking().ToListAsync(token);
        Assert.Equal(32, runs.Count);
        Assert.All(runs, run => Assert.Equal((int)EtlRunStatus.Succeeded, run.Status));
        Assert.Equal((64L, 0L, 32L, 0L, 0L),
            (runs.Sum(run => run.AcceptedRecords), runs.Sum(run => run.RejectedRecords), runs.Sum(run => run.FilteredRecords),
                runs.Sum(run => run.OutstandingRecords), runs.Sum(run => run.OutstandingBytes)));
        var terminals = await observer.Terminals.AsNoTracking().ToListAsync(token);
        var outbox = await observer.Outbox.AsNoTracking().ToListAsync(token);
        Assert.Equal(32, terminals.Count);
        Assert.Equal(32, terminals.Select(row => row.ResultId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(32, outbox.Count);
        Assert.Equal(32, outbox.Select(row => row.DeliveryId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(outbox, row => Assert.Equal((int)EtlDeliveryDisposition.Consumed, row.Disposition));
        Assert.Equal(32, await observer.Manifest.CountAsync(token));
        Assert.Equal(32, await observer.ConnectorBatches.CountAsync(token));
        var reservations = await observer.Reservations.AsNoTracking().ToListAsync(token);
        Assert.Equal(32, reservations.Count);
        Assert.All(reservations, row => Assert.True(row.Settled));
        foreach (var run in runs)
        {
            var id = EtlRuntimeRecordCodec.DecodeIdentity(run.RunId, "run id");
            var file = files.ContainsKey(id);
            Assert.True(file || connectors.ContainsKey(id));
            var expected = file ? (4L, 0L, 0L) : (0L, 0L, 2L);
            Assert.Equal(expected, (run.AcceptedRecords, run.RejectedRecords, run.FilteredRecords));
            Assert.Equal((0L, 0L), (run.OutstandingRecords, run.OutstandingBytes));
            var terminal = Assert.Single(terminals, row => row.ScopeKey == run.ScopeKey && row.RunKey == run.RunKey);
            var delivery = Assert.Single(outbox, row => row.ScopeKey == run.ScopeKey && row.RunKey == run.RunKey);
            var result = EtlRuntimeRecordCodec.Read<EtlTerminalResult>(terminal.ResultJson);
            Assert.Equal(EtlTerminalOutcome.Succeeded, result.Outcome);
            Assert.Equal(expected, (result.AcceptedRecords, result.RejectedRecords, result.FilteredRecords));
            Assert.Equal(terminal.ResultId, result.ResultId);
            Assert.Equal(EtlRuntimeRecordCodec.HashText(terminal.ResultJson), terminal.ResultHash);
            Assert.True(deliveredResults.TryGetValue(id, out var consumed));
            Assert.Equal(result, consumed.Result);
            Assert.Equal(delivery.PayloadHash, consumed.PayloadHash);
            if (file)
                Assert.Equal(2, await observer.Partitions.CountAsync(row => row.ScopeKey == run.ScopeKey && row.RunKey == run.RunKey, token));
            else
                Assert.Equal(2, await observer.ConnectorPartitions.CountAsync(row => row.ScopeKey == run.ScopeKey && row.RunKey == run.RunKey, token));
        }
        Assert.All(await observer.ConnectorBatches.AsNoTracking().ToListAsync(token), row => Assert.True(row.Settled));
        foreach (var conflict in conflicts.Distinct()) output.WriteLine("Observed workload native conflict: " + conflict);
        output.WriteLine("Store workload: 32 mixed runs, four concurrent writers, 64 stale-fence refusals, 32 unique terminal/outbox acknowledgements; no physical connector I/O or native bookmark consumption is asserted.");

        async Task WorkAsync(int ordinal)
        {
            try
            {
                await using var context = database.CreateContext(new NativeRaceCommand(conflicts));
                var store = ((IEtlRuntimeTestDatabase)database).CreateStore(context, clock);
                var worker = $"worker-{ordinal}";
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (await store.ClaimPreparationAsync(scope, worker, token) is { } filePreparation)
                    {
                        var admission = files[filePreparation.RunId];
                        Assert.Equal(EtlStoreStatus.Applied, await store.CommitPreparationAsync(filePreparation, EtlRuntimeFixtures.Prepared(4), token));
                        for (var partition = 0; partition < 2; partition++)
                        {
                            var old = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(scope, filePreparation.RunId, worker, token));
                            Assert.Equal(EtlStoreStatus.Applied, await store.ReleasePartitionAsync(old, token));
                            var current = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(scope, filePreparation.RunId, worker, token));
                            Assert.True(current.Fence > old.Fence);
                            var batch = CreateBatch(admission, EtlRuntimeFixtures.Prepared(4), current.PartitionOrdinal, current.StartOrdinal, current.EndOrdinalExclusive, admission.Invocation.RunId);
                            Assert.Equal(EtlStoreStatus.LeaseLost, (await store.ReserveBatchAsync(old, batch, token)).Status);
                            Interlocked.Increment(ref staleRefusals);
                            Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(current, batch, token)).Status);
                            Assert.Equal(EtlStoreStatus.Applied, (await store.AcceptBatchAsync(current, CreateAcceptance(admission, batch, 2, 0, 0), token)).Status);
                        }
                        completed.Enqueue(filePreparation.RunId);
                    }
                    else if (await store.ClaimConnectorPreparationAsync(scope, worker, token) is { } connectorPreparation)
                    {
                        var admission = connectors[connectorPreparation.RunId];
                        var prepared = new EtlConnectorPreparedRun(null, null);
                        Assert.Equal(EtlStoreStatus.Applied, await store.CommitConnectorPreparationAsync(connectorPreparation, prepared, token));
                        for (var partition = 0; partition < 2; partition++)
                        {
                            var old = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(scope, connectorPreparation.RunId, worker, token));
                            Assert.Equal(EtlStoreStatus.Applied, await store.ReleaseConnectorPartitionAsync(old, token));
                            var current = Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(scope, connectorPreparation.RunId, worker, token));
                            Assert.True(current.Fence > old.Fence);
                            var attached = await store.AttachTestHttpPageAsync(current, RetainedPage(current, admission.Invocation.RunId, 10, 1), token);
                            Assert.Equal(EtlStoreStatus.Applied, attached.Status);
                            current = attached.Lease!;
                            var batch = HttpAcceptanceBatch(admission, prepared, current, 0, 1, admission.Invocation.RunId);
                            Assert.Equal(EtlStoreStatus.LeaseLost, (await store.ReserveConnectorBatchAsync(old, batch, token)).Status);
                            Interlocked.Increment(ref staleRefusals);
                            Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveConnectorBatchAsync(current, batch, token)).Status);
                            Assert.Equal(EtlStoreStatus.Applied, (await store.RetainConnectorBatchOutputAsync(current, batch.BatchId, new(0, 0, 1, null, null), token)).Status);
                            Assert.Equal(EtlStoreStatus.Applied, (await store.AcceptConnectorBatchAsync(current, batch.BatchId, token)).Status);
                        }
                        completed.Enqueue(connectorPreparation.RunId);
                    }
                    else return;
                }
            }
            catch
            {
                await cancellation.CancelAsync();
                throw;
            }
        }

        async Task DeliverAsync(int ordinal)
        {
            try
            {
                await using var context = database.CreateContext(new NativeRaceCommand(conflicts));
                var store = ((IEtlRuntimeTestDatabase)database).CreateStore(context, clock);
                while (await store.ClaimDeliveryAsync(scope, $"delivery-{ordinal}", token) is { } delivery)
                {
                    var invocation = files.TryGetValue(delivery.RunId, out var file) ? file.Invocation : connectors[delivery.RunId].Invocation;
                    Assert.Equal(invocation.Scope, delivery.Scope);
                    Assert.Equal(invocation.ResultAddress, delivery.Address);
                    Assert.True(deliveredResults.TryAdd(delivery.RunId, (delivery.Result, delivery.PayloadHash)));
                    Assert.Equal(EtlStoreStatus.Applied, await store.AcknowledgeDeliveryAsync(delivery, EtlDeliveryDisposition.Consumed, token));
                    delivered.Enqueue(delivery.DeliveryId);
                }
            }
            catch
            {
                await cancellation.CancelAsync();
                throw;
            }
        }
    }
}
