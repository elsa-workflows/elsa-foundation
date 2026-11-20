using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Output;
using RabbitMQ.Client;

namespace Elsa.Etl.RabbitMq.ProviderTests;

/// <summary>
/// Spec 1200 on a real RabbitMQ broker: the runtime's writer drives the driver package's publisher, so the message shape,
/// publisher confirms and failure classification are proven together.
/// </summary>
[Collection(EtlRabbitMqBrokerCollection.Name)]
public sealed class EtlRabbitMqProviderTests(EtlRabbitMqBrokerFixture broker)
{
    private readonly RabbitMqEtlPublisher _publisher = new();
    private readonly string _name = "etl-" + Guid.NewGuid().ToString("N")[..12];

    [SkippableFact]
    public async Task A_confirmed_batch_lands_in_a_bound_queue_as_persistent_json_messages_in_order()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await DeclareAsync(_name, bind: true);

        var outcome = await Writer().WriteAsync(Delivery(_name, "loaded", """[{"id":1,"name":"Ada"},{"id":2,"name":"Bram"},{"id":3,"name":"Cleo"}]"""), default);

        Assert.Equal(EtlRabbitMqWriteOutcome.Confirmed, outcome);
        var messages = await DrainAsync(_name);
        Assert.Equal(["""{"id":1,"name":"Ada"}""", """{"id":2,"name":"Bram"}""", """{"id":3,"name":"Cleo"}"""],
            messages.Select(message => Encoding.UTF8.GetString(message.Body.Span)));
        Assert.Equal(["effect-1:0", "effect-1:1", "effect-1:2"], messages.Select(message => message.BasicProperties.MessageId));
        Assert.All(messages, message =>
        {
            Assert.Equal("application/json", message.BasicProperties.ContentType);
            Assert.Equal(DeliveryModes.Persistent, message.BasicProperties.DeliveryMode);
            Assert.Equal("run-1", Header(message, "elsa-etl-run-id"));
            Assert.Equal("batch-1", Header(message, "elsa-etl-batch-id"));
            Assert.Equal("effect-1", Header(message, "elsa-etl-effect-id"));
        });
        Assert.Equal("2", Header(messages[2], "elsa-etl-record-index"));
    }

    [SkippableFact]
    public async Task The_default_exchange_routes_to_the_queue_the_routing_key_names()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await DeclareAsync(_name, bind: false);

        Assert.Equal(EtlRabbitMqWriteOutcome.Confirmed, await Writer().WriteAsync(Delivery("", _name, """[{"id":1}]"""), default));

        Assert.Single(await DrainAsync(_name));
    }

    [SkippableFact]
    public async Task A_message_no_queue_matches_is_returned_and_fails_the_batch_as_unroutable()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await DeclareAsync(_name, bind: false);

        var exception = await Assert.ThrowsAsync<EtlEffectException>(() =>
            Writer().WriteAsync(Delivery(_name, "nobody", """[{"id":1}]"""), default).AsTask());

        Assert.Equal("etl.rabbitmq-unroutable", exception.Code);
    }

    [SkippableFact]
    public async Task A_negative_acknowledgement_yields_so_the_batch_is_published_again()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        // A queue that holds one message and rejects further publishes, so the broker negatively acknowledges the second.
        await DeclareAsync(_name, bind: true, new Dictionary<string, object?> { ["x-max-length"] = 1, ["x-overflow"] = "reject-publish" });

        var outcome = await Writer().WriteAsync(Delivery(_name, "loaded", """[{"id":1},{"id":2}]"""), default);

        Assert.Equal(EtlRabbitMqWriteOutcome.Yield, outcome);
        Assert.Equal(["effect-1:0"], (await DrainAsync(_name)).Select(message => message.BasicProperties.MessageId));
    }

    [SkippableFact]
    public async Task A_missing_exchange_or_refused_credentials_fail_the_batch_without_retrying()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);

        var missing = await Assert.ThrowsAsync<EtlEffectException>(() =>
            Writer().WriteAsync(Delivery("missing-" + _name, "loaded", """[{"id":1}]"""), default).AsTask());
        var refused = await Assert.ThrowsAsync<EtlEffectException>(() =>
            Writer().WriteAsync(Delivery("", _name, """[{"id":1}]""") with { ConnectionString = broker.WithWrongPassword() }, default).AsTask());

        Assert.Equal("etl.rabbitmq-publish-rejected", missing.Code);
        Assert.Equal("etl.rabbitmq-publish-rejected", refused.Code);
    }

    [SkippableFact]
    public async Task A_virtual_host_at_its_connection_limit_yields_and_one_the_user_may_not_open_is_rejected()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        // A virtual host that admits no connection answers connection.open with 530, as one at its limit does.
        Skip.IfNot(await broker.ControlAsync("add_vhost", _name), "The supplied broker cannot be configured.");
        await broker.ControlAsync("set_permissions", "-p", _name, "etl", ".*", ".*", ".*");
        await broker.ControlAsync("set_vhost_limits", "-p", _name, """{"max-connections": 0}""");

        var limited = await Writer().WriteAsync(Delivery("", _name, """[{"id":1}]""") with { ConnectionString = broker.WithVirtualHost(_name) }, default);
        var forbidden = await Assert.ThrowsAsync<EtlEffectException>(() => Writer().WriteAsync(
            Delivery("", _name, """[{"id":1}]""") with { ConnectionString = broker.WithVirtualHost("missing-" + _name) }, default).AsTask());
        // A virtual host the user may not open stays a refusal even when its name reads like the broker's limit wording.
        var named = "limit (1) is reached " + _name;
        await broker.ControlAsync("add_vhost", named);
        var namedRefusal = await Assert.ThrowsAsync<EtlEffectException>(() => Writer().WriteAsync(
            Delivery("", _name, """[{"id":1}]""") with { ConnectionString = broker.WithVirtualHost(named) }, default).AsTask());

        Assert.Equal(EtlRabbitMqWriteOutcome.Yield, limited);
        Assert.Equal("etl.rabbitmq-publish-rejected", forbidden.Code);
        Assert.Equal("etl.rabbitmq-publish-rejected", namedRefusal.Code);
    }

    [SkippableFact]
    public async Task A_user_at_its_channel_limit_yields()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        // A user that may open no channel is refused with 530 when the publisher opens its channel, as one at its limit is.
        var secret = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
        Skip.IfNot(await broker.ControlAsync("add_user", _name, secret), "The supplied broker cannot be configured.");
        await broker.ControlAsync("set_permissions", "-p", "/", _name, ".*", ".*", ".*");
        await broker.ControlAsync("set_user_limits", _name, """{"max-channels": 0}""");
        var uri = new UriBuilder(broker.ConnectionString) { UserName = _name, Password = secret }.Uri.ToString();

        Assert.Equal(EtlRabbitMqWriteOutcome.Yield,
            await Writer().WriteAsync(Delivery("", _name, """[{"id":1}]""") with { ConnectionString = uri }, default));
    }

    [SkippableFact]
    public async Task A_broker_under_a_resource_alarm_yields_and_holds_at_most_one_blocked_connection_until_it_unblocks()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await DeclareAsync(_name, bind: true);
        // A disk alarm blocks every publishing connection and stops reading it, without closing it.
        Skip.IfNot(await broker.ControlAsync("set_disk_free_limit", "10000GB"), "The supplied broker cannot be configured.");
        var cleared = false;
        // A regression fails the test instead of hanging it.
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var blocked = new EtlRabbitMqRouteWriter(new RabbitMqEtlPublisher(TimeSpan.FromSeconds(3)));
            var started = DateTime.UtcNow;
            var first = await blocked.WriteAsync(Delivery(_name, "loaded", """[{"id":1}]"""), guard.Token);
            var firstTook = DateTime.UtcNow - started;
            // While the target's connection is held blocked, a further publication yields without opening another.
            started = DateTime.UtcNow;
            var second = await blocked.WriteAsync(Delivery(_name, "loaded", """[{"id":2}]"""), guard.Token);
            var secondTook = DateTime.UtcNow - started;
            // The caller's cancellation ends a blocked publication promptly too, whatever the confirm bound.
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            started = DateTime.UtcNow;
            try { await Writer().WriteAsync(Delivery(_name, "loaded", """[{"id":3}]"""), stop.Token); }
            catch (OperationCanceledException) { }
            var cancelledTook = DateTime.UtcNow - started;

            Assert.Equal((EtlRabbitMqWriteOutcome.Yield, EtlRabbitMqWriteOutcome.Yield), (first, second));
            Assert.True(firstTook < TimeSpan.FromSeconds(8), $"The blocked publication took {firstTook}.");
            Assert.True(secondTook < TimeSpan.FromSeconds(1), $"The publication behind a held connection took {secondTook}.");
            Assert.True(cancelledTook < TimeSpan.FromSeconds(6), $"The cancelled publication took {cancelledTook}.");
            // One held connection per publisher and target, not one per attempt.
            Assert.True(await broker.BlockedConnectionsAsync() <= 2, "More than one blocked connection per publisher was left open.");

            await broker.ControlAsync("set_disk_free_limit", "50MB");
            cleared = true;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (await broker.BlockedConnectionsAsync() > 0 && DateTime.UtcNow < deadline)
                await Task.Delay(250, guard.Token);
            Assert.Equal(0, await broker.BlockedConnectionsAsync());
            // Once the broker unblocks, the target publishes again.
            Assert.Equal(EtlRabbitMqWriteOutcome.Confirmed, await blocked.WriteAsync(Delivery(_name, "loaded", """[{"id":4}]"""), guard.Token));
        }
        finally
        {
            if (!cleared) await broker.ControlAsync("set_disk_free_limit", "50MB");
        }
    }

    [SkippableFact]
    public async Task An_unreachable_broker_yields()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);

        var outcome = await Writer().WriteAsync(Delivery("", _name, """[{"id":1}]""") with
        {
            ConnectionString = "amqp://etl:unused@127.0.0.1:1/"
        }, default);

        Assert.Equal(EtlRabbitMqWriteOutcome.Yield, outcome);
    }

    [Fact]
    public void A_target_names_the_host_port_virtual_host_and_user_without_the_password()
    {
        Assert.Equal(new EtlRabbitMqTarget("broker", 5672, "/", "etl"), _publisher.DescribeTarget("amqp://etl:secret@broker"));
        Assert.Equal(new EtlRabbitMqTarget("broker", 5671, "sales", "etl"), _publisher.DescribeTarget("amqps://etl:secret@broker/sales"));
        Assert.Equal(new EtlRabbitMqTarget("broker", 5673, "/", "etl"), _publisher.DescribeTarget("amqp://etl:secret@broker:5673/%2f"));
        Assert.ThrowsAny<ArgumentException>(() => _publisher.DescribeTarget("http://broker"));
    }

    private EtlRabbitMqRouteWriter Writer() => new(_publisher);

    private EtlRabbitMqRouteDelivery Delivery(string exchange, string routingKey, string body) =>
        new(broker.ConnectionString, exchange, routingKey, Encoding.UTF8.GetBytes(body), "effect-1", "run-1", "batch-1");

    private async Task DeclareAsync(string name, bool bind, IDictionary<string, object?>? queueArguments = null)
    {
        await using var connection = await broker.ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(name, ExchangeType.Direct, durable: true, autoDelete: false);
        await channel.QueueDeclareAsync(name, durable: true, exclusive: false, autoDelete: false, arguments: queueArguments);
        if (bind)
            await channel.QueueBindAsync(name, name, "loaded");
    }

    private async Task<List<BasicGetResult>> DrainAsync(string queue)
    {
        await using var connection = await broker.ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();
        var messages = new List<BasicGetResult>();
        while (await channel.BasicGetAsync(queue, autoAck: true) is { } message)
            messages.Add(message);
        return messages;
    }

    private static string? Header(BasicGetResult message, string name) =>
        message.BasicProperties.Headers?.TryGetValue(name, out var value) == true && value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : null;
}
