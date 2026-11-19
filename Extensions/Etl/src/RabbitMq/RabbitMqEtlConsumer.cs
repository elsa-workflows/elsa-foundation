using Elsa.Etl.Core.Runtime;
using RabbitMQ.Client;

namespace Elsa.Etl.RabbitMq;

/// <summary>
/// Receives a finite source's messages over RabbitMQ.Client with <c>basic.get</c> and no automatic acknowledgement (spec
/// 1201), handing each body to the runtime as it arrives. The messages stay unacknowledged on the receipt's channel until the
/// runtime acknowledges them; closing the channel unacknowledged, or losing the connection, makes the broker requeue them.
/// </summary>
internal sealed class RabbitMqEtlConsumer : IEtlRabbitMqConsumer
{
    // The broker closes a channel whose deliveries stay unacknowledged past its consumer_timeout (30 minutes by default), and
    // the clock starts at the first delivery. A run stops taking messages well before that, after at least one, and the rest
    // stay queued for the next run, so a slow or distant broker drains a backlog over several runs instead of failing each.
    private static readonly TimeSpan DefaultDrainBudget = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _drainBudget;

    public RabbitMqEtlConsumer() : this(DefaultDrainBudget)
    {
    }

    /// <summary>Uses another bound on how long one run takes messages, for tests.</summary>
    internal RabbitMqEtlConsumer(TimeSpan drainBudget) => _drainBudget = drainBudget;

    public async ValueTask<IEtlRabbitMqReceipt> ReceiveAsync(string connectionString, string queue, int maxMessages,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<bool>> accept, CancellationToken cancellationToken)
    {
        var connection = await RabbitMqEtlPublisher.Factory(connectionString).CreateConnectionAsync(cancellationToken);
        try
        {
            var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            var count = 0;
            ulong lastTag = 0;
            var draining = System.Diagnostics.Stopwatch.StartNew();
            while (count < maxMessages && (count == 0 || draining.Elapsed < _drainBudget) &&
                   await channel.BasicGetAsync(queue, autoAck: false, cancellationToken) is { } message)
            {
                // A body the runtime refuses, because it would not fit the snapshot, goes back to the queue at once.
                if (!await accept(message.Body, cancellationToken))
                {
                    await channel.BasicRejectAsync(message.DeliveryTag, requeue: true, cancellationToken);
                    break;
                }
                count++;
                lastTag = message.DeliveryTag;
            }
            return new Receipt(connection, channel, count, lastTag);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public EtlRabbitMqErrorKind Classify(Exception exception) => RabbitMqEtlPublisher.ClassifyFailure(exception);

    private sealed class Receipt(IConnection connection, IChannel channel, int count, ulong lastTag) : IEtlRabbitMqReceipt
    {
        public int Count => count;

        // Delivery tags are per channel and increase, so one multiple acknowledgement covers every message taken.
        public async ValueTask AcknowledgeAsync(CancellationToken cancellationToken)
        {
            if (lastTag > 0)
                await channel.BasicAckAsync(lastTag, multiple: true, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await channel.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
