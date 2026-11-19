namespace Elsa.Etl.Core.Runtime;

/// <summary>
/// The adapter that isolates the RabbitMQ driver from the ETL runtime (spec 1200). The runtime's writer owns the protocol:
/// which messages a route body becomes, their ids and headers, and what each outcome means. The publisher only connects,
/// publishes on a confirming channel and classifies failures.
/// </summary>
/// <remarks>A single-implementation adapter: the runtime resolves at most one, registered by <c>Elsa.Etl.RabbitMq</c>.</remarks>
public interface IEtlRabbitMqPublisher
{
    /// <summary>The connection's target without any secret, for the binding's pinned handle.</summary>
    /// <exception cref="ArgumentException">The AMQP URI is not valid, for example not an <c>amqp</c> or <c>amqps</c> URI.</exception>
    /// <exception cref="FormatException">The AMQP URI is malformed (<see cref="UriFormatException"/>).</exception>
    EtlRabbitMqTarget DescribeTarget(string connectionString);

    /// <summary>
    /// Publishes the messages in order on one channel with publisher confirms, each persistent, mandatory and of content type
    /// <c>application/json</c>, and returns once every message was confirmed, or at the first message the broker returned as
    /// unroutable or negatively acknowledged. Earlier messages of the list may already have been delivered. Each confirm must be
    /// bounded, so a broker that blocks publishers under a resource alarm surfaces as a transient failure, not an endless wait.
    /// </summary>
    ValueTask<EtlRabbitMqPublishOutcome> PublishAsync(string connectionString, string exchange, string routingKey,
        IReadOnlyList<EtlRabbitMqMessage> messages, CancellationToken cancellationToken);

    /// <summary>Classifies an exception <see cref="PublishAsync"/> raised.</summary>
    EtlRabbitMqErrorKind Classify(Exception exception);
}

/// <summary>A RabbitMQ connection's target without secrets.</summary>
public sealed record EtlRabbitMqTarget(string Host, int Port, string VirtualHost, string User);

/// <summary>One message to publish: its id, string headers and UTF-8 JSON body.</summary>
public sealed record EtlRabbitMqMessage(string MessageId, IReadOnlyList<KeyValuePair<string, string>> Headers, ReadOnlyMemory<byte> Body);

/// <summary>How a publication ended.</summary>
public enum EtlRabbitMqPublishOutcome
{
    /// <summary>The broker confirmed every message.</summary>
    Confirmed,
    /// <summary>The broker returned a mandatory message because no queue matched.</summary>
    Unroutable,
    /// <summary>The broker negatively acknowledged a message.</summary>
    Nacked
}

/// <summary>The classes of publication failure the writer distinguishes.</summary>
public enum EtlRabbitMqErrorKind
{
    /// <summary>The broker is unreachable or the connection or channel closed; publishing again may succeed.</summary>
    Transient,
    /// <summary>A deterministic refusal, such as access refused, a missing exchange or failed authentication.</summary>
    Rejected
}
