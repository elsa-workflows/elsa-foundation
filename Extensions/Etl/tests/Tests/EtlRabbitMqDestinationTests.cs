using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Tests.Support;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1200: authoring rules, artifact compatibility, host bindings and the writer of the RabbitMQ destination.</summary>
public sealed class EtlRabbitMqDestinationTests
{
    private static readonly EtlScope Scope = new("tenant", "rabbitmq");

    [Fact]
    public void A_rabbitmq_destination_on_the_main_route_compiles_and_round_trips()
    {
        var artifact = Assert.IsType<EtlArtifact>(Compile(Pipeline()).Artifact);

        Assert.Equal(new EtlRabbitMqDestination("events", "etl.orders", "orders.created"), EtlArtifactV2Json.Read(artifact.Pipeline).Routes.Main);
        Assert.Contains("\"kind\":\"rabbitmq\"", artifact.Pipeline.GetRawText(), StringComparison.Ordinal);
        // The default exchange is the empty name.
        Assert.NotNull(Compile(WithDestination("", "orders")).Artifact);
    }

    [Fact]
    public void Existing_pipelines_keep_their_bytes_and_the_connector_kinds_keep_their_values()
    {
        var baseline = Compile(EtlV2TestPipelines.Create());

        Assert.Equal(baseline.Artifact!.ContentHash, Compile(EtlV2TestPipelines.Create()).Artifact!.ContentHash);
        Assert.DoesNotContain("rabbitmq", baseline.Artifact.Pipeline.GetRawText(), StringComparison.Ordinal);
        // Identities hash the integer kind, so the new kind is appended.
        Assert.Equal([0, 1, 2, 3, 4], new[] { EtlConnectorKind.Csv, EtlConnectorKind.Http, EtlConnectorKind.Jsonl, EtlConnectorKind.Sql,
            EtlConnectorKind.RabbitMq }.Select(kind => (int)kind));
    }

    [Theory]
    [InlineData("bad exchange", "orders")]
    [InlineData("events/x", "orders")]
    [InlineData("events", "two words")]
    [InlineData("events", "café")]
    public void An_exchange_or_routing_key_the_broker_cannot_take_is_refused(string exchange, string routingKey) =>
        AssertRejected(WithDestination(exchange, routingKey), "destination.rabbitmq-name-invalid");

    [Fact]
    public void Names_longer_than_a_protocol_short_string_are_refused()
    {
        AssertRejected(WithDestination(new string('e', 256), "orders"), "destination.rabbitmq-name-invalid");
        AssertRejected(WithDestination("events", new string('k', 256)), "destination.rabbitmq-name-invalid");
        Assert.NotNull(Compile(WithDestination(new string('e', 255), new string('k', 255))).Artifact);
    }

    [Fact]
    public void Only_the_main_route_can_publish_to_rabbitmq() =>
        AssertRejected(Pipeline() with { Routes = new(new EtlJsonlDestination("output"), new EtlRabbitMqDestination("events", "", "errors")) },
            "destination.rabbitmq-route-unsupported");

    [Fact]
    public void A_binding_pins_a_secret_free_handle_and_refuses_an_exchange_it_does_not_allow()
    {
        var bindings = new HostRabbitMqBindings([Binding("amqp://etl:secret@broker:5672/sales")], [new TestRabbitMqPublisher()]);

        var resolved = bindings.ResolveCurrent(Scope, "events", "MainOutput", "etl.orders");
        HostRabbitMqResolvedBinding With(string uri) =>
            new HostRabbitMqBindings([Binding(uri)], [new TestRabbitMqPublisher()]).ResolveCurrent(Scope, "events", "MainOutput", "etl.orders");

        Assert.Equal(resolved.Handle, With("amqp://etl:other@broker:5672/sales").Handle);
        Assert.NotEqual(resolved.Handle, With("amqp://etl:secret@broker:5672/finance").Handle);
        Assert.NotEqual(resolved.Handle, With("amqp://etl:secret@other:5672/sales").Handle);
        Assert.NotEqual(resolved.Handle, With("amqp://loader:secret@broker:5672/sales").Handle);
        Assert.NotEqual(resolved.Handle, With("amqp://etl:secret@broker:5673/sales").Handle);
        var wider = Binding("amqp://etl:secret@broker:5672/sales");
        wider.AllowedExchanges = ["etl.orders", "etl.refunds"];
        Assert.NotEqual(resolved.Handle,
            new HostRabbitMqBindings([wider], [new TestRabbitMqPublisher()]).ResolveCurrent(Scope, "events", "MainOutput", "etl.orders").Handle);
        Assert.DoesNotContain("secret", resolved.ToString(), StringComparison.Ordinal);
        Assert.Equal(resolved.Handle, bindings.ResolveExact(Scope, "events", "MainOutput", "v1", resolved.Handle, "etl.orders").Handle);
        Assert.Equal("etl.rabbitmq-exchange-not-allowed",
            Assert.Throws<EtlBindingException>(() => bindings.ResolveCurrent(Scope, "events", "MainOutput", "etl.other")).Code);
        Assert.Equal("etl.rabbitmq-exchange-not-allowed",
            Assert.Throws<EtlBindingException>(() => bindings.ResolveCurrent(Scope, "events", "MainOutput", "ETL.orders")).Code);
        Assert.Equal("etl.binding-revision-unavailable", Assert.Throws<EtlBindingException>(() =>
            bindings.ResolveExact(Scope, "events", "MainOutput", "v1", new string('0', 64), "etl.orders")).Code);
        Assert.Equal("etl.binding-unavailable", Assert.Throws<EtlBindingException>(() =>
            new HostRabbitMqBindings([Binding("not a uri")], [new TestRabbitMqPublisher()]).ResolveCurrent(Scope, "events", "MainOutput", "etl.orders")).Code);
        // Two current revisions of one alias are ambiguous.
        var second = Binding("amqp://etl:secret@broker/sales");
        second.Revision = "v2";
        Assert.Equal("etl.binding-ambiguous", Assert.Throws<EtlBindingException>(() =>
            new HostRabbitMqBindings([Binding("amqp://etl:secret@broker/sales"), second], [new TestRabbitMqPublisher()])
                .ResolveCurrent(Scope, "events", "MainOutput", "etl.orders")).Code);
        Assert.Equal("etl.rabbitmq-unavailable", Assert.Throws<EtlBindingException>(() =>
            new HostRabbitMqBindings([Binding("amqp://etl:secret@broker/sales")], []).ResolveCurrent(Scope, "events", "MainOutput", "etl.orders")).Code);
    }

    [Fact]
    public async Task The_writer_publishes_one_message_per_record_with_stable_ids_and_headers()
    {
        var publisher = new TestRabbitMqPublisher();
        var delivery = Delivery("""[{"id":1,"name":"Ada"},{"id":2,"name":"Björk"}]""");

        Assert.Equal(EtlRabbitMqWriteOutcome.Confirmed, await new EtlRabbitMqRouteWriter(publisher).WriteAsync(delivery, default));
        // A replay publishes the same messages again.
        Assert.Equal(EtlRabbitMqWriteOutcome.Confirmed, await new EtlRabbitMqRouteWriter(publisher).WriteAsync(delivery, default));

        var first = publisher.Deliveries.Take(2).ToArray();
        Assert.Equal(["effect-1:0", "effect-1:1"], first.Select(message => message.MessageId));
        Assert.Equal(first, publisher.Deliveries.Skip(2), new DeliveryComparer());
        Assert.All(first, message => Assert.Equal(("etl.orders", "orders.created"), (message.Exchange, message.RoutingKey)));
        Assert.Equal("""{"id":2,"name":"Björk"}""", first[1].Body);
        Assert.Equal(new Dictionary<string, string>
        {
            ["elsa-etl-run-id"] = "run-1", ["elsa-etl-batch-id"] = "batch-1", ["elsa-etl-effect-id"] = "effect-1", ["elsa-etl-record-index"] = "1"
        }, first[1].Headers);
    }

    [Fact]
    public async Task A_negative_acknowledgement_or_a_transient_failure_yields_and_a_refusal_fails_with_a_stable_code()
    {
        var delivery = Delivery("""[{"id":1}]""");
        async Task<EtlRabbitMqWriteOutcome> Write(object step) =>
            await new EtlRabbitMqRouteWriter(new TestRabbitMqPublisher().Then(step)).WriteAsync(delivery, default);

        Assert.Equal(EtlRabbitMqWriteOutcome.Yield, await Write(EtlRabbitMqPublishOutcome.Nacked));
        Assert.Equal(EtlRabbitMqWriteOutcome.Yield, await Write(new IOException("connection reset")));
        Assert.Equal("etl.rabbitmq-unroutable",
            (await Assert.ThrowsAsync<EtlEffectException>(() => Write(EtlRabbitMqPublishOutcome.Unroutable))).Code);
        Assert.Equal("etl.rabbitmq-publish-rejected",
            (await Assert.ThrowsAsync<EtlEffectException>(() => Write(new InvalidOperationException("ACCESS_REFUSED")))).Code);
    }

    [Theory]
    [InlineData("""{"id":1}""")]
    [InlineData("""[1]""")]
    [InlineData("""[{"id":1}""")]
    public async Task A_retained_body_that_is_not_an_array_of_objects_is_refused_before_publishing(string body)
    {
        var publisher = new TestRabbitMqPublisher();

        var exception = await Assert.ThrowsAsync<EtlEffectException>(() =>
            new EtlRabbitMqRouteWriter(publisher).WriteAsync(Delivery(body), default).AsTask());

        Assert.Equal("etl.connector-output-invalid", exception.Code);
        Assert.Equal(0, publisher.Attempts);
    }

    private static EtlRabbitMqRouteDelivery Delivery(string body) =>
        new("amqp://etl:secret@broker/sales", "etl.orders", "orders.created", Encoding.UTF8.GetBytes(body), "effect-1", "run-1", "batch-1");

    private static EtlPipelineV2 Pipeline() => WithDestination("etl.orders", "orders.created");

    private static EtlPipelineV2 WithDestination(string exchange, string routingKey) => EtlV2TestPipelines.Create() with
    {
        Routes = new(new EtlRabbitMqDestination("events", exchange, routingKey), new EtlJsonlDestination("errors"))
    };

    private static EtlHostRabbitMqBindingOptions Binding(string uri) => new()
    {
        TenantId = Scope.TenantId, PersistencePartition = Scope.PersistencePartition, Alias = "events", Revision = "v1",
        ConnectionString = uri, AllowedExchanges = ["etl.orders"]
    };

    private static EtlCompilationResult Compile(EtlPipelineV2 pipeline) => new EtlArtifactCompiler().Compile("orders", 1, pipeline);

    private static void AssertRejected(EtlPipelineV2 pipeline, string code)
    {
        var result = Compile(pipeline);
        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, item => item.Code == code);
    }

    private sealed class DeliveryComparer : IEqualityComparer<TestRabbitMqDelivery>
    {
        public bool Equals(TestRabbitMqDelivery? x, TestRabbitMqDelivery? y) => x is not null && y is not null &&
            (x.Exchange, x.RoutingKey, x.MessageId, x.Body) == (y.Exchange, y.RoutingKey, y.MessageId, y.Body) &&
            x.Headers.OrderBy(pair => pair.Key).SequenceEqual(y.Headers.OrderBy(pair => pair.Key));

        public int GetHashCode(TestRabbitMqDelivery obj) => obj.MessageId.GetHashCode(StringComparison.Ordinal);
    }
}
