using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1200: RabbitMQ delivery with publisher confirms through the real worker and store, on a test publisher.</summary>
public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task A_csv_pipeline_publishes_each_accepted_record_as_one_confirmed_message()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var publisher = new TestRabbitMqPublisher();
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlRabbitMqPublisher>(publisher),
            configureRuntime: runtime => runtime.RabbitMqBindings.Add(RabbitMqBinding(files.Scope)));

        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-first", RabbitMqPipeline()));

        Assert.Equal((EtlRunStatus.BusinessError, 2L), (run.Status, run.AcceptedRecords));
        Assert.Equal(["""{"id":1,"name":"Ada","category":"one"}""", """{"id":3,"name":"Bram","category":"three"}"""],
            publisher.Deliveries.Select(message => message.Body).Order(StringComparer.Ordinal));
        Assert.All(publisher.Deliveries, message =>
        {
            Assert.Equal(("etl.people", "people.loaded"), (message.Exchange, message.RoutingKey));
            Assert.Equal("rabbitmq-first", message.Headers["elsa-etl-run-id"]);
            Assert.Equal(message.Headers["elsa-etl-effect-id"] + ":0", message.MessageId);
        });
        // The record whose lookup misses is rejected to the JSON Lines error route.
        Assert.Single(Directory.EnumerateFiles(files.ErrorRoot, "*.jsonl").SelectMany(File.ReadAllLines).Where(line => line.Length > 0));
    }

    [Fact]
    public async Task A_broker_that_fails_or_refuses_a_message_pauses_the_run_until_every_message_is_confirmed()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        // The first attempt loses its connection, and the second has its message negatively acknowledged after the broker took
        // the batch's first message; the third is confirmed.
        var publisher = new TestRabbitMqPublisher().Then(new IOException("connection reset")).Then(EtlRabbitMqPublishOutcome.Nacked);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlRabbitMqPublisher>(publisher),
            configureRuntime: runtime => runtime.RabbitMqBindings.Add(RabbitMqBinding(files.Scope)));

        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-retried", RabbitMqPipeline()));

        Assert.Equal((EtlRunStatus.BusinessError, 2L), (run.Status, run.AcceptedRecords));
        Assert.Equal(4, publisher.Attempts);
        // The negatively acknowledged batch was published again in full, with the same message id: at least once.
        var ids = publisher.Deliveries.Select(message => message.MessageId).ToArray();
        Assert.Equal(3, ids.Length);
        Assert.Equal([1, 2], ids.GroupBy(id => id).Select(group => group.Count()).Order());
    }

    [Fact]
    public async Task An_unroutable_message_fails_the_run_without_recording_the_route()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var publisher = new TestRabbitMqPublisher().Then(EtlRabbitMqPublishOutcome.Unroutable);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlRabbitMqPublisher>(publisher),
            configureRuntime: runtime => runtime.RabbitMqBindings.Add(RabbitMqBinding(files.Scope)));

        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-unroutable", RabbitMqPipeline()));

        Assert.Equal("etl.rabbitmq-unroutable", run.TerminalResult?.Diagnostic?.Code);
        Assert.True(run.AcceptedRecords < 2);
    }

    [Fact]
    public async Task A_rabbitmq_destination_without_its_package_is_refused_at_admission()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureRuntime: runtime => runtime.RabbitMqBindings.Add(RabbitMqBinding(files.Scope)));

        var run = await AdmitAndWaitAsync(files, host, ConnectorInvocation(files.Scope, "rabbitmq-unavailable", RabbitMqPipeline()),
            EtlStoreStatus.Replay);

        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.Equal("etl.rabbitmq-unavailable", run.TerminalResult?.Diagnostic?.Code);
    }

    [Fact]
    public async Task A_worker_killed_after_the_broker_confirmed_a_batch_is_replaced_and_the_batch_is_published_again_with_the_same_ids()
    {
        await using var files = new RuntimeFiles(CsvJourneySource);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        EtlHostRabbitMqBindingOptions[] rabbitMq = [RabbitMqBinding(files.Scope)];
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate,
            configureServices: services => services.AddSingleton<IEtlRabbitMqPublisher>(new TestRabbitMqPublisher()),
            configureRuntime: runtime => runtime.RabbitMqBindings.AddRange(rabbitMq));
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(deadline.Token);
        var invocation = ConnectorInvocation(files.Scope, "rabbitmq-process", RabbitMqPipeline());
        await files.AdmitAsync(host, invocation);
        var journal = EtlWorkerProtocol.RabbitMqJournalPath(Path.GetDirectoryName(files.SnapshotRoot)!);

        // The broker confirms the predecessor's first batch, and the predecessor is killed before the runtime records it.
        var predecessor = await files.StartWorkerAsync("rabbitmq-predecessor", deadline.Token,
            connectorGate: EtlConnectorWorkerGate.BeforePublicationRecord, rabbitMqBindings: rabbitMq);
        Assert.Equal(EtlConnectorWorkerGate.BeforePublicationRecord, (await predecessor.WaitForConnectorCheckpointAsync(deadline.Token)).Gate);
        var confirmed = Assert.Single(TestRabbitMqPublisher.ReadJournal(journal));
        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);

        _ = await files.StartWorkerAsync("rabbitmq-survivor", deadline.Token, rabbitMqBindings: rabbitMq);
        var run = await files.WaitForTerminalAsync(host.Services, invocation.RunId);

        Assert.Equal((EtlRunStatus.BusinessError, 2L), (run.Status, run.AcceptedRecords));
        var deliveries = TestRabbitMqPublisher.ReadJournal(journal);
        // Two batches, the confirmed one published twice with the same id, headers and body.
        Assert.Equal(3, deliveries.Count);
        Assert.Equal(2, deliveries.Select(message => message.MessageId).Distinct().Count());
        var replayed = Assert.Single(deliveries.Skip(1), message => message.MessageId == confirmed.MessageId);
        Assert.Equal((confirmed.Body, confirmed.Headers.Count), (replayed.Body, replayed.Headers.Count));
        Assert.All(confirmed.Headers, header => Assert.Equal(header.Value, replayed.Headers[header.Key]));
    }

    private static EtlPipelineV2 RabbitMqPipeline() => CsvRoutePipeline("csv", csvErrors: false) with
    {
        Routes = new(new EtlRabbitMqDestination("events", "etl.people", "people.loaded"), new EtlJsonlDestination("errors")),
        Mapping = [new("id", EtlFieldOrigin.Source, "id"), new("name", EtlFieldOrigin.Source, "name"), new("category", EtlFieldOrigin.Lookup, "category")]
    };

    private static EtlHostRabbitMqBindingOptions RabbitMqBinding(EtlScope scope) => new()
    {
        TenantId = scope.TenantId, PersistencePartition = scope.PersistencePartition, Alias = "events", Role = "MainOutput",
        Revision = "v1", ConnectionString = "amqp://etl:secret@broker:5672/etl", AllowedExchanges = ["etl.people"]
    };
}
