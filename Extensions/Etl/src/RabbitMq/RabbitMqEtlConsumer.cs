using Elsa.Etl.Core.Runtime;
using RabbitMQ.Client;

namespace Elsa.Etl.RabbitMq;

/// <summary>
/// Receives a finite source's messages over RabbitMQ.Client with <c>basic.get</c> and no automatic acknowledgement (spec
/// 1201). The messages stay unacknowledged on the receipt's channel until the runtime acknowledges them; closing the channel
/// unacknowledged, or losing the connection, makes the broker requeue them.
/// </summary>
internal sealed class RabbitMqEtlConsumer : IEtlRabbitMqConsumer
{
    public async ValueTask<IEtlRabbitMqReceipt> ReceiveAsync(string connectionString, string queue, int maxMessages, long maxBytes,
        CancellationToken cancellationToken)
    {
        var connection = await RabbitMqEtlPublisher.Factory(connectionString).CreateConnectionAsync(cancellationToken);
        try
        {
            var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            var bodies = new List<ReadOnlyMemory<byte>>();
            ulong lastTag = 0;
            long bytes = 0;
            var firstTooLarge = false;
            while (bodies.Count < maxMessages && await channel.BasicGetAsync(queue, autoAck: false, cancellationToken) is { } message)
            {
                // Each body takes a line end in the snapshot; a message that would not fit goes back to the queue at once.
                if (bytes + message.Body.Length + 1 > maxBytes)
                {
                    firstTooLarge = bodies.Count == 0;
                    await channel.BasicRejectAsync(message.DeliveryTag, requeue: true, cancellationToken);
                    break;
                }
                bytes += message.Body.Length + 1;
                bodies.Add(message.Body.ToArray());
                lastTag = message.DeliveryTag;
            }
            return new Receipt(connection, channel, bodies, lastTag, firstTooLarge);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public EtlRabbitMqErrorKind Classify(Exception exception) => RabbitMqEtlPublisher.ClassifyFailure(exception);

    private sealed class Receipt(IConnection connection, IChannel channel, IReadOnlyList<ReadOnlyMemory<byte>> bodies, ulong lastTag,
        bool firstTooLarge) : IEtlRabbitMqReceipt
    {
        public IReadOnlyList<ReadOnlyMemory<byte>> Bodies => bodies;
        public bool FirstMessageTooLarge => firstTooLarge;

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
