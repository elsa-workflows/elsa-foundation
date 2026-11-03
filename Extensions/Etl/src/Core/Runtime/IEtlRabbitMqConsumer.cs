namespace Elsa.Etl.Core.Runtime;

/// <summary>
/// The adapter that receives RabbitMQ messages for a finite source without acknowledging them (spec 1201). The runtime owns
/// the order: it acknowledges a receipt only after the snapshot of its messages is recorded as the run's prepared input.
/// </summary>
/// <remarks>A single-implementation adapter, registered by <c>Elsa.Etl.RabbitMq</c> beside <see cref="IEtlRabbitMqPublisher"/>.</remarks>
public interface IEtlRabbitMqConsumer
{
    /// <summary>
    /// Receives up to <paramref name="maxMessages"/> messages from the queue on one channel, in queue order, without
    /// acknowledging them, and stops before a message that would take the bodies past <paramref name="maxBytes"/>, counting
    /// each body plus one byte for its line end; that message and every later one stay queued. Returns at once when the queue
    /// is empty.
    /// </summary>
    ValueTask<IEtlRabbitMqReceipt> ReceiveAsync(string connectionString, string queue, int maxMessages, long maxBytes,
        CancellationToken cancellationToken);

    /// <summary>Classifies an exception <see cref="ReceiveAsync"/> or a receipt raised.</summary>
    EtlRabbitMqErrorKind Classify(Exception exception);
}

/// <summary>Messages received and not yet acknowledged. Disposing it unacknowledged lets the broker requeue them.</summary>
public interface IEtlRabbitMqReceipt : IAsyncDisposable
{
    /// <summary>The message bodies, in queue order.</summary>
    IReadOnlyList<ReadOnlyMemory<byte>> Bodies { get; }

    /// <summary>Whether the first message waiting in the queue is larger than the byte bound on its own.</summary>
    bool FirstMessageTooLarge { get; }

    /// <summary>Acknowledges every received message.</summary>
    ValueTask AcknowledgeAsync(CancellationToken cancellationToken);
}
