using System.Net.Sockets;
using System.Text.RegularExpressions;
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
    // The broker's own limit wording: "connection limit (N) is reached" per virtual host, user or node, "… limit of (N)" for
    // channels, and a limit it could not query. Names are quoted in the reply and removed first, so a virtual host or user
    // whose name says "limit" is not mistaken for one.
    private static readonly Regex LimitReached = new(@"limit \(\d+\) is reached|limit of \(\d+\)|limit cannot be queried",
        RegexOptions.CultureInvariant);
    private static readonly Regex QuotedName = new("'[^']*'", RegexOptions.CultureInvariant);
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
        var connection = await factory.CreateConnectionAsync(cancellationToken);
        var answered = false;
        try
        {
            var outcome = await PublishAsync(connection, exchange, routingKey, messages, cancellationToken);
            answered = true;
            return outcome;
        }
        finally
        {
            // The broker answered every publish, so the connection closes normally. Otherwise it may be blocked or gone: the
            // client's abort and close then wait out fixed and round-trip timeouts, past the caller's cancellation, so the
            // connection is released in the background and the attempt yields at once.
            if (answered)
                await connection.DisposeAsync();
            else
                _ = ReleaseAsync(connection);
        }
    }

    private static async Task ReleaseAsync(IConnection connection)
    {
        try
        {
            await connection.AbortAsync();
            await connection.DisposeAsync();
        }
        catch (Exception exception) when (exception is OperationInterruptedException or IOException or SocketException or
                                          TimeoutException or OperationCanceledException or ObjectDisposedException)
        {
            // The connection is already unusable; releasing it is best effort.
        }
    }

    private async ValueTask<EtlRabbitMqPublishOutcome> PublishAsync(IConnection connection, string exchange, string routingKey,
        IReadOnlyList<EtlRabbitMqMessage> messages, CancellationToken cancellationToken)
    {
        var channel = await connection.CreateChannelAsync(
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
                await channel.DisposeAsync();
                return exception.IsReturn ? EtlRabbitMqPublishOutcome.Unroutable : EtlRabbitMqPublishOutcome.Nacked;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The RabbitMQ broker did not confirm a message in time.", exception);
            }
        }
        await channel.DisposeAsync();
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
        reason.ReplyCode == NotAllowed && LimitReached.IsMatch(QuotedName.Replace(reason.ReplyText, "''"))
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
