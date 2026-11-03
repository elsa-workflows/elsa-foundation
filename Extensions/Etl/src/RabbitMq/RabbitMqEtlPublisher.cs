using System.Net.Sockets;
using Elsa.Etl.Core.Runtime;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Elsa.Etl.RabbitMq;

/// <summary>
/// Publishes ETL route messages over RabbitMQ.Client on a channel with publisher confirmation tracking (spec 1200), so each
/// publish completes only once the broker confirmed the message, and a return or negative acknowledgement surfaces as a
/// <see cref="PublishException"/>.
/// </summary>
/// <remarks>
/// Each publication opens its own connection without automatic recovery: the runtime retries a whole batch, so a connection
/// that recovered mid-batch could not tell which messages it confirmed. Each confirm and each protocol round trip is bounded,
/// because a broker under a resource alarm blocks publishers without closing their connections; a bounded wait yields the
/// attempt and its capacity instead of holding them until the alarm clears.
/// </remarks>
internal sealed class RabbitMqEtlPublisher : IEtlRabbitMqPublisher
{
    // AMQP reply codes that a retry cannot cure: access refused, not found and precondition failed (a channel error), and
    // not allowed (a connection error, such as a virtual host the user may not open). RabbitMQ also answers a connection or
    // channel limit (per virtual host, user or node) with not allowed; a later attempt can pass it, so such a reply is transient.
    private static readonly HashSet<ushort> RejectedReplyCodes = [403, 404, 406, 530];
    private const ushort NotAllowed = 530;
    private static readonly TimeSpan DefaultConfirmTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _confirmTimeout;

    public RabbitMqEtlPublisher() : this(DefaultConfirmTimeout)
    {
    }

    /// <summary>Uses a shorter bound on each confirm and round trip, for tests.</summary>
    internal RabbitMqEtlPublisher(TimeSpan confirmTimeout) => _confirmTimeout = confirmTimeout;

    public EtlRabbitMqTarget DescribeTarget(string connectionString)
    {
        var factory = Factory(connectionString);
        // The factory's URI carries the resolved port: the default for amqp (5672) or amqps (5671) when the URI names none.
        return new(factory.HostName, factory.Uri.Port, factory.VirtualHost, factory.UserName);
    }

    public async ValueTask<EtlRabbitMqPublishOutcome> PublishAsync(string connectionString, string exchange, string routingKey,
        IReadOnlyList<EtlRabbitMqMessage> messages, CancellationToken cancellationToken)
    {
        var factory = Factory(connectionString, _confirmTimeout);
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
        foreach (var message in messages)
        {
            var properties = new BasicProperties
            {
                MessageId = message.MessageId,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Headers = message.Headers.ToDictionary(header => header.Key, object? (header) => header.Value)
            };
            using var confirm = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            confirm.CancelAfter(_confirmTimeout);
            try
            {
                // With confirmation tracking, the publish completes when the broker confirms the message.
                await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, message.Body, confirm.Token);
            }
            catch (PublishException exception)
            {
                return exception.IsReturn ? EtlRabbitMqPublishOutcome.Unroutable : EtlRabbitMqPublishOutcome.Nacked;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The RabbitMQ broker did not confirm a message in time.", exception);
            }
        }
        return EtlRabbitMqPublishOutcome.Confirmed;
    }

    public EtlRabbitMqErrorKind Classify(Exception exception) => exception switch
    {
        AuthenticationFailureException => EtlRabbitMqErrorKind.Rejected,
        BrokerUnreachableException { InnerException: AuthenticationFailureException } => EtlRabbitMqErrorKind.Rejected,
        BrokerUnreachableException => EtlRabbitMqErrorKind.Transient,
        // A closed connection or channel (AlreadyClosedException is one too) carries the broker's reply code.
        OperationInterruptedException interrupted => interrupted.ShutdownReason is { } reason ? Classify(reason) : EtlRabbitMqErrorKind.Transient,
        PublishException or IOException or SocketException or TimeoutException or OperationCanceledException => EtlRabbitMqErrorKind.Transient,
        _ => EtlRabbitMqErrorKind.Rejected
    };

    private static EtlRabbitMqErrorKind Classify(ShutdownEventArgs reason) =>
        reason.ReplyCode == NotAllowed && reason.ReplyText.Contains("limit", StringComparison.OrdinalIgnoreCase)
            ? EtlRabbitMqErrorKind.Transient
            : RejectedReplyCodes.Contains(reason.ReplyCode) ? EtlRabbitMqErrorKind.Rejected : EtlRabbitMqErrorKind.Transient;

    private static ConnectionFactory Factory(string connectionString, TimeSpan? roundTrip = null) => new()
    {
        Uri = new Uri(connectionString),
        AutomaticRecoveryEnabled = false,
        TopologyRecoveryEnabled = false,
        ClientProvidedName = "elsa-etl",
        RequestedConnectionTimeout = TimeSpan.FromSeconds(30),
        ContinuationTimeout = roundTrip ?? DefaultConfirmTimeout
    };
}
