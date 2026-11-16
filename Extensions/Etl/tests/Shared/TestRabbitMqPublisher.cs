using System.Text;
using System.Text.Json;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests.Support;

/// <summary>One message a <see cref="TestRabbitMqPublisher"/> delivered.</summary>
internal sealed record TestRabbitMqDelivery(string Exchange, string RoutingKey, string MessageId,
    Dictionary<string, string> Headers, string Body);

/// <summary>
/// A broker stand-in for spec 1200: it records every delivered message, optionally appending it to a JSON Lines journal that
/// other processes read, and plays scripted failures in order before delivering normally.
/// </summary>
/// <remarks>
/// A scripted <see cref="EtlRabbitMqPublishOutcome.Unroutable"/> or <see cref="EtlRabbitMqPublishOutcome.Nacked"/> delivers
/// the batch's first message and then ends, as a broker does when it refuses a later one. <see cref="IOException"/> and
/// <see cref="TimeoutException"/> are transient; anything else is rejected.
/// </remarks>
internal sealed class TestRabbitMqPublisher(string? journalPath = null) : IEtlRabbitMqPublisher
{
    private readonly Lock _gate = new();
    private readonly Queue<object> _script = new();
    private readonly List<TestRabbitMqDelivery> _deliveries = [];

    public IReadOnlyList<TestRabbitMqDelivery> Deliveries
    {
        get { lock (_gate) return _deliveries.ToArray(); }
    }

    public int Attempts { get; private set; }

    /// <summary>Plays an outcome or throws an exception on the next publication.</summary>
    public TestRabbitMqPublisher Then(object outcomeOrException)
    {
        lock (_gate) _script.Enqueue(outcomeOrException);
        return this;
    }

    public EtlRabbitMqTarget DescribeTarget(string connectionString)
    {
        var uri = new Uri(connectionString);
        if (uri.Scheme is not ("amqp" or "amqps"))
            throw new ArgumentException("An AMQP URI is required.", nameof(connectionString));
        var virtualHost = uri.AbsolutePath.Length <= 1 ? "/" : Uri.UnescapeDataString(uri.AbsolutePath[1..]);
        return new(uri.Host, uri.IsDefaultPort ? 5672 : uri.Port, virtualHost, Uri.UnescapeDataString(uri.UserInfo.Split(':')[0]));
    }

    public ValueTask<EtlRabbitMqPublishOutcome> PublishAsync(string connectionString, string exchange, string routingKey,
        IReadOnlyList<EtlRabbitMqMessage> messages, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Attempts++;
            var step = _script.Count > 0 ? _script.Dequeue() : EtlRabbitMqPublishOutcome.Confirmed;
            if (step is Exception exception)
                throw exception;
            var outcome = (EtlRabbitMqPublishOutcome)step;
            foreach (var message in outcome == EtlRabbitMqPublishOutcome.Confirmed ? messages : messages.Take(1))
                Deliver(new(exchange, routingKey, message.MessageId,
                    message.Headers.ToDictionary(header => header.Key, header => header.Value), Encoding.UTF8.GetString(message.Body.Span)));
            return ValueTask.FromResult(outcome);
        }
    }

    public EtlRabbitMqErrorKind Classify(Exception exception) =>
        exception is IOException or TimeoutException ? EtlRabbitMqErrorKind.Transient : EtlRabbitMqErrorKind.Rejected;

    /// <summary>Reads the deliveries a journal holds.</summary>
    public static IReadOnlyList<TestRabbitMqDelivery> ReadJournal(string journalPath) => File.Exists(journalPath)
        ? File.ReadAllLines(journalPath).Where(line => line.Length > 0)
            .Select(line => JsonSerializer.Deserialize<TestRabbitMqDelivery>(line)!).ToArray()
        : [];

    private void Deliver(TestRabbitMqDelivery delivery)
    {
        _deliveries.Add(delivery);
        if (journalPath is not null)
            File.AppendAllText(journalPath, JsonSerializer.Serialize(delivery) + "\n");
    }
}
