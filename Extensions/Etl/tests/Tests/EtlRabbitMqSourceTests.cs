using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Preparation;
using Elsa.Etl.Tests.Support;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1201: authoring rules, bindings, the snapshot writer and the preparer of the RabbitMQ source.</summary>
public sealed class EtlRabbitMqSourceTests : IDisposable
{
    private static readonly EtlScope Scope = new("tenant", "rabbitmq-source");
    private static readonly EtlJsonField[] Fields =
        [new("id", "/id", EtlColumnType.Int64, true), new("name", "/name", EtlColumnType.String, true)];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "etl-rabbitmq-source-" + Guid.NewGuid().ToString("N"));

    private string SnapshotRoot => Path.Combine(_root, "snapshots");

    [Fact]
    public void A_rabbitmq_source_compiles_round_trips_and_leaves_existing_artifacts_unchanged()
    {
        var artifact = Assert.IsType<EtlArtifact>(Compile(Pipeline()).Artifact);

        var read = Assert.IsType<EtlRabbitMqSource>(EtlArtifactV2Json.Read(artifact.Pipeline).Source);
        Assert.Equal(("events-in", "people", 10), (read.Binding, read.Queue, read.MaxMessages));
        Assert.Equal(Fields, read.Fields);
        Assert.Contains("\"kind\":\"rabbitmq\"", artifact.Pipeline.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("rabbitmq", Compile(EtlV2TestPipelines.Create()).Artifact!.Pipeline.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", 10, "source.rabbitmq-name-invalid")]
    [InlineData("bad queue", 10, "source.rabbitmq-name-invalid")]
    [InlineData("people", 0, "source.rabbitmq-max-messages-invalid")]
    [InlineData("people", 100_001, "source.rabbitmq-max-messages-invalid")]
    public void A_queue_or_maximum_the_broker_or_runtime_cannot_take_is_refused(string queue, int maxMessages, string code) =>
        AssertRejected(Pipeline(new EtlRabbitMqSource("events-in", queue, Fields, maxMessages)), code);

    [Fact]
    public void A_queue_name_longer_than_a_protocol_short_string_is_refused()
    {
        AssertRejected(Pipeline(new EtlRabbitMqSource("events-in", new string('q', 256), Fields, 10)), "source.rabbitmq-name-invalid");
        Assert.NotNull(Compile(Pipeline(new EtlRabbitMqSource("events-in", new string('q', 255), Fields, 100_000))).Artifact);
    }

    [Fact]
    public void A_source_binding_allows_its_queues_exactly_and_needs_the_consumer()
    {
        var bindings = Bindings(new TestRabbitMqConsumer());

        var resolved = bindings.ResolveCurrent(Scope, "events-in", "Source", "people");

        Assert.NotNull(resolved.Consumer);
        Assert.Equal("etl.rabbitmq-queue-not-allowed",
            Assert.Throws<EtlBindingException>(() => bindings.ResolveCurrent(Scope, "events-in", "Source", "orders")).Code);
        Assert.Equal("etl.rabbitmq-queue-not-allowed",
            Assert.Throws<EtlBindingException>(() => bindings.ResolveCurrent(Scope, "events-in", "Source", "People")).Code);
        Assert.Equal("etl.rabbitmq-unavailable", Assert.Throws<EtlBindingException>(() =>
            new HostRabbitMqBindings([SourceBinding()], [new TestRabbitMqPublisher()]).ResolveCurrent(Scope, "events-in", "Source", "people")).Code);
        // The allowed queues are part of the pinned handle.
        var wider = SourceBinding();
        wider.AllowedQueues = ["people", "orders"];
        Assert.NotEqual(resolved.Handle, new HostRabbitMqBindings([wider], [new TestRabbitMqPublisher()], [new TestRabbitMqConsumer()])
            .ResolveCurrent(Scope, "events-in", "Source", "people").Handle);
    }

    [Fact]
    public void A_destination_binding_keeps_its_spec_1200_handle_whatever_queues_it_lists()
    {
        var destination = new EtlHostRabbitMqBindingOptions
        {
            TenantId = Scope.TenantId, PersistencePartition = Scope.PersistencePartition, Alias = "events-out", Role = "MainOutput",
            Revision = "v1", ConnectionString = "amqp://etl:secret@broker:5672/sales", AllowedExchanges = ["etl.orders"],
            AllowedQueues = ["people"]
        };
        var target = new TestRabbitMqPublisher().DescribeTarget(destination.ConnectionString);
        // The identity spec 1200 pinned, so runs admitted before this source existed still resolve their destination.
        var spec1200 = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            FormatVersion = 1, ScopeIdentity = EtlRuntimeIdentity.ScopeKey(Scope), destination.Alias, destination.Role,
            destination.Revision, target.Host, target.Port, target.VirtualHost, target.User, AllowedExchanges = new[] { "etl.orders" }
        })));

        var resolved = new HostRabbitMqBindings([destination], [new TestRabbitMqPublisher()])
            .ResolveCurrent(Scope, "events-out", "MainOutput", "etl.orders");

        Assert.Equal(spec1200, resolved.Handle);
    }

    [Theory]
    [InlineData("{\"id\":1}", "{\"id\":1}")]
    [InlineData("{\n  \"id\": 1,\r\n  \"name\": \"Ada\"\n}", "{   \"id\": 1,    \"name\": \"Ada\" }")]
    [InlineData("\uFEFF{\"id\":1}", "{\"id\":1}")]
    [InlineData("\uFEFF", "\u0001")]
    [InlineData("\uFEFF\uFEFF", "\u0001")]
    [InlineData("\uFEFF\uFEFF{\"id\":1}", "\u0001")]
    [InlineData("not json", "not json")]
    [InlineData("not\njson", "\u0001")]
    [InlineData("", "\u0001")]
    [InlineData("  ", "\u0001")]
    [InlineData("\t", "\u0001")]
    [InlineData("\r", "\u0001")]
    public void Each_message_body_becomes_exactly_one_snapshot_line(string body, string line) =>
        Assert.Equal(line, Encoding.UTF8.GetString(EtlRabbitMqSnapshotWriter.Line(Encoding.UTF8.GetBytes(body)).Span));

    [Fact]
    public async Task Preparation_writes_the_received_messages_and_hands_their_receipt_over_unacknowledged()
    {
        var consumer = new TestRabbitMqConsumer("{\"id\":1,\"name\":\"Ada\"}", "{\"id\":2,\"name\":\"Bram\"}");

        var prepared = await Preparer(consumer).PrepareAsync(Admission(Pipeline()));

        var receipt = Assert.IsAssignableFrom<IEtlRabbitMqReceipt>(prepared.Receipt);
        Assert.Equal(2L, prepared.Prepared!.File!.RecordCount);
        Assert.Equal(["receive 2"], consumer.Log);
        Assert.Equal("{\"id\":1,\"name\":\"Ada\"}\n{\"id\":2,\"name\":\"Bram\"}\n",
            await File.ReadAllTextAsync(Path.Combine(SnapshotRoot, prepared.Prepared.File.Source.Key)));
        await receipt.DisposeAsync();
        Assert.Equal(2, consumer.Ready.Count);
    }

    [Fact]
    public async Task A_retried_preparation_never_reuses_a_predecessor_snapshot()
    {
        var consumer = new TestRabbitMqConsumer("{\"id\":1,\"name\":\"Ada\"}");
        var admission = Admission(Pipeline());

        var first = await Preparer(consumer).PrepareAsync(admission);
        await first.Receipt!.DisposeAsync();
        var second = await Preparer(consumer).PrepareAsync(admission);

        // The predecessor's messages were requeued, so the retry receives them again into a snapshot of its own.
        Assert.NotEqual(first.Prepared!.File!.Source.Key, second.Prepared!.File!.Source.Key);
        Assert.Equal(["receive 1", "requeue 1", "receive 1"], consumer.Log);
    }

    [Fact]
    public async Task A_failed_or_transient_receive_prepares_nothing_and_a_too_large_message_is_left_queued()
    {
        var transient = await Preparer(new TestRabbitMqConsumer().Then(new IOException("connection reset"))).PrepareAsync(Admission(Pipeline()));
        var rejected = await Preparer(new TestRabbitMqConsumer().Then(new InvalidOperationException("ACCESS_REFUSED"))).PrepareAsync(Admission(Pipeline()));
        var large = new TestRabbitMqConsumer("{\"id\":1,\"name\":\"" + new string('x', 200) + "\"}");
        var tooLarge = await Preparer(large).PrepareAsync(Admission(Pipeline(limits: limits => limits with { MaxSnapshotBytes = 64, MaxLookupBytes = 64 })));

        Assert.True(transient.Transient);
        Assert.Equal("etl.rabbitmq-read-rejected", rejected.Diagnostic?.Code);
        Assert.Equal("etl.snapshot-too-large", tooLarge.Diagnostic?.Code);
        Assert.Null(tooLarge.Receipt);
        Assert.Single(large.Ready);
        Assert.Equal(["receive 0", "requeue 0"], large.Log);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private EtlConnectorPreparer Preparer(TestRabbitMqConsumer consumer) =>
        new(new HostFileBindings([]), SnapshotRoot) { RabbitMqBindings = Bindings(consumer) };

    private static HostRabbitMqBindings Bindings(TestRabbitMqConsumer consumer) =>
        new([SourceBinding()], [new TestRabbitMqPublisher()], [consumer]);

    private static EtlHostRabbitMqBindingOptions SourceBinding() => new()
    {
        TenantId = Scope.TenantId, PersistencePartition = Scope.PersistencePartition, Alias = "events-in", Role = "Source",
        Revision = "v1", ConnectionString = "amqp://etl:secret@broker/etl", AllowedQueues = ["people"]
    };

    private static EtlConnectorRunAdmission Admission(EtlPipelineV2 pipeline)
    {
        var artifact = Compile(pipeline).Artifact ?? throw new InvalidOperationException("The test pipeline does not compile.");
        var invocation = EtlRuntimeFixtures.Admission("rabbitmq-source-" + Guid.NewGuid().ToString("N"), Scope).Invocation with { Artifact = artifact };
        var resolved = Bindings(new TestRabbitMqConsumer()).ResolveCurrent(Scope, "events-in", "Source", "people");
        return new EtlConnectorRunAdmission(invocation, new EtlConnectorRunBindings(
            new EtlConnectorBindingRevision(EtlConnectorKind.RabbitMq, new EtlBindingRevision(resolved.Alias, resolved.Revision, resolved.Handle, null)),
            null,
            new EtlConnectorBindingRevision(EtlConnectorKind.Jsonl, new EtlBindingRevision("output", "v1", "output-handle", null)),
            new EtlConnectorBindingRevision(EtlConnectorKind.Jsonl, new EtlBindingRevision("errors", "v1", "errors-handle", null)), []));
    }

    internal static EtlPipelineV2 Pipeline(EtlRabbitMqSource? source = null, Func<EtlConnectorLimits, EtlConnectorLimits>? limits = null)
    {
        var baseline = EtlV2TestPipelines.Create();
        var declared = source ?? new EtlRabbitMqSource("events-in", "people", Fields, 10);
        var connectorLimits = baseline.Limits with { FilePartitionCount = 1, MaxBatchRecords = 10 };
        return baseline with
        {
            Source = declared,
            Lookup = null,
            Routes = new(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")),
            Mapping = [new("id", EtlFieldOrigin.Source, "id"), new("name", EtlFieldOrigin.Source, "name")],
            Filters = [],
            Limits = limits is null ? connectorLimits : limits(connectorLimits)
        };
    }

    private static EtlCompilationResult Compile(EtlPipelineV2 pipeline) => new EtlArtifactCompiler().Compile("orders", 1, pipeline);

    private static void AssertRejected(EtlPipelineV2 pipeline, string code)
    {
        var result = Compile(pipeline);
        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, item => item.Code == code);
    }
}
