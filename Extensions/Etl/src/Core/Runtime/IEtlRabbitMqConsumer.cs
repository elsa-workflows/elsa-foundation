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
    /// acknowledging them, and hands each body to <paramref name="accept"/> as it arrives; the body is valid only during that
    /// call. Stops when the queue is empty, at the first body <paramref name="accept"/> refuses, which goes back to the
    /// queue with every later message, or, after at least one message, once a time bound well inside the broker's
    /// <c>consumer_timeout</c> is spent; whatever was not taken stays queued. An exception <paramref name="accept"/> throws
    /// propagates unchanged, so the runtime can tell its own failures from the broker's.
    /// </summary>
    ValueTask<IEtlRabbitMqReceipt> ReceiveAsync(string connectionString, string queue, int maxMessages,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<bool>> accept, CancellationToken cancellationToken);

    /// <summary>Classifies an exception <see cref="ReceiveAsync"/> or a receipt raised.</summary>
    EtlRabbitMqErrorKind Classify(Exception exception);
}

/// <summary>Messages received and not yet acknowledged. Disposing it unacknowledged lets the broker requeue them.</summary>
public interface IEtlRabbitMqReceipt : IAsyncDisposable
{
    /// <summary>How many messages were accepted.</summary>
    int Count { get; }

    /// <summary>Acknowledges every accepted message.</summary>
    ValueTask AcknowledgeAsync(CancellationToken cancellationToken);
}
