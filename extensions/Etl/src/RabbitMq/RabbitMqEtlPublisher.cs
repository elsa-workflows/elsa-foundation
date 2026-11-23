using System.Collections.Concurrent;
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
/// that recovered mid-batch could not tell which messages it confirmed. Each confirm and each protocol round trip is bounded.
/// A broker under a resource alarm blocks publishers without closing their connections and stops reading them: its
/// <c>connection.blocked</c> notice ends the publication at once, and the blocked connection is held, one per target, until
/// the broker unblocks or drops it. While one is held, a publication to that target yields without opening a connection.
/// </remarks>
internal sealed class RabbitMqEtlPublisher : IEtlRabbitMqPublisher
{
    // AMQP reply codes that a retry cannot cure: access refused, not found and precondition failed (a channel error), and
    // not allowed and not implemented (connection errors, such as a virtual host the user may not open, or basic.get on a
    // stream queue). RabbitMQ also answers a connection or channel limit (per virtual host, user or node) with not allowed; a
    // later attempt can pass it, so such a reply is transient.
    private static readonly HashSet<ushort> RejectedReplyCodes = [403, 404, 406, 530, 540];
    private const ushort NotAllowed = 530;
    private const ushort PreconditionFailed = 406;
    // The broker's own limit wording: "connection limit (N) is reached" per virtual host, user or node, "… limit of (N)" for
    // channels, and a limit it could not query. Names are quoted in the reply and removed first, so a virtual host or user
    // whose name says "limit" is not mistaken for one.
    private static readonly Regex LimitReached = new(@"limit \(\d+\) is reached|limit of \(\d+\)|limit cannot be queried",
        RegexOptions.CultureInvariant);
    private static readonly Regex QuotedName = new("'[^']*'", RegexOptions.CultureInvariant);
    private static readonly TimeSpan DefaultConfirmTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _confirmTimeout;
    // A broker under a resource alarm blocks a publishing connection and stops reading it, so it never sees that connection
    // close until the alarm clears. One blocked connection per target is held until the broker unblocks or drops it; while
    // it is held, further publications to that target yield without opening another.
    private readonly ConcurrentDictionary<string, IConnection> _blocked = new(StringComparer.Ordinal);

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
        if (_blocked.ContainsKey(connectionString))
            throw new TimeoutException("The RabbitMQ broker is blocking publishers on this target.");
        var factory = Factory(connectionString, _confirmTimeout);
        var connection = await factory.CreateConnectionAsync(cancellationToken);
        // Not disposed: the broker's notice may arrive after the publication ended.
        var blocked = new CancellationTokenSource();
        // Off the client's dispatch loop: cancelling runs the publication's continuations, which must not hold the loop that
        // delivers the broker's later notices.
        connection.ConnectionBlockedAsync += (_, _) =>
        {
            _ = Task.Run(blocked.Cancel);
            return Task.CompletedTask;
        };
        // The broker sends one unblocked notice per blocked one, possibly before the hold below is taken; remember it.
        var unblocked = 0;
        connection.ConnectionUnblockedAsync += (_, _) =>
        {
            Volatile.Write(ref unblocked, 1);
            return Free(connectionString, connection);
        };
        connection.ConnectionShutdownAsync += (_, _) => Free(connectionString, connection);
        var answered = false;
        try
        {
            var outcome = await PublishAsync(connection, exchange, routingKey, messages, blocked.Token, cancellationToken);
            answered = true;
            return outcome;
        }
        finally
        {
            // The broker answered every publish, so the connection closes normally. A blocked one is held until the broker
            // unblocks it. Otherwise it may be gone: the client's abort and close then wait out fixed and round-trip
            // timeouts, past the caller's cancellation, so the connection is released in the background.
            if (answered)
                await connection.DisposeAsync();
            else if (blocked.IsCancellationRequested && connection.IsOpen && _blocked.TryAdd(connectionString, connection))
            {
                // An unblock or shutdown that arrived before the hold was taken found nothing to free.
                if (!connection.IsOpen || Volatile.Read(ref unblocked) == 1)
                    await Free(connectionString, connection);
            }
            else
                _ = Task.Run(() => ReleaseAsync(connection));
        }
    }

    /// <summary>Releases a held blocked connection once the broker unblocks it or it shuts down.</summary>
    private Task Free(string connectionString, IConnection connection)
    {
        if (_blocked.TryRemove(new KeyValuePair<string, IConnection>(connectionString, connection)))
            _ = Task.Run(() => ReleaseAsync(connection));
        return Task.CompletedTask;
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
        IReadOnlyList<EtlRabbitMqMessage> messages, CancellationToken blocked, CancellationToken cancellationToken)
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
            // A confirm ends at the bound, when the broker blocks the connection, or on the caller's cancellation.
            using var confirm = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, blocked);
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
                throw new TimeoutException(blocked.IsCancellationRequested
                    ? "The RabbitMQ broker is blocking publishers on this target."
                    : "The RabbitMQ broker did not confirm a message in time.", exception);
            }
        }
        await channel.DisposeAsync();
        return EtlRabbitMqPublishOutcome.Confirmed;
    }

    public EtlRabbitMqErrorKind Classify(Exception exception) => ClassifyFailure(exception);

    /// <summary>Classifies a driver failure for the publisher and the consumer alike.</summary>
    internal static EtlRabbitMqErrorKind ClassifyFailure(Exception exception) => exception switch
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
        // A channel closed because messages were held unacknowledged past the broker's consumer_timeout (406) can be retried.
        reason.ReplyCode == PreconditionFailed && reason.ReplyText.Contains("delivery acknowledgement", StringComparison.OrdinalIgnoreCase) &&
        reason.ReplyText.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            ? EtlRabbitMqErrorKind.Transient
            : ClassifyReplyCode(reason);

    private static EtlRabbitMqErrorKind ClassifyReplyCode(ShutdownEventArgs reason) =>
        reason.ReplyCode == NotAllowed && LimitReached.IsMatch(QuotedName.Replace(reason.ReplyText, "''"))
            ? EtlRabbitMqErrorKind.Transient
            : RejectedReplyCodes.Contains(reason.ReplyCode) ? EtlRabbitMqErrorKind.Rejected : EtlRabbitMqErrorKind.Transient;

    internal static ConnectionFactory Factory(string connectionString, TimeSpan? roundTrip = null) => new()
    {
        Uri = new Uri(connectionString),
        AutomaticRecoveryEnabled = false,
        TopologyRecoveryEnabled = false,
        ClientProvidedName = "elsa-etl",
        RequestedConnectionTimeout = TimeSpan.FromSeconds(30),
        ContinuationTimeout = roundTrip ?? DefaultConfirmTimeout
    };
}
