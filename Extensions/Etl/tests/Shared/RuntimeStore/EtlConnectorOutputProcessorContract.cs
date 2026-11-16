using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Transformation;
using Elsa.Etl.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public abstract partial class EtlRuntimeStoreContract
{
    [SkippableTheory]
    [InlineData(204, false)]
    [InlineData(202, false)]
    [InlineData(204, true)]
    public async Task Output_processor_retains_transformed_bodies_and_records_only_verified_route_delivery(
        int responseStatus, bool corruptRetainedBody)
    {
        const string expectedHttpBody = "[{\"id\":1}]";
        const string expectedErrorBody = "{\"recordOrdinal\":2,\"code\":\"etl.invalid-value\"}\n";
        var exactOutputLimit = Encoding.UTF8.GetByteCount(expectedHttpBody) + Encoding.UTF8.GetByteCount(expectedErrorBody);
        var expectedReady = (responseStatus is 200 or 201 or 204) && !corruptRetainedBody;
        var root = Path.Combine(Path.GetTempPath(), "elsa-etl-output-processor-" + Guid.NewGuid().ToString("N"));
        var snapshotRoot = Path.Combine(root, "snapshots");
        var errorRoot = Path.Combine(root, "errors");
        Directory.CreateDirectory(root);
        try
        {
            await using var endpoint = await EtlHttpLoopbackServer.StartAsync((request, _) =>
                ValueTask.FromResult(new EtlHttpLoopbackResponse(request.Method == "POST" ? responseStatus : 500, "")));
            var scope = new EtlScope("tenant-output", "processor");
            var httpOptions = new EtlHostHttpBindingOptions
            {
                TenantId = scope.TenantId,
                PersistencePartition = scope.PersistencePartition,
                Alias = "output",
                Role = "MainOutput",
                Revision = "destination-revision-1",
                Endpoint = endpoint.CreateUri("/output").AbsoluteUri,
                AllowedPaths = ["/output"],
                AllowHttp = true
            };
            var httpBindings = new HostHttpBindings([httpOptions]);
            var fileBinding = new HostFileBinding(scope, "errors", EtlFileRole.ErrorOutput,
                "destination-revision-1", errorRoot, IsCurrent: true, AtomicPublication: true);
            var fileBindings = new HostFileBindings([fileBinding]);
            var filePin = fileBindings.Resolve(scope, "errors", EtlFileRole.ErrorOutput);
            await using var httpPin = await httpBindings.ResolveCurrentAsync(scope, "output", "MainOutput");

            var defaultPipeline = CsvMixedRoutePipeline();
            var pipeline = defaultPipeline with
            {
                Limits = defaultPipeline.Limits with
                {
                    FilePartitionCount = 1,
                    MaxPartitions = 1,
                    MaxBatchRecords = 2,
                    MaxOutputBatchBytes = exactOutputLimit
                }
            };
            var admission = ConnectorAdmission("connector-output-processor", scope, pipeline);
            admission = admission with
            {
                Bindings = admission.Bindings with
                {
                    Main = new(EtlConnectorKind.Http,
                        new("output", httpPin.Revision, httpPin.Handle, null)),
                    Errors = new(EtlConnectorKind.Jsonl,
                        new("errors", filePin.Revision, filePin.Handle, filePin.ExpectedSha256))
                }
            };

            await using var database = await CreateDatabaseAsync();
            await using var context = database.CreateContext();
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
            var lease = Assert.Single(await StartOutputCsvRunAsync(store, admission, recordCount: 2));
            var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(scope, admission.Invocation.RunId));
            var batch = OutputTestBatch(admission, run.Prepared!, lease, recordCount: 2);
            var reserved = await store.ReserveConnectorBatchAsync(lease, batch);
            Assert.Equal(EtlStoreStatus.Applied, reserved.Status);
            lease = lease with { OwedBatch = reserved.Reservation };
            var input = new EtlConnectorInputBatch(lease.PartitionOrdinal, batch,
            [
                new(1, 5, new EtlTypedRow(1, new Dictionary<string, EtlTypedValue>
                    { ["id"] = new(EtlColumnType.Int64, 1L) }), null),
                new(2, 6, null, new EtlRowError(2, null, "etl.invalid-value"))
            ]);
            using var transport = new EtlHttpTransport(TimeProvider.System);
            var processor = new EtlConnectorOutputProcessor(store, fileBindings, httpBindings,
                new TestChunkPublisherFactory(), transport, snapshotRoot);

            var prepared = await processor.TransformAndRetainAsync(lease, admission, pipeline, input);

            Assert.True(prepared.Succeeded, prepared.FailureCode);
            var output = Assert.IsType<EtlConnectorBatchOutput>(prepared.Output);
            Assert.Equal((1L, 1L, 0L), (output.AcceptedRecords, output.RejectedRecords, output.FilteredRecords));
            Assert.Equal(EtlConnectorKind.Http, output.Main!.Kind);
            Assert.Equal(EtlConnectorKind.Jsonl, output.Errors!.Kind);
            Assert.Empty(endpoint.Requests);
            Assert.NotNull((await context.ConnectorBatches.AsNoTracking().SingleAsync()).OutputJson);
            var retainedHttp = await new FileChunkStore(".json").ReadAsync(snapshotRoot,
                "etl-connector-http-route-body-v1:" + output.Main.EffectId, pipeline.Limits.MaxOutputBatchBytes,
                output.Main.Body.ByteLength);
            var retainedBytes = Assert.IsType<byte[]>(retainedHttp);
            Assert.Equal(expectedHttpBody, Encoding.UTF8.GetString(retainedBytes));
            if (corruptRetainedBody)
            {
                var corrupted = retainedBytes.ToArray();
                corrupted[0] = (byte)' ';
                await File.WriteAllBytesAsync(Path.Combine(snapshotRoot, output.Main.Body.Key), corrupted);
            }

            var withoutRetainedPlan = await processor.DispatchAsync(lease, admission, pipeline, output);
            Assert.Equal("etl.connector-output-conflict", withoutRetainedPlan.FailureCode);
            Assert.Empty(endpoint.Requests);
            lease = lease with { OwedBatch = lease.OwedBatch! with { Output = output } };
            var dispatch = await processor.DispatchAsync(lease, admission, pipeline, output);

            var expectedFailure = corruptRetainedBody ? "etl.output-read-conflict" : responseStatus == 202 ? "etl.http.delivery-rejected" : null;
            Assert.Equal(expectedReady ? EtlStoreStatus.Applied : EtlStoreStatus.Invalid, dispatch.Status);
            Assert.Equal(expectedReady, dispatch.ReadyToAccept);
            Assert.False(dispatch.Yield);
            Assert.Equal(expectedFailure, dispatch.FailureCode);
            if (corruptRetainedBody)
                Assert.Empty(endpoint.Requests);
            else
            {
                var request = Assert.Single(endpoint.Requests);
                Assert.Equal("POST", request.Method);
                Assert.Equal("/output", request.RawTarget);
                Assert.Equal(expectedHttpBody, Encoding.UTF8.GetString(request.Body));
                Assert.Equal(output.Main.EffectId, request.Headers["Idempotency-Key"]);
            }
            if (expectedReady)
            {
                var publishedError = await File.ReadAllBytesAsync(Path.Combine(errorRoot,
                    EtlRuntimeIdentity.ConnectorFileOutputKey(output.Errors.EffectId)));
                Assert.Equal(expectedErrorBody, Encoding.UTF8.GetString(publishedError));
            }

            var batchRow = await context.ConnectorBatches.AsNoTracking().SingleAsync();
            if (corruptRetainedBody)
                Assert.Null(batchRow.HttpDeliveryJson);
            else
            {
                var delivery = Assert.IsType<EtlConnectorHttpDelivery>(Assert.IsType<EtlConnectorHttpDeliveries>(
                    EtlConnectorRecordCodec.Read<EtlConnectorHttpDeliveries>(batchRow.HttpDeliveryJson!)).Main);
                Assert.Equal(expectedReady ? EtlHttpDeliveryPhase.Acknowledged : EtlHttpDeliveryPhase.Failed, delivery.Phase);
            }
            Assert.Equal(expectedReady, batchRow.ErrorFilePublished);
            Assert.False(batchRow.Settled);
            var afterDispatch = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(scope, admission.Invocation.RunId));
            Assert.Equal(0, afterDispatch.Run.AcceptedRecords);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [SkippableFact]
    public async Task A_recorded_rabbitmq_route_is_not_published_again_when_its_batch_is_dispatched_again()
    {
        var root = Path.Combine(Path.GetTempPath(), "elsa-etl-output-rabbitmq-" + Guid.NewGuid().ToString("N"));
        var snapshotRoot = Path.Combine(root, "snapshots");
        var errorRoot = Path.Combine(root, "errors");
        Directory.CreateDirectory(root);
        try
        {
            var scope = new EtlScope("tenant-output", "rabbitmq");
            var publisher = new TestRabbitMqPublisher();
            var rabbitMqBindings = new HostRabbitMqBindings([new EtlHostRabbitMqBindingOptions
            {
                TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition, Alias = "events", Revision = "v1",
                ConnectionString = "amqp://etl:secret@broker/etl", AllowedExchanges = ["etl.people"]
            }], [publisher]);
            var fileBinding = new HostFileBinding(scope, "errors", EtlFileRole.ErrorOutput,
                "destination-revision-1", errorRoot, IsCurrent: true, AtomicPublication: true);
            var fileBindings = new HostFileBindings([fileBinding]);
            var filePin = fileBindings.Resolve(scope, "errors", EtlFileRole.ErrorOutput);
            var exchange = rabbitMqBindings.ResolveCurrent(scope, "events", "MainOutput", "etl.people");

            var defaultPipeline = CsvMixedRoutePipeline();
            var pipeline = defaultPipeline with
            {
                Routes = new(new EtlRabbitMqDestination("events", "etl.people", "people.loaded"), new EtlJsonlDestination("errors")),
                Limits = defaultPipeline.Limits with { FilePartitionCount = 1, MaxPartitions = 1, MaxBatchRecords = 2 }
            };
            var admission = ConnectorAdmission("connector-output-rabbitmq", scope, pipeline);
            admission = admission with
            {
                Bindings = admission.Bindings with
                {
                    Main = new(EtlConnectorKind.RabbitMq, new("events", exchange.Revision, exchange.Handle, null)),
                    Errors = new(EtlConnectorKind.Jsonl, new("errors", filePin.Revision, filePin.Handle, filePin.ExpectedSha256))
                }
            };

            await using var database = await CreateDatabaseAsync();
            await using var context = database.CreateContext();
            var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
            var lease = Assert.Single(await StartOutputCsvRunAsync(store, admission, recordCount: 2));
            var run = Assert.IsType<EtlConnectorRunView>(await store.FindConnectorRunAsync(scope, admission.Invocation.RunId));
            var batch = OutputTestBatch(admission, run.Prepared!, lease, recordCount: 2);
            var reserved = await store.ReserveConnectorBatchAsync(lease, batch);
            lease = lease with { OwedBatch = reserved.Reservation };
            var input = new EtlConnectorInputBatch(lease.PartitionOrdinal, batch,
            [
                new(1, 5, new EtlTypedRow(1, new Dictionary<string, EtlTypedValue> { ["id"] = new(EtlColumnType.Int64, 1L) }), null),
                new(2, 6, null, new EtlRowError(2, null, "etl.invalid-value"))
            ]);
            using var transport = new EtlHttpTransport(TimeProvider.System);
            var processor = new EtlConnectorOutputProcessor(store, fileBindings, new HostHttpBindings([]),
                new TestChunkPublisherFactory(), transport, snapshotRoot, rabbitMqBindings: rabbitMqBindings);
            var output = Assert.IsType<EtlConnectorBatchOutput>((await processor.TransformAndRetainAsync(lease, admission, pipeline, input)).Output);
            lease = lease with { OwedBatch = lease.OwedBatch! with { Output = output } };

            var first = await processor.DispatchAsync(lease, admission, pipeline, output);
            // The batch is dispatched again, as after an error route's retry, carrying the publications the store recorded.
            var recorded = await context.ConnectorBatches.AsNoTracking().SingleAsync();
            lease = lease with { OwedBatch = lease.OwedBatch! with { FilePublications = new(recorded.MainFilePublished, recorded.ErrorFilePublished) } };
            var again = await processor.DispatchAsync(lease, admission, pipeline, output);

            Assert.Equal((EtlStoreStatus.Applied, true), (first.Status, first.ReadyToAccept));
            Assert.True(recorded.MainFilePublished);
            // A RabbitMQ route is persisted as a recorded publication, effect mechanism 2, like a file or SQL route (FR-007).
            Assert.Equal(2, recorded.MainEffectKind);
            Assert.Equal((true, false, (string?)null), (again.ReadyToAccept, again.Yield, again.FailureCode));
            Assert.Equal(1, publisher.Attempts);
            Assert.Single(publisher.Deliveries);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private protected sealed class TestChunkPublisherFactory : IFileChunkPublisherFactory
    {
        public IFileChunkPublisher Create(EtlFileChunkFormat format) => new FileChunkStore(format switch
        {
            EtlFileChunkFormat.Jsonl or EtlFileChunkFormat.JsonlSnapshot => ".jsonl",
            EtlFileChunkFormat.Csv or EtlFileChunkFormat.CsvOutput => ".csv",
            EtlFileChunkFormat.Index => ".index",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        });
    }
}
