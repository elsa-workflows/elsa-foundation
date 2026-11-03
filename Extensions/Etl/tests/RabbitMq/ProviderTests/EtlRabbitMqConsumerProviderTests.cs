using System.Text;
using Elsa.Etl.Core.Runtime;
using RabbitMQ.Client;

namespace Elsa.Etl.RabbitMq.ProviderTests;

/// <summary>
/// Spec 1201 on a real RabbitMQ broker: received messages stay unacknowledged until the receipt acknowledges them, and a
/// receipt closed unacknowledged returns them to the queue, as a worker that dies does.
/// </summary>
[Collection(EtlRabbitMqBrokerCollection.Name)]
public sealed class EtlRabbitMqConsumerProviderTests(EtlRabbitMqBrokerFixture broker)
{
    private readonly RabbitMqEtlConsumer _consumer = new();
    private readonly string _queue = "etl-in-" + Guid.NewGuid().ToString("N")[..12];

    [SkippableFact]
    public async Task Received_messages_stay_unacknowledged_until_acknowledged_and_return_to_the_queue_otherwise()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await FillAsync("""{"id":1}""", """{"id":2}""", """{"id":3}""");

        await using (var abandoned = await _consumer.ReceiveAsync(broker.ConnectionString, _queue, 10, 1_000_000, default))
        {
            Assert.Equal(["""{"id":1}""", """{"id":2}""", """{"id":3}"""], abandoned.Bodies.Select(Text));
            Assert.Equal(0u, await ReadyAsync());
        }
        // Closed unacknowledged, as when a worker dies before the snapshot is recorded: the broker requeues them in order.
        Assert.Equal(3u, await ReadyAsync());

        await using (var acknowledged = await _consumer.ReceiveAsync(broker.ConnectionString, _queue, 10, 1_000_000, default))
        {
            Assert.Equal(["""{"id":1}""", """{"id":2}""", """{"id":3}"""], acknowledged.Bodies.Select(Text));
            await acknowledged.AcknowledgeAsync(default);
        }
        Assert.Equal(0u, await ReadyAsync());
    }

    [SkippableFact]
    public async Task The_maximum_and_byte_bounds_leave_the_rest_queued()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await FillAsync("""{"id":1}""", """{"id":2}""", """{"id":3}""", """{"id":4}""");

        await using (var two = await _consumer.ReceiveAsync(broker.ConnectionString, _queue, 2, 1_000_000, default))
        {
            Assert.Equal(2, two.Bodies.Count);
            await two.AcknowledgeAsync(default);
        }
        // Each body counts with its line end: one 8-byte body takes 9 of the 10 bytes, so a second does not fit.
        await using (var bounded = await _consumer.ReceiveAsync(broker.ConnectionString, _queue, 10, 10, default))
        {
            Assert.Equal(["""{"id":3}"""], bounded.Bodies.Select(Text));
            Assert.False(bounded.FirstMessageTooLarge);
            await bounded.AcknowledgeAsync(default);
        }
        await using (var tooLarge = await _consumer.ReceiveAsync(broker.ConnectionString, _queue, 10, 5, default))
        {
            Assert.Empty(tooLarge.Bodies);
            Assert.True(tooLarge.FirstMessageTooLarge);
        }
        Assert.Equal(1u, await ReadyAsync());
    }

    [SkippableFact]
    public async Task An_empty_queue_returns_at_once_a_missing_queue_is_rejected_and_an_unreachable_broker_is_transient()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await FillAsync();

        await using (var empty = await _consumer.ReceiveAsync(broker.ConnectionString, _queue, 10, 1_000, default))
            Assert.Empty(empty.Bodies);
        var missing = await Assert.ThrowsAnyAsync<Exception>(() =>
            _consumer.ReceiveAsync(broker.ConnectionString, "missing-" + _queue, 10, 1_000, default).AsTask());
        var unreachable = await Assert.ThrowsAnyAsync<Exception>(() =>
            _consumer.ReceiveAsync("amqp://etl:unused@127.0.0.1:1/", _queue, 10, 1_000, default).AsTask());

        Assert.Equal(EtlRabbitMqErrorKind.Rejected, _consumer.Classify(missing));
        Assert.Equal(EtlRabbitMqErrorKind.Transient, _consumer.Classify(unreachable));
    }

    private static string Text(ReadOnlyMemory<byte> body) => Encoding.UTF8.GetString(body.Span);

    private async Task FillAsync(params string[] messages)
    {
        await using var connection = await broker.ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        await channel.QueueDeclareAsync(_queue, durable: true, exclusive: false, autoDelete: false);
        foreach (var message in messages)
            await channel.BasicPublishAsync("", _queue, mandatory: true, new BasicProperties { DeliveryMode = DeliveryModes.Persistent },
                Encoding.UTF8.GetBytes(message));
    }

    private async Task<uint> ReadyAsync()
    {
        // The broker counts requeued messages as ready once the closed channel is gone; allow it a moment.
        for (var attempt = 0; ; attempt++)
        {
            await using var connection = await broker.ConnectAsync();
            await using var channel = await connection.CreateChannelAsync();
            var ready = (await channel.QueueDeclarePassiveAsync(_queue)).MessageCount;
            if (ready > 0 || attempt == 20) return ready;
            await Task.Delay(100);
        }
    }
}
