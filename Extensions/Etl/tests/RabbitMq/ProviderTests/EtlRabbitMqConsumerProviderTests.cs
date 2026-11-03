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

        var (abandoned, abandonedBodies, _) = await ReceiveAsync(10, 1_000_000);
        await using (abandoned)
        {
            Assert.Equal(["""{"id":1}""", """{"id":2}""", """{"id":3}"""], abandonedBodies);
            Assert.Equal(0u, await ReadyAsync());
        }
        // Closed unacknowledged, as when a worker dies before the snapshot is recorded: the broker requeues them in order.
        Assert.Equal(3u, await ReadyAsync());

        var (acknowledged, acknowledgedBodies, _) = await ReceiveAsync(10, 1_000_000);
        await using (acknowledged)
        {
            Assert.Equal(["""{"id":1}""", """{"id":2}""", """{"id":3}"""], acknowledgedBodies);
            await acknowledged.AcknowledgeAsync(default);
        }
        Assert.Equal(0u, await ReadyAsync());
    }

    [SkippableFact]
    public async Task The_maximum_and_byte_bounds_leave_the_rest_queued()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await FillAsync("""{"id":1}""", """{"id":2}""", """{"id":3}""", """{"id":4}""");

        var (two, _, _) = await ReceiveAsync(2, 1_000_000);
        await using (two)
        {
            Assert.Equal(2, two.Count);
            await two.AcknowledgeAsync(default);
        }
        // A refused body goes back to the queue: one 8-byte body with its line end takes 9 of 10 bytes, so a second does not fit.
        var (bounded, boundedBodies, boundedRefusedFirst) = await ReceiveAsync(10, 10);
        await using (bounded)
        {
            Assert.Equal(["""{"id":3}"""], boundedBodies);
            Assert.False(boundedRefusedFirst);
            await bounded.AcknowledgeAsync(default);
        }
        var (tooLarge, tooLargeBodies, refusedFirst) = await ReceiveAsync(10, 5);
        await using (tooLarge)
        {
            Assert.Empty(tooLargeBodies);
            Assert.True(refusedFirst);
        }
        Assert.Equal(1u, await ReadyAsync());
    }

    [SkippableFact]
    public async Task A_run_stops_taking_messages_once_its_drain_budget_is_spent_after_at_least_one()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await FillAsync("""{"id":1}""", """{"id":2}""", """{"id":3}""");

        // With no time left, a run still takes one message, so a backlog always drains; the rest stay queued.
        var spent = new RabbitMqEtlConsumer(TimeSpan.Zero);
        await using (var receipt = await spent.ReceiveAsync(broker.ConnectionString, _queue, 10, Accept, default))
        {
            Assert.Equal(1, receipt.Count);
            await receipt.AcknowledgeAsync(default);
        }

        Assert.Equal(2u, await ReadyAsync());
    }

    [Fact]
    public void A_channel_closed_for_an_acknowledgement_timeout_is_transient_and_other_precondition_failures_are_not()
    {
        var timedOut = new RabbitMQ.Client.Exceptions.AlreadyClosedException(new RabbitMQ.Client.Events.ShutdownEventArgs(ShutdownInitiator.Peer, 406,
            "PRECONDITION_FAILED - delivery acknowledgement on channel 1 timed out. Timeout value used: 1800000 ms."));
        var inequivalent = new RabbitMQ.Client.Exceptions.AlreadyClosedException(new RabbitMQ.Client.Events.ShutdownEventArgs(ShutdownInitiator.Peer, 406,
            "PRECONDITION_FAILED - inequivalent arg 'durable' for queue 'people'"));

        Assert.Equal(EtlRabbitMqErrorKind.Transient, _consumer.Classify(timedOut));
        Assert.Equal(EtlRabbitMqErrorKind.Rejected, _consumer.Classify(inequivalent));
    }

    [SkippableFact]
    public async Task An_empty_queue_returns_at_once_a_missing_queue_is_rejected_and_an_unreachable_broker_is_transient()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await FillAsync();

        var (empty, emptyBodies, _) = await ReceiveAsync(10, 1_000);
        await using (empty)
            Assert.Empty(emptyBodies);
        var missing = await Assert.ThrowsAnyAsync<Exception>(() =>
            _consumer.ReceiveAsync(broker.ConnectionString, "missing-" + _queue, 10, Accept, default).AsTask());
        var unreachable = await Assert.ThrowsAnyAsync<Exception>(() =>
            _consumer.ReceiveAsync("amqp://etl:unused@127.0.0.1:1/", _queue, 10, Accept, default).AsTask());

        Assert.Equal(EtlRabbitMqErrorKind.Rejected, _consumer.Classify(missing));
        Assert.Equal(EtlRabbitMqErrorKind.Transient, _consumer.Classify(unreachable));
    }

    [SkippableFact]
    public async Task A_drain_that_fails_partway_or_a_dropped_connection_returns_the_taken_messages_to_the_queue()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await FillAsync("""{"id":1}""", """{"id":2}""", """{"id":3}""");

        // The runtime's accept fails on the second body, after the first was taken.
        var taken = 0;
        await Assert.ThrowsAsync<IOException>(() => _consumer.ReceiveAsync(broker.ConnectionString, _queue, 10,
            (_, _) => ++taken == 2 ? throw new IOException("snapshot write failed") : ValueTask.FromResult(true), default).AsTask());
        Assert.Equal(3u, await ReadyAsync());

        // The broker drops the connection while the receipt holds the messages unacknowledged.
        var (held, heldBodies, _) = await ReceiveAsync(10, 1_000_000);
        await using (held)
        {
            Assert.Equal(3, heldBodies.Count);
            Skip.IfNot(await broker.ControlAsync("close_all_connections", "dropped by the test"), "The broker was supplied, not started.");
            Assert.Equal(3u, await ReadyAsync());
        }
    }

    [SkippableFact]
    public async Task A_stream_queue_which_basic_get_cannot_read_is_rejected()
    {
        Skip.If(broker.SkipReason is not null, broker.SkipReason);
        await using (var connection = await broker.ConnectAsync())
        await using (var channel = await connection.CreateChannelAsync())
            await channel.QueueDeclareAsync(_queue, durable: true, exclusive: false, autoDelete: false,
                new Dictionary<string, object?> { ["x-queue-type"] = "stream" });

        var stream = await Assert.ThrowsAnyAsync<Exception>(() => _consumer.ReceiveAsync(broker.ConnectionString, _queue, 10, Accept, default).AsTask());

        Assert.Equal(EtlRabbitMqErrorKind.Rejected, _consumer.Classify(stream));
    }

    private static ValueTask<bool> Accept(ReadOnlyMemory<byte> body, CancellationToken cancellationToken) => ValueTask.FromResult(true);

    /// <summary>Receives with the runtime's bound: each body counts with its line end, and the first that would not fit is refused.</summary>
    private async Task<(IEtlRabbitMqReceipt Receipt, List<string> Bodies, bool RefusedFirst)> ReceiveAsync(int maxMessages, long maxBytes)
    {
        var bodies = new List<string>();
        long written = 0;
        var refusedFirst = false;
        var receipt = await _consumer.ReceiveAsync(broker.ConnectionString, _queue, maxMessages, (body, _) =>
        {
            if (written + body.Length + 1 > maxBytes)
            {
                refusedFirst = written == 0;
                return ValueTask.FromResult(false);
            }
            written += body.Length + 1;
            bodies.Add(Encoding.UTF8.GetString(body.Span));
            return ValueTask.FromResult(true);
        }, default);
        return (receipt, bodies, refusedFirst);
    }

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
