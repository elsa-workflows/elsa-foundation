using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1201: RabbitMQ ingestion with manual acknowledgements through the real worker and store, on a test consumer.</summary>
public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task A_queue_is_drained_into_records_and_acknowledged_only_after_the_prepared_run_is_recorded()
    {
        await using var files = new RuntimeFiles("");
        var consumer = new TestRabbitMqConsumer("""{"id":1,"name":"Ada"}""", """{"id":2,"name":"Bram"}""", "not json",
            """{"id":3,"name":"Cleo"}""", "[4]");
        var host = await StartRabbitMqSourceHostAsync(files, consumer);

        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-source", EtlRabbitMqSourceTests.Pipeline()));

        // The body that is not JSON and the one that is not an object are each one record routed to errors; the others are delivered.
        Assert.Equal((EtlRunStatus.BusinessError, 3L), (run.Status, run.AcceptedRecords));
        Assert.Equal(["Ada", "Bram", "Cleo"], ReadJsonlNames(files.OutputRoot));
        Assert.Equal(["etl.jsonl.record-invalid", "etl.jsonl.record-not-object"], Directory.EnumerateFiles(files.ErrorRoot, "*.jsonl")
            .SelectMany(File.ReadAllLines).Where(line => line.Length > 0)
            .Select(line => System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("code").GetString()!).Order().ToArray());
        Assert.Equal(["receive 5", "record Applied", "ack 5"], consumer.Log);
        Assert.Empty(consumer.Ready);
    }

    [Fact]
    public async Task A_run_takes_at_most_its_maximum_and_leaves_the_rest_queued_and_an_empty_queue_succeeds()
    {
        await using var files = new RuntimeFiles("");
        var consumer = new TestRabbitMqConsumer("""{"id":1,"name":"Ada"}""", """{"id":2,"name":"Bram"}""", """{"id":3,"name":"Cleo"}""");
        var host = await StartRabbitMqSourceHostAsync(files, consumer);
        var bounded = EtlRabbitMqSourceTests.Pipeline(new EtlRabbitMqSource("events-in", "people",
            [new("id", "/id", EtlColumnType.Int64, true), new("name", "/name", EtlColumnType.String, true)], 2));

        var first = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-bounded", bounded));
        var second = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-rest", bounded));
        var third = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-empty", bounded));

        Assert.Equal((EtlRunStatus.Succeeded, 2L), (first.Status, first.AcceptedRecords));
        Assert.Equal((EtlRunStatus.Succeeded, 1L), (second.Status, second.AcceptedRecords));
        Assert.Equal((EtlRunStatus.Succeeded, 0L), (third.Status, third.AcceptedRecords));
        Assert.Equal(["receive 2", "record Applied", "ack 2", "receive 1", "record Applied", "ack 1", "receive 0", "record Applied", "ack 0"],
            consumer.Log);
    }

    [Fact]
    public async Task A_drain_stops_within_the_retained_input_budget_so_a_backlog_drains_over_several_runs()
    {
        await using var files = new RuntimeFiles("");
        var consumer = new TestRabbitMqConsumer(new[] { "Ada", "Bob", "Cid", "Dee", "Eve" }
            .Select((name, index) => $$"""{"id":{{index + 1}},"name":"{{name}}"}""").ToArray());
        var host = await StartRabbitMqSourceHostAsync(files, consumer);
        var pipeline = EtlRabbitMqSourceTests.Pipeline();
        // Each 21-byte body is a 22-byte line plus a 24-byte index entry, beside the 24-byte sentinel: three fit in 200 bytes.
        var bounded = pipeline with { Http = pipeline.Http with { MaxRetainedInputBytes = 200, MaxPageBytes = 200, MaxContinuationBytes = 200 } };

        var first = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-retained", bounded));
        var second = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-retained-rest", bounded));

        Assert.Equal((EtlRunStatus.Succeeded, 3L), (first.Status, first.AcceptedRecords));
        Assert.Equal((EtlRunStatus.Succeeded, 2L), (second.Status, second.AcceptedRecords));
        Assert.Equal(["receive 3", "record Applied", "ack 3", "receive 2", "record Applied", "ack 2"], consumer.Log);
    }

    [Fact]
    public async Task A_prepared_run_the_store_refuses_to_record_never_acknowledges_its_messages()
    {
        await using var files = new RuntimeFiles("");
        var consumer = new TestRabbitMqConsumer("""{"id":1,"name":"Ada"}""", """{"id":2,"name":"Bram"}""");
        var host = await StartRabbitMqSourceHostAsync(files, consumer, refuseRecord: true);

        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-refused", EtlRabbitMqSourceTests.Pipeline()));

        Assert.Equal("etl.connector-preparation-refused", run.TerminalResult?.Diagnostic?.Code);
        Assert.Equal(["receive 2", "record Conflict", "requeue 2"], consumer.Log);
        Assert.Equal(2, consumer.Ready.Count);
    }

    [Fact]
    public async Task A_preparation_record_the_store_replays_still_acknowledges_the_messages()
    {
        await using var files = new RuntimeFiles("");
        var consumer = new TestRabbitMqConsumer("""{"id":1,"name":"Ada"}""");
        // The store already holds this record, as when a commit's answer was lost and the same attempt commits again.
        var host = await StartRabbitMqSourceHostAsync(files, consumer, replayRecord: true);

        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-replayed", EtlRabbitMqSourceTests.Pipeline()));

        Assert.Equal((EtlRunStatus.Succeeded, 1L), (run.Status, run.AcceptedRecords));
        Assert.Equal(["receive 1", "record Replay", "ack 1"], consumer.Log);
        Assert.Empty(consumer.Ready);
    }

    [Fact]
    public async Task A_failed_acknowledgement_does_not_fail_the_recorded_run_and_leaves_the_messages_to_the_broker()
    {
        await using var files = new RuntimeFiles("");
        var consumer = new TestRabbitMqConsumer("""{"id":1,"name":"Ada"}""") { AcknowledgementFailure = new IOException("connection reset") };
        var host = await StartRabbitMqSourceHostAsync(files, consumer);

        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-ack-lost", EtlRabbitMqSourceTests.Pipeline()));

        // The snapshot was recorded, so the run completes; the unacknowledged message goes back to the broker for a later run.
        Assert.Equal((EtlRunStatus.Succeeded, 1L), (run.Status, run.AcceptedRecords));
        Assert.Equal(["receive 1", "record Applied", "requeue 1"], consumer.Log);
        Assert.Single(consumer.Ready);
    }

    [Fact]
    public async Task The_acknowledgement_survives_the_preparation_heartbeat_stopping_once_the_run_is_recorded()
    {
        await using var files = new RuntimeFiles("");
        var consumer = new TestRabbitMqConsumer("""{"id":1,"name":"Ada"}""");
        // The record ends the preparation lease, so the heartbeat, renewing every second, cancels its token before the ack.
        var host = await StartRabbitMqSourceHostAsync(files, consumer, holdAfterRecord: TimeSpan.FromSeconds(2.5));

        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-heartbeat", EtlRabbitMqSourceTests.Pipeline()));
        // The run can complete while preparation still holds after the record; wait for preparation to settle its receipt.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (consumer.Log.Count < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(100);

        Assert.Equal((EtlRunStatus.Succeeded, 1L), (run.Status, run.AcceptedRecords));
        Assert.Equal(["receive 1", "record Applied", "ack 1"], consumer.Log);
        Assert.Empty(consumer.Ready);
    }

    [Fact]
    public async Task A_queue_drains_into_a_sql_destination_typed_by_the_source_fields()
    {
        await using var files = new RuntimeFiles("");
        var target = $"Data Source={Path.Combine(Path.GetDirectoryName(files.SnapshotRoot)!, "target.db")};Pooling=False";
        ExecuteSql(target, "CREATE TABLE people (id INTEGER NOT NULL, name TEXT NOT NULL, CONSTRAINT people_key UNIQUE (id))");
        var consumer = new TestRabbitMqConsumer("""{"id":1,"name":"Ada"}""", """{"id":2,"name":"Bram"}""");
        var host = await StartRabbitMqSourceHostAsync(files, consumer, sqlTarget: target);
        var pipeline = EtlRabbitMqSourceTests.Pipeline() with
        {
            Routes = new(new EtlSqlDestination("people-out", "people", ["id"]), new EtlJsonlDestination("errors"))
        };

        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-to-sql", pipeline));

        // The SQL route types its parameters from the source fields: id binds as an integer, name as text.
        Assert.Equal((EtlRunStatus.Succeeded, 2L), (run.Status, run.AcceptedRecords));
        Assert.Equal(["1|Ada|integer|text", "2|Bram|integer|text"],
            QuerySql(target, "SELECT id, name, typeof(id), typeof(name) FROM people ORDER BY id"));
        Assert.Equal(["receive 2", "record Applied", "ack 2"], consumer.Log);
    }

    private static async Task<Microsoft.Extensions.Hosting.IHost> StartRabbitMqSourceHostAsync(RuntimeFiles files,
        TestRabbitMqConsumer consumer, bool refuseRecord = false, TimeSpan holdAfterRecord = default, bool replayRecord = false,
        string? sqlTarget = null) =>
        await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services =>
            {
                services.AddSingleton<IEtlRabbitMqPublisher>(new TestRabbitMqPublisher());
                services.AddSingleton<IEtlRabbitMqConsumer>(consumer);
                services.Replace(ServiceDescriptor.Scoped<IEtlConnectorRuntimeStore>(provider =>
                    new PreparationRecordingStore(provider.GetRequiredService<IEtlRuntimeStore>(), consumer, refuseRecord, holdAfterRecord,
                        replayRecord)));
                if (sqlTarget is not null)
                    services.AddSingleton<IEtlSqlEngine>(new SqliteEtlSqlEngine());
            },
            configureRuntime: runtime =>
            {
                runtime.RabbitMqBindings.Add(new EtlHostRabbitMqBindingOptions
                {
                    TenantId = files.Scope.TenantId, PersistencePartition = files.Scope.PersistencePartition, Alias = "events-in",
                    Role = "Source", Revision = "v1", ConnectionString = "amqp://etl:secret@broker/etl", AllowedQueues = ["people"]
                });
                if (sqlTarget is not null)
                    runtime.SqlBindings.Add(new EtlHostSqlBindingOptions
                    {
                        TenantId = files.Scope.TenantId, PersistencePartition = files.Scope.PersistencePartition, Alias = "people-out",
                        Role = "MainOutput", Revision = "v1", Provider = "sqlite", ConnectionString = sqlTarget, AllowedTables = ["people"]
                    });
            });

    /// <summary>
    /// Notes the preparation record in the consumer's log, refuses it, or commits it twice so the store replays it, so tests
    /// see when acknowledgement happens.
    /// </summary>
    private sealed class PreparationRecordingStore(IEtlRuntimeStore runtime, TestRabbitMqConsumer consumer, bool refuse,
        TimeSpan holdAfterRecord, bool replay)
        : ForwardingEtlConnectorRuntimeStore(runtime, (IEtlConnectorRuntimeStore)runtime)
    {
        public override async ValueTask<EtlStoreStatus> CommitConnectorPreparationAsync(EtlPreparationLease lease,
            EtlConnectorPreparedRun prepared, CancellationToken cancellationToken = default)
        {
            var status = refuse ? EtlStoreStatus.Conflict : await Connector.CommitConnectorPreparationAsync(lease, prepared, cancellationToken);
            if (replay)
                status = await Connector.CommitConnectorPreparationAsync(lease, prepared, cancellationToken);
            consumer.Note("record " + status);
            if (holdAfterRecord > TimeSpan.Zero)
                await Task.Delay(holdAfterRecord, CancellationToken.None);
            return status;
        }
    }
}
