using System.Data.Common;
using System.Security.Cryptography;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Preparation;
using Elsa.Etl.Runtime.Output;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Runtime_module_migrates_only_its_tables_and_registers_no_design_services()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        var tables = await context.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToListAsync();
        Assert.Contains(EtlRuntimeEfModule.RunTableName, tables);
        Assert.Contains(EtlRuntimeEfModule.ManifestTableName, tables);
        Assert.DoesNotContain(EtlDesignEfModule.DraftTableName, tables);

        var services = new ServiceCollection();
        new EtlRuntimeEntityFrameworkCoreFeature().ConfigureServices(services);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IEtlRuntimeStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IEtlDefinitionStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IEtlArtifactCompiler));
    }

    [Fact]
    public async Task Admission_is_idempotent_scoped_and_cancellation_tombstone_binds_the_result_address()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var scope = new EtlScope(null, "partition-a");
        var admission = CreateAdmission("run-null", scope);

        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(admission)).Status);
        Assert.Equal(EtlStoreStatus.Replay, (await store.AdmitAsync(admission)).Status);
        Assert.Equal(EtlStoreStatus.Conflict,
            (await store.AdmitAsync(admission with { Bindings = admission.Bindings with { Source = admission.Bindings.Source with { Revision = "v2" } } })).Status);

        var tenantDefault = CreateAdmission("run-null", new EtlScope("default", "partition-a"));
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(tenantDefault)).Status);
        Assert.NotNull(await store.FindRunAsync(scope, admission.Invocation.RunId));
        Assert.NotNull(await store.FindRunAsync(tenantDefault.Invocation.Scope, tenantDefault.Invocation.RunId));

        var unsupportedArtifact = new EtlArtifact(2, admission.Invocation.Artifact.ArtifactId,
            admission.Invocation.Artifact.ContentHash, admission.Invocation.Artifact.DefinitionId,
            admission.Invocation.Artifact.DefinitionVersion, admission.Invocation.Artifact.Pipeline);
        var unsupported = admission with { Invocation = admission.Invocation with { RunId = "unsupported", Artifact = unsupportedArtifact } };
        Assert.Equal(EtlStoreStatus.Invalid, (await store.AdmitAsync(unsupported)).Status);

        var cancellation = CreateAdmission("cancel-before-admit", scope);
        Assert.Equal(EtlStoreStatus.Applied, (await store.CancelAsync(cancellation.Invocation)).Status);
        Assert.Equal(EtlStoreStatus.Replay, (await store.CancelAsync(cancellation.Invocation)).Status);
        Assert.Equal(EtlStoreStatus.Conflict,
            (await store.AdmitAsync(cancellation)).Status);
        var changedAddress = cancellation.Invocation with
        {
            ResultAddress = cancellation.Invocation.ResultAddress with { BookmarkId = "different-bookmark" }
        };
        Assert.Equal(EtlStoreStatus.Conflict, (await store.CancelAsync(changedAddress)).Status);
        var tombstone = await store.FindRunAsync(scope, cancellation.Invocation.RunId);
        Assert.Equal(EtlRunStatus.Cancelled, tombstone!.Status);
        Assert.Null(tombstone.Bindings);
        Assert.NotNull(await store.ClaimDeliveryAsync(scope, "delivery-worker"));
    }

    [Fact]
    public async Task Partition_claims_reserve_before_dispatch_reuse_owed_work_and_fence_released_attempts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context, clock, partitionLease: TimeSpan.FromSeconds(5));
        var pipeline = EtlTestPipelines.Create() with
        {
            Limits = EtlTestPipelines.Create().Limits with
            {
                MaxRecordBytes = 1024,
                MaxBatchRecords = 2,
                MaxBatchBytes = 1024,
                MaxOutstandingRecords = 2,
                MaxOutstandingBytes = 1024
            }
        };
        var admission = CreateAdmission("run-reserve", new EtlScope("tenant-a", "partition-a"), pipeline);
        await StartAsync(store, admission, 4);

        var first = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker-a"));
        var second = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker-b"));
        Assert.Equal(0, first.PartitionOrdinal);
        Assert.Equal(1, second.PartitionOrdinal);
        var firstBatch = CreateBatch(admission, EtlRuntimeFixtures.Prepared(4), first.PartitionOrdinal, 1, 3, "input-a");
        var secondBatch = CreateBatch(admission, EtlRuntimeFixtures.Prepared(4), second.PartitionOrdinal, 3, 5, "input-b");

        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(first, firstBatch)).Status);
        Assert.Equal(EtlStoreStatus.BudgetExceeded, (await store.ReserveBatchAsync(second, secondBatch)).Status);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleasePartitionAsync(second));
        Assert.Equal(EtlStoreStatus.LeaseLost, (await store.ReserveBatchAsync(second, secondBatch)).Status);

        Assert.Equal(EtlStoreStatus.Applied, await store.ReleasePartitionAsync(first));
        Assert.Equal(EtlStoreStatus.LeaseLost, (await store.AcceptBatchAsync(first, CreateAcceptance(admission, firstBatch, 2, 0, 0))).Status);

        var retry = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker-c"));
        Assert.Equal(first.PartitionOrdinal, retry.PartitionOrdinal);
        Assert.True(retry.Fence > first.Fence);
        Assert.Equal(EtlStoreStatus.Replay, (await store.ReserveBatchAsync(retry, firstBatch)).Status);
        Assert.Equal(EtlStoreStatus.Conflict,
            (await store.ReserveBatchAsync(retry, firstBatch with { InputHash = new string('f', 64) })).Status);
        var before = await store.FindRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        Assert.Equal(2, before!.OutstandingRecords);
        Assert.Equal(100, before.OutstandingBytes);
        Assert.Equal(EtlStoreStatus.Applied, (await store.AcceptBatchAsync(retry, CreateAcceptance(admission, firstBatch, 2, 0, 0))).Status);

        var last = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker-d"));
        Assert.Equal(second.PartitionOrdinal, last.PartitionOrdinal);
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(last, secondBatch)).Status);
        Assert.Equal(EtlStoreStatus.Applied, (await store.AcceptBatchAsync(last, CreateAcceptance(admission, secondBatch, 2, 0, 0))).Status);

        var completed = await store.FindRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        Assert.Equal(EtlRunStatus.Succeeded, completed!.Status);
        Assert.Equal(4, completed.AcceptedRecords);
        Assert.NotNull(completed.TerminalResult);
        var delivery = Assert.IsType<EtlDeliveryLease>(await store.ClaimDeliveryAsync(admission.Invocation.Scope, "delivery-worker"));
        Assert.Equal(EtlStoreStatus.Applied, await store.AcknowledgeDeliveryAsync(delivery, EtlDeliveryDisposition.Consumed));
        Assert.Equal(EtlStoreStatus.Replay, await store.AcknowledgeDeliveryAsync(delivery, EtlDeliveryDisposition.Consumed));
        Assert.Null(await store.ClaimDeliveryAsync(admission.Invocation.Scope, "delivery-worker-2"));
    }

    [Fact]
    public async Task Expired_delivery_claim_is_fenced_until_consumer_proof_settles_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context, clock);
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create() with
        {
            Limits = new EtlLimits { PartitionCount = 1, MaxBatchRecords = 2 }
        };
        var admission = CreateAdmission("run-delivery-fence", scope, pipeline);
        await StartAsync(store, admission, 2);

        var partition = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(scope, admission.Invocation.RunId, "partition-worker"));
        var batch = CreateBatch(admission, EtlRuntimeFixtures.Prepared(2), partition.PartitionOrdinal,
            partition.StartOrdinal, partition.StartOrdinal + 2, "reserved-before-cancel");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(partition, batch)).Status);
        Assert.Equal(EtlStoreStatus.Applied, (await store.CancelAsync(admission.Invocation)).Status);

        var terminalBefore = Assert.IsType<EtlRunView>(await store.FindRunAsync(scope, admission.Invocation.RunId));
        Assert.Equal(EtlRunStatus.Cancelled, terminalBefore.Status);
        Assert.Equal(2, terminalBefore.OutstandingRecords);
        Assert.Equal(batch.InputBytes, terminalBefore.OutstandingBytes);
        Assert.NotNull(terminalBefore.TerminalResult);

        var ownerA = Assert.IsType<EtlDeliveryLease>(await store.ClaimDeliveryAsync(scope, "delivery-worker-a"));
        clock.Advance(TimeSpan.FromSeconds(31));
        var ownerB = Assert.IsType<EtlDeliveryLease>(await store.ClaimDeliveryAsync(scope, "delivery-worker-b"));

        Assert.Equal(ownerA.DeliveryId, ownerB.DeliveryId);
        Assert.Equal(ownerA.Result, ownerB.Result);
        Assert.Equal(ownerA.Address, ownerB.Address);
        Assert.Equal(ownerA.PayloadHash, ownerB.PayloadHash);
        Assert.True(ownerB.Fence > ownerA.Fence);

        var outboxBeforeLateAck = await context.Outbox.AsNoTracking().SingleAsync(row => row.DeliveryId == ownerA.DeliveryId);
        Assert.Null(outboxBeforeLateAck.Disposition);
        Assert.Null(outboxBeforeLateAck.AcknowledgedAtUtcTicks);
        Assert.Equal(ownerB.Fence, outboxBeforeLateAck.Fence);

        Assert.Equal(EtlStoreStatus.LeaseLost,
            await store.AcknowledgeDeliveryAsync(ownerA, EtlDeliveryDisposition.Consumed));

        var outboxAfterLateAck = await context.Outbox.AsNoTracking().SingleAsync(row => row.DeliveryId == ownerA.DeliveryId);
        Assert.Equal(outboxBeforeLateAck.Owner, outboxAfterLateAck.Owner);
        Assert.Equal(outboxBeforeLateAck.Fence, outboxAfterLateAck.Fence);
        Assert.Equal(outboxBeforeLateAck.ExpiresAtUtcTicks, outboxAfterLateAck.ExpiresAtUtcTicks);
        Assert.Null(outboxAfterLateAck.Disposition);
        Assert.Null(outboxAfterLateAck.AcknowledgedAtUtcTicks);

        var terminalAfterLateAck = Assert.IsType<EtlRunView>(await store.FindRunAsync(scope, admission.Invocation.RunId));
        Assert.Equal(
            (terminalBefore.Status, terminalBefore.OutstandingRecords, terminalBefore.OutstandingBytes,
                terminalBefore.AcceptedRecords, terminalBefore.RejectedRecords, terminalBefore.FilteredRecords, terminalBefore.TerminalResult),
            (terminalAfterLateAck.Status, terminalAfterLateAck.OutstandingRecords, terminalAfterLateAck.OutstandingBytes,
                terminalAfterLateAck.AcceptedRecords, terminalAfterLateAck.RejectedRecords, terminalAfterLateAck.FilteredRecords,
                terminalAfterLateAck.TerminalResult));

        // A test consumer's typed proof is the store boundary input; native delivery is covered separately.
        var consumedProof = EtlDeliveryDisposition.Consumed;
        Assert.Equal(EtlStoreStatus.Applied, await store.AcknowledgeDeliveryAsync(ownerB, consumedProof));
        Assert.Equal(EtlStoreStatus.Replay, await store.AcknowledgeDeliveryAsync(ownerB, consumedProof));
        // Once that identical proof is recorded, replay does not require the old claim to remain current.
        Assert.Equal(EtlStoreStatus.Replay, await store.AcknowledgeDeliveryAsync(ownerA, consumedProof));
        Assert.Equal(EtlStoreStatus.Conflict,
            await store.AcknowledgeDeliveryAsync(ownerB, EtlDeliveryDisposition.AlreadyConsumed));

        var acknowledged = await context.Outbox.AsNoTracking().SingleAsync(row => row.DeliveryId == ownerA.DeliveryId);
        Assert.Equal((int)consumedProof, acknowledged.Disposition);
        Assert.NotNull(acknowledged.AcknowledgedAtUtcTicks);
        var terminalAfterSettlement = Assert.IsType<EtlRunView>(await store.FindRunAsync(scope, admission.Invocation.RunId));
        Assert.Equal(terminalBefore.TerminalResult, terminalAfterSettlement.TerminalResult);
        Assert.Equal(terminalBefore.OutstandingRecords, terminalAfterSettlement.OutstandingRecords);
        Assert.Equal(terminalBefore.OutstandingBytes, terminalAfterSettlement.OutstandingBytes);
    }

    [Fact]
    public async Task Last_acceptance_atomically_completes_with_business_error_and_manifest_pages_are_ordered()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var scope = new EtlScope("tenant-a", "partition-a");
        var admission = CreateAdmission("run-rejected", scope);
        await StartAsync(store, admission, 4);

        var first = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(scope, admission.Invocation.RunId, "worker-a"));
        var batch0 = CreateBatch(admission, EtlRuntimeFixtures.Prepared(4), first.PartitionOrdinal, 1, 3, "input-a");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(first, batch0)).Status);
        Assert.Equal(EtlStoreStatus.Applied, (await store.AcceptBatchAsync(first, CreateAcceptance(admission, batch0, 1, 1, 0))).Status);

        var second = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(scope, admission.Invocation.RunId, "worker-b"));
        var batch1 = CreateBatch(admission, EtlRuntimeFixtures.Prepared(4), second.PartitionOrdinal, 3, 5, "input-b");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(second, batch1)).Status);
        Assert.Equal(EtlStoreStatus.Applied, (await store.AcceptBatchAsync(second, CreateAcceptance(admission, batch1, 2, 0, 0))).Status);

        var completed = await store.FindRunAsync(scope, admission.Invocation.RunId);
        Assert.Equal(EtlRunStatus.BusinessError, completed!.Status);
        Assert.Equal(EtlTerminalOutcome.BusinessError, completed.TerminalResult!.Outcome);
        Assert.Equal("etl.rows-rejected", completed.TerminalResult.Diagnostic!.Code);
        var firstPage = await store.ListManifestAsync(scope, admission.Invocation.RunId, null, 1);
        var nextPage = await store.ListManifestAsync(scope, admission.Invocation.RunId, firstPage[0].Position, 1);
        var finalPage = await store.ListManifestAsync(scope, admission.Invocation.RunId, nextPage[0].Position, 1);
        Assert.Single(firstPage);
        Assert.Single(nextPage);
        Assert.Single(finalPage);
        Assert.Equal(EtlOutputRoute.Main, firstPage[0].Position.Route);
        Assert.Equal(EtlOutputRoute.Error, nextPage[0].Position.Route);
        Assert.Equal(1, finalPage[0].Position.PartitionOrdinal);
        var delivery = Assert.IsType<EtlDeliveryLease>(await store.ClaimDeliveryAsync(scope, "delivery-worker"));
        Assert.Equal(1, delivery.Result.RejectedRecords);
        Assert.Single(await store.ListManifestAsync(scope, admission.Invocation.RunId, null, 5),
            entry => entry.Effect.Route == EtlOutputRoute.Error);
    }

    [Fact]
    public async Task Empty_source_is_terminalized_with_outbox_during_preparation()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = CreateAdmission("run-empty", new EtlScope(null, "partition-a"));
        await store.AdmitAsync(admission);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(admission.Invocation.Scope, "prep-worker"));

        Assert.Equal(EtlStoreStatus.Applied, await store.CommitPreparationAsync(preparation, EtlRuntimeFixtures.Prepared(0)));
        var run = await store.FindRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        Assert.Equal(EtlRunStatus.Succeeded, run!.Status);
        Assert.Equal(0, run.TerminalResult!.AcceptedRecords);
        Assert.NotNull(await store.ClaimDeliveryAsync(admission.Invocation.Scope, "delivery-worker"));
    }

    [Fact]
    public async Task Unscoped_partition_claim_selects_only_a_runnable_run_in_the_exact_scope()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var scope = new EtlScope("tenant-a", "partition-a");
        var first = CreateAdmission("run-first", scope);
        var otherTenant = CreateAdmission("run-other-tenant", new EtlScope("tenant-b", "partition-a"));
        await StartAsync(store, first, 4);
        await StartAsync(store, otherTenant, 4);

        var claimed = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(scope, null, "scoped-worker"));
        Assert.Equal(first.Invocation.RunId, claimed.RunId);
        Assert.NotEqual(otherTenant.Invocation.RunId, claimed.RunId);
    }

    [Fact]
    public async Task Preparation_refuses_snapshots_that_do_not_match_the_pinned_source_hash()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = CreateAdmission("run-snapshot-hash", new EtlScope("tenant-a", "partition-a"));
        await store.AdmitAsync(admission);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(admission.Invocation.Scope, "prep-worker"));
        var changed = EtlRuntimeFixtures.Prepared(4) with
        {
            Source = EtlRuntimeFixtures.Prepared(4).Source with { ContentHash = new string('f', 64) }
        };

        Assert.Equal(EtlStoreStatus.Invalid, await store.CommitPreparationAsync(preparation, changed));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitPreparationAsync(preparation, EtlRuntimeFixtures.Prepared(4)));
    }

    [Fact]
    public async Task Preparation_persists_the_real_one_based_nonempty_partition_plan()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var root = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Preparer", Guid.NewGuid().ToString("N"));
        var snapshotsRoot = Path.Combine(root, "snapshots");
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "source.csv");
            var lookupPath = Path.Combine(root, "lookup.csv");
            await File.WriteAllTextAsync(sourcePath, "id,name\n1,one\n2,two\n3,three\n4,four\n");
            await File.WriteAllTextAsync(lookupPath, "id,category\n1,item one\n2,item two\n3,item three\n4,item four\n");
            var scope = new EtlScope("tenant-real", "partition-real");
            var hostBindings = new HostFileBindings(
            [
                Input(scope, "source", EtlFileRole.Source, sourcePath),
                Input(scope, "lookup", EtlFileRole.Lookup, lookupPath),
                Output(scope, "output", EtlFileRole.MainOutput, Path.Combine(root, "output")),
                Output(scope, "errors", EtlFileRole.ErrorOutput, Path.Combine(root, "errors"))
            ]);
            var sourcePin = hostBindings.Resolve(scope, "source", EtlFileRole.Source);
            var lookupPin = hostBindings.Resolve(scope, "lookup", EtlFileRole.Lookup);
            var pipeline = EtlTestPipelines.Create();
            var created = await new EtlFilePreparer(hostBindings, snapshotsRoot)
                .CreateSnapshotsAsync(scope, "run-real-preparer", pipeline, sourcePin, lookupPin);
            Assert.True(created.Succeeded, created.Diagnostic?.Code);
            var result = await new EtlFilePreparer(hostBindings, snapshotsRoot).PrepareAsync(pipeline, created.Snapshots!);
            Assert.True(result.Succeeded, result.Diagnostic?.Code);
            var descriptor = result.Prepared!;
            Assert.Equal(4, descriptor.SourceRecordCount);
            Assert.Equal(120, descriptor.SourceIndex.ByteLength);

            var template = EtlRuntimeFixtures.Admission("run-real-preparer", scope);
            var artifact = Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", 1, pipeline).Artifact);
            var admission = template with
            {
                Invocation = template.Invocation with { Artifact = artifact },
                Bindings = new(
                    Binding(sourcePin), Binding(lookupPin),
                    Binding(hostBindings.Resolve(scope, "output", EtlFileRole.MainOutput)),
                    Binding(hostBindings.Resolve(scope, "errors", EtlFileRole.ErrorOutput)))
            };
            Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(admission)).Status);
            var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(scope, "prep-worker"));
            var prepared = new EtlPreparedRun(1,
                Reference(descriptor.Inputs.SourceChunk), Reference(descriptor.Inputs.LookupChunk),
                Reference(descriptor.SourceIndex), descriptor.SourceRecordCount);

            Assert.Equal(EtlStoreStatus.Applied, await store.CommitPreparationAsync(preparation, prepared));
            var ranges = await store.ListPartitionsAsync(scope, admission.Invocation.RunId, -1, 10);
            Assert.Equal(new long[] { 1, 3 }, ranges.Select(x => x.StartOrdinal));
            Assert.Equal(new long[] { 3, 5 }, ranges.Select(x => x.EndOrdinalExclusive));

            await using var reopenedContext = database.CreateContext();
            var reopened = database.CreateStore(reopenedContext);
            var recovered = await reopened.FindRunAsync(scope, admission.Invocation.RunId);
            Assert.Equal(EtlRunStatus.Running, recovered!.Status);
            Assert.Equal(descriptor.SourceRecordCount, recovered.Prepared!.SourceRecordCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Preparation_lease_expiry_requires_a_new_fence_and_reject_admission_is_durable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context, clock, preparationLease: TimeSpan.FromSeconds(5));
        var admission = CreateAdmission("run-prep", new EtlScope("tenant-a", "partition-a"));
        await store.AdmitAsync(admission);
        var old = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(admission.Invocation.Scope, "prep-a"));
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(EtlStoreStatus.LeaseLost, await store.RenewPreparationAsync(old));
        var current = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(admission.Invocation.Scope, "prep-b"));
        Assert.True(current.Fence > old.Fence);
        Assert.Equal(EtlStoreStatus.LeaseLost, await store.CommitPreparationAsync(old, EtlRuntimeFixtures.Prepared(4)));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitPreparationAsync(current, EtlRuntimeFixtures.Prepared(4)));

        var refused = CreateAdmission("run-refused", admission.Invocation.Scope);
        var refusal = await store.RejectAdmissionAsync(refused.Invocation, new EtlRunDiagnostic("source.revision-unavailable"));
        Assert.Equal(EtlStoreStatus.Applied, refusal.Status);
        Assert.Equal(EtlTerminalOutcome.BusinessError, refusal.Result!.Outcome);
        var run = await store.FindRunAsync(refused.Invocation.Scope, refused.Invocation.RunId);
        Assert.Null(run!.Bindings);
        Assert.NotNull(await store.ClaimDeliveryAsync(refused.Invocation.Scope, "delivery-worker"));
    }

    [Fact]
    public async Task Admission_refusal_cannot_terminalize_an_already_pinned_invocation_and_persisted_json_is_bounded()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = CreateAdmission("run-binding-race", new EtlScope("tenant-a", "partition-a"));

        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(admission)).Status);
        Assert.Equal(EtlStoreStatus.Conflict,
            (await store.RejectAdmissionAsync(admission.Invocation,
                new EtlRunDiagnostic("source.revision-unavailable", Field: new string('x', 200)))).Status);
        var active = await store.FindRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        Assert.Equal(EtlRunStatus.Preparing, active!.Status);
        Assert.NotNull(active.Bindings);
        Assert.Null(active.TerminalResult);
        Assert.Null(await store.ClaimDeliveryAsync(admission.Invocation.Scope, "delivery-worker"));

        Assert.Throws<InvalidDataException>(() => EtlRuntimeRecordCodec.ReadInvocation(new string(' ', 512 * 1024 + 1)));
        Assert.Throws<InvalidDataException>(() => EtlRuntimeRecordCodec.ReadBindings(new string(' ', 64 * 1024 + 1)));
        Assert.Throws<InvalidDataException>(() => EtlRuntimeRecordCodec.Read<EtlTerminalResult>(new string(' ', 16 * 1024 + 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Composite_run_views_never_mix_run_and_terminal_states(bool listRuns)
    {
        await using var database = await TestDatabase.CreateAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        var admission = CreateAdmission("run-read-snapshot", scope);
        EtlPreparationLease preparation;
        await using (var setupContext = database.CreateContext())
        {
            var setup = database.CreateStore(setupContext);
            await setup.AdmitAsync(admission);
            preparation = Assert.IsType<EtlPreparationLease>(await setup.ClaimPreparationAsync(scope, "prep-worker"));
        }

        var barrier = new TableReadBarrier(EtlRuntimeEfModule.TerminalTableName);
        await using var readerContext = database.CreateContext(barrier);
        var reader = database.CreateStore(readerContext);
        var readTask = ReadAsync(reader, scope, admission.Invocation.RunId, listRuns);
        await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(5));

        await using var writerContext = database.CreateContext();
        var writer = database.CreateStore(writerContext);
        var writerTask = writer.CommitPreparationAsync(preparation, EtlRuntimeFixtures.Prepared(0)).AsTask();
        try
        {
            Assert.Equal(EtlStoreStatus.Applied, await writerTask.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            barrier.Release();
        }
        var view = await readTask.WaitAsync(TimeSpan.FromSeconds(5));
        await writerTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(view);
        Assert.False(view.Status == EtlRunStatus.Preparing && view.TerminalResult is not null,
            "A composite read returned a preparing run paired with a terminal result committed between its queries.");
        Assert.False(view.Status == EtlRunStatus.Succeeded && view.TerminalResult is null,
            "A composite read returned a succeeded run without its terminal result.");
    }

    [Fact]
    public async Task New_admission_builds_its_composite_view_before_committing_the_admission()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scope = new EtlScope("tenant-a", "partition-a");
        var admission = CreateAdmission("run-admission-read-snapshot", scope);
        var barrier = new TableReadBarrier(EtlRuntimeEfModule.TerminalTableName);
        var commitObserver = new TransactionCommitObserver();
        await using var admissionContext = database.CreateContext(barrier, commitObserver);
        var admissionStore = database.CreateStore(admissionContext);

        var admissionTask = admissionStore.AdmitAsync(admission).AsTask();
        await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        var committedBeforeViewCompleted = commitObserver.CommitStarted;
        barrier.Release();
        var result = await admissionTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(EtlStoreStatus.Applied, result.Status);
        Assert.NotNull(result.Run);
        Assert.False(committedBeforeViewCompleted, "Admission committed before it read the view it returned.");
        Assert.Equal(EtlRunStatus.Preparing, result.Run.Status);
        Assert.Null(result.Run.TerminalResult);
        Assert.True(commitObserver.CommitStarted);
    }

    [Fact]
    public async Task Acceptance_snapshots_bounded_effects_before_database_queries()
    {
        await using var database = await TestDatabase.CreateAsync();
        var barrier = new TableReadBarrier(EtlRuntimeEfModule.ManifestTableName);
        await using var context = database.CreateContext(barrier);
        var store = database.CreateStore(context, new MutableTimeProvider());
        var pipeline = EtlTestPipelines.Create() with { Limits = new EtlLimits { PartitionCount = 1, MaxBatchRecords = 2 } };
        var admission = CreateAdmission("run-effect-snapshot", new EtlScope("tenant-a", "partition-a"), pipeline);
        await StartAsync(store, admission, 2);
        var lease = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker"));
        var batch = CreateBatch(admission, EtlRuntimeFixtures.Prepared(2), lease.PartitionOrdinal, lease.StartOrdinal, lease.StartOrdinal + 2, "two-effects");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(lease, batch)).Status);

        var acceptance = CreateAcceptance(admission, batch, 1, 1, 0);
        var effects = acceptance.Effects.ToArray();
        acceptance = acceptance with { Effects = effects };
        Assert.Equal(EtlStoreStatus.Invalid,
            (await store.AcceptBatchAsync(lease, acceptance with { Effects = [effects[0], effects[1], effects[0]] })).Status);
        var expectedErrorKey = effects.Single(effect => effect.Route == EtlOutputRoute.Error).Chunk.Key;
        var acceptTask = store.AcceptBatchAsync(lease, acceptance).AsTask();
        try
        {
            await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(5));
            var errorIndex = Array.FindIndex(effects, effect => effect.Route == EtlOutputRoute.Error);
            effects[errorIndex] = effects[errorIndex] with
            {
                Chunk = effects[errorIndex].Chunk with { Key = "forged-after-validation" }
            };
        }
        finally
        {
            barrier.Release();
        }

        Assert.Equal(EtlStoreStatus.Applied, (await acceptTask.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        var manifest = await store.ListManifestAsync(admission.Invocation.Scope, admission.Invocation.RunId, null, 10);
        Assert.Equal(expectedErrorKey, Assert.Single(manifest, item => item.Position.Route == EtlOutputRoute.Error).Effect.Chunk.Key);
    }

    private static async Task<EtlRunView?> ReadAsync(EfEtlRuntimeStore store, EtlScope scope, string runId, bool listRuns)
    {
        if (!listRuns)
            return await store.FindRunAsync(scope, runId);
        return Assert.Single(await store.ListRunsAsync(scope, null, 10), run => run.Invocation.RunId == runId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reused_context_refuses_old_acceptance_after_another_context_takes_over(bool probeOtherOperations)
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new MutableTimeProvider();
        await using var oldContext = database.CreateContext();
        var oldStore = database.CreateStore(oldContext, clock, partitionLease: TimeSpan.FromSeconds(5));
        var admission = CreateAdmission("run-context-fence", new EtlScope("tenant-a", "partition-a"));
        await StartAsync(oldStore, admission, 4);
        var oldLease = Assert.IsType<EtlPartitionLease>(await oldStore.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker-old"));
        var prepared = EtlRuntimeFixtures.Prepared(4);
        var batch = CreateBatch(admission, prepared, oldLease.PartitionOrdinal, oldLease.StartOrdinal, oldLease.StartOrdinal + 2, "retry-input");
        Assert.Equal(EtlStoreStatus.Applied, (await oldStore.ReserveBatchAsync(oldLease, batch)).Status);
        Assert.Equal(oldLease.Owner, Assert.Single(await oldStore.ListPartitionsAsync(admission.Invocation.Scope, admission.Invocation.RunId, -1, 10),
            view => view.PartitionOrdinal == oldLease.PartitionOrdinal).Owner);

        await using var newContext = database.CreateContext();
        var newStore = database.CreateStore(newContext, clock, partitionLease: TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(6));
        var current = Assert.IsType<EtlPartitionLease>(await newStore.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker-new"));
        Assert.True(current.Fence > oldLease.Fence);
        Assert.Equal("worker-new", Assert.Single(await oldStore.ListPartitionsAsync(admission.Invocation.Scope, admission.Invocation.RunId, -1, 10),
            view => view.PartitionOrdinal == current.PartitionOrdinal).Owner);

        if (probeOtherOperations)
        {
            Assert.Equal(EtlStoreStatus.LeaseLost, await oldStore.RenewPartitionAsync(oldLease));
            Assert.Equal(EtlStoreStatus.LeaseLost, (await oldStore.ReserveBatchAsync(oldLease, batch)).Status);
        }
        Assert.Equal(EtlStoreStatus.LeaseLost,
            (await oldStore.AcceptBatchAsync(oldLease, CreateAcceptance(admission, batch, 2, 0, 0))).Status);
        Assert.Equal(EtlStoreStatus.Replay, (await newStore.ReserveBatchAsync(current, batch)).Status);
        var unchanged = await oldStore.FindRunAsync(admission.Invocation.Scope, admission.Invocation.RunId);
        Assert.Equal(2, unchanged!.OutstandingRecords);
        Assert.Empty(await oldStore.ListManifestAsync(admission.Invocation.Scope, admission.Invocation.RunId, null, 10));
        Assert.Equal(1, Assert.Single(await oldStore.ListPartitionsAsync(admission.Invocation.Scope, admission.Invocation.RunId, -1, 10),
            view => view.PartitionOrdinal == current.PartitionOrdinal).NextOrdinal);
    }

    [Fact]
    public async Task Renewal_and_release_follow_ownership_while_reservation_still_requires_the_current_cursor()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context, new MutableTimeProvider());
        var pipeline = EtlTestPipelines.Create() with { Limits = new EtlLimits { PartitionCount = 1, MaxBatchRecords = 2 } };
        var admission = CreateAdmission("renew-after-accept", new EtlScope(null, "partition"), pipeline);
        await StartAsync(store, admission, 4);
        var lease = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker"));
        var first = CreateBatch(admission, EtlRuntimeFixtures.Prepared(4), 0, 1, 3, "first");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(lease, first)).Status);
        Assert.Equal(3, (await store.AcceptBatchAsync(lease, CreateAcceptance(admission, first, 2, 0, 0))).Lease!.NextOrdinal);
        // A heartbeat can have captured this lease just before acceptance advanced the cursor.
        Assert.Equal(EtlStoreStatus.Applied, await store.RenewPartitionAsync(lease));
        var next = CreateBatch(admission, EtlRuntimeFixtures.Prepared(4), 0, 3, 5, "next");
        Assert.Equal(EtlStoreStatus.LeaseLost, (await store.ReserveBatchAsync(lease, next)).Status);
        Assert.Equal(EtlStoreStatus.Applied, await store.ReleasePartitionAsync(lease));
        var successor = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "next-worker"));
        Assert.Equal(3, successor.NextOrdinal);
        Assert.True(successor.Fence > lease.Fence);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Settled_replay_refuses_changed_counters_or_duplicate_route_substitution(bool duplicateRoute)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context, new MutableTimeProvider());
        var pipeline = EtlTestPipelines.Create() with { Limits = new EtlLimits { PartitionCount = 1, MaxBatchRecords = 4 } };
        var admission = CreateAdmission("replay-acceptance", new EtlScope(null, "partition"), pipeline);
        await StartAsync(store, admission, 8);
        var lease = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker"));
        var batch = CreateBatch(admission, EtlRuntimeFixtures.Prepared(8), 0, 1, 5, "first");
        Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(lease, batch)).Status);
        var acceptance = CreateAcceptance(admission, batch, 2, 1, 1);
        var accepted = await store.AcceptBatchAsync(lease, acceptance);
        Assert.Equal(EtlStoreStatus.Applied, accepted.Status);
        var changed = duplicateRoute
            ? acceptance with { Effects = [acceptance.Effects[0], acceptance.Effects[0]] }
            : acceptance with { AcceptedRecords = 1, FilteredRecords = 2 };
        Assert.Equal(EtlStoreStatus.Conflict, (await store.AcceptBatchAsync(accepted.Lease!, changed)).Status);
        var run = Assert.IsType<EtlRunView>(await store.FindRunAsync(admission.Invocation.Scope, admission.Invocation.RunId));
        Assert.Equal(2, run.AcceptedRecords);
        Assert.Equal(1, run.RejectedRecords);
        Assert.Equal(1, run.FilteredRecords);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Nonempty_work_cannot_claim_zero_input_or_output_bytes(bool zeroInput)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context, new MutableTimeProvider());
        var admission = CreateAdmission("zero-byte", new EtlScope(null, "partition"));
        await StartAsync(store, admission, 4);
        var lease = Assert.IsType<EtlPartitionLease>(await store.ClaimPartitionAsync(admission.Invocation.Scope, admission.Invocation.RunId, "worker"));
        var batch = CreateBatch(admission, EtlRuntimeFixtures.Prepared(4), 0, 1, 3, "first");
        if (zeroInput)
        {
            batch = batch with { InputBytes = 0, BatchId = EtlRuntimeIdentity.BatchId(admission, EtlRuntimeFixtures.Prepared(4), 0, 1, 3, 0, batch.InputHash) };
            Assert.Equal(EtlStoreStatus.Invalid, (await store.ReserveBatchAsync(lease, batch)).Status);
        }
        else
        {
            Assert.Equal(EtlStoreStatus.Applied, (await store.ReserveBatchAsync(lease, batch)).Status);
            var acceptance = CreateAcceptance(admission, batch, 2, 0, 0);
            var effect = acceptance.Effects[0];
            acceptance = acceptance with { Effects = [effect with { Chunk = effect.Chunk with { ByteLength = 0 } }] };
            Assert.Equal(EtlStoreStatus.Invalid, (await store.AcceptBatchAsync(lease, acceptance)).Status);
        }
        Assert.Empty(await store.ListManifestAsync(admission.Invocation.Scope, admission.Invocation.RunId, null, 10));
    }

    [Fact]
    public async Task Persisted_invocation_preserves_opaque_scope_code_units_without_JSON_replacement()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scope = new EtlScope("tenant-\ud800", "partition-\ud801");
        var admission = CreateAdmission("opaque-scope", scope);
        await using (var context = database.CreateContext())
            Assert.Equal(EtlStoreStatus.Applied, (await database.CreateStore(context).AdmitAsync(admission)).Status);
        await using (var context = database.CreateContext())
        {
            var store = database.CreateStore(context);
            var reopened = Assert.IsType<EtlRunView>(await store.FindRunAsync(scope, admission.Invocation.RunId));
            Assert.Equal(scope, reopened.Invocation.Scope);
            Assert.Null(await store.FindRunAsync(new EtlScope("tenant-\ufffd", "partition-\ufffd"), admission.Invocation.RunId));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Persisted_invocation_preserves_opaque_run_and_address_code_units(bool addressIdentity)
    {
        await using var database = await TestDatabase.CreateAsync();
        var scope = new EtlScope(null, "partition");
        var admission = CreateAdmission(addressIdentity ? "address-roundtrip" : "run-\ud800", scope);
        if (addressIdentity)
            admission = admission with { Invocation = admission.Invocation with
            {
                ResultAddress = admission.Invocation.ResultAddress with { BookmarkId = "bookmark-\ud801" }
            } };
        await using (var context = database.CreateContext())
            Assert.Equal(EtlStoreStatus.Applied, (await database.CreateStore(context).AdmitAsync(admission)).Status);
        await using (var context = database.CreateContext())
        {
            var store = database.CreateStore(context);
            var reopened = Assert.IsType<EtlRunView>(await store.FindRunAsync(scope, admission.Invocation.RunId));
            Assert.Equal(admission.Invocation.RunId, reopened.Invocation.RunId);
            Assert.Equal(admission.Invocation.ResultAddress, reopened.Invocation.ResultAddress);
            Assert.Equal(EtlStoreStatus.Replay, (await store.AdmitAsync(admission)).Status);
        }
    }

    private static EtlRunAdmission CreateAdmission(string runId, EtlScope scope, EtlPipeline? pipeline = null)
    {
        var template = EtlRuntimeFixtures.Admission(runId, scope);
        var source = pipeline ?? EtlTestPipelines.Create();
        var artifact = Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", 1, source).Artifact);
        return template with { Invocation = template.Invocation with { Artifact = artifact } };
    }

    private static async Task StartAsync(IEtlRuntimeStore store, EtlRunAdmission admission, long recordCount)
    {
        Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(admission)).Status);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(admission.Invocation.Scope, "prep-worker"));
        Assert.Equal(EtlStoreStatus.Applied, await store.CommitPreparationAsync(preparation, EtlRuntimeFixtures.Prepared(recordCount)));
    }

    private static EtlBatchDescriptor CreateBatch(EtlRunAdmission admission, EtlPreparedRun prepared, int partition,
        long start, long end, string marker)
    {
        var inputHash = EtlRuntimeRecordCodec.HashText(marker);
        const long inputBytes = 100;
        return new(EtlRuntimeIdentity.BatchId(admission, prepared, partition, start, end, inputBytes, inputHash),
            start, end, inputBytes, inputHash);
    }

    private static EtlBatchAcceptance CreateAcceptance(EtlRunAdmission admission, EtlBatchDescriptor batch,
        long accepted, long rejected, long filtered)
    {
        var effects = new List<EtlOutputEffect>();
        if (accepted > 0)
        {
            effects.Add(new(EtlOutputRoute.Main,
                new(EtlRuntimeIdentity.OutputKey(admission, batch.BatchId, EtlOutputRoute.Main), EtlRuntimeFixtures.Hash, 128), accepted));
        }
        if (rejected > 0)
        {
            effects.Add(new(EtlOutputRoute.Error,
                new(EtlRuntimeIdentity.OutputKey(admission, batch.BatchId, EtlOutputRoute.Error), new string('b', 64), 64), rejected));
        }
        return new(batch, accepted, rejected, filtered, effects);
    }

    private static HostFileBinding Input(EtlScope scope, string alias, EtlFileRole role, string path) =>
        new(scope, alias, role, "v1", path, true,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), ImmutableInput: true);

    private static HostFileBinding Output(EtlScope scope, string alias, EtlFileRole role, string path) =>
        new(scope, alias, role, "v1", path, true, AtomicPublication: true);

    private static EtlBindingRevision Binding(PinnedFileBinding pin) =>
        new(pin.Alias, pin.Revision, pin.Handle, pin.ExpectedSha256);

    private static EtlObjectReference Reference(EtlChunk chunk) =>
        new(chunk.Key, chunk.ContentHash, chunk.ByteLength);

    private sealed class TestDatabase(string directory) : IAsyncDisposable
    {
        private readonly string _connectionString = $"Data Source={Path.Combine(directory, "etl-runtime.db")};Pooling=False";

        public static async Task<TestDatabase> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new TestDatabase(directory);
            await using var context = database.CreateContext();
            await EfDatabaseMigrator.ApplyAsync(context, EtlRuntimeSqliteDbContext.ExpectedProviderName);
            return database;
        }

        public EtlRuntimeSqliteDbContext CreateContext(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<EtlRuntimeSqliteDbContext>()
            .UseSqlite(_connectionString, sqlite => sqlite
                .MigrationsAssembly(typeof(EtlRuntimeSqliteDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(EtlRuntimeEfModule.HistoryTableName))
            .AddInterceptors(interceptors)
            .Options);

        public EfEtlRuntimeStore CreateStore(EtlRuntimeSqliteDbContext context, TimeProvider? clock = null,
            TimeSpan? preparationLease = null, TimeSpan? partitionLease = null) => new(context, clock ?? TimeProvider.System,
            new EtlRuntimeStoreOptions(preparationLease ?? TimeSpan.FromSeconds(30), partitionLease ?? TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30), MaximumPartitionsPerRun: 128, MaximumPageSize: 100));

        public ValueTask DisposeAsync()
        {
            Directory.Delete(directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-20T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class TableReadBarrier(string tableName) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _intercepted;

        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(tableName, StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref _intercepted, 1) == 0)
            {
                _entered.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class TransactionCommitObserver : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _commitStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CommitStarted => _commitStarted.Task.IsCompleted;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            _commitStarted.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
