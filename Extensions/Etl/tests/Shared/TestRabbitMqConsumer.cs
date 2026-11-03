using System.Text;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests.Support;

/// <summary>
/// A queue stand-in for spec 1201: receiving takes messages from the front without acknowledging them; a receipt either
/// acknowledges them or, disposed unacknowledged, returns them to the front in order, as a broker requeues them. Every step
/// is logged so tests can check the acknowledgement happened after the preparation record.
/// </summary>
internal sealed class TestRabbitMqConsumer : IEtlRabbitMqConsumer
{
    private readonly Lock _gate = new();
    private readonly List<byte[]> _ready = [];
    private readonly Queue<Exception> _script = new();
    private readonly List<string> _log = [];

    public TestRabbitMqConsumer(params string[] messages) => _ready.AddRange(messages.Select(Encoding.UTF8.GetBytes));

    public IReadOnlyList<string> Ready { get { lock (_gate) return _ready.Select(Encoding.UTF8.GetString).ToArray(); } }
    public IReadOnlyList<string> Log { get { lock (_gate) return _log.ToArray(); } }
    public int Acknowledged { get; private set; }

    /// <summary>When set, acknowledging throws it, as a connection lost before the acknowledgement does.</summary>
    public Exception? AcknowledgementFailure { get; set; }

    /// <summary>Throws the exception on the next receive.</summary>
    public TestRabbitMqConsumer Then(Exception exception)
    {
        lock (_gate) _script.Enqueue(exception);
        return this;
    }

    /// <summary>Records a step in the log, for example the store's preparation record.</summary>
    public void Note(string step)
    {
        lock (_gate) _log.Add(step);
    }

    public async ValueTask<IEtlRabbitMqReceipt> ReceiveAsync(string connectionString, string queue, int maxMessages,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<bool>> accept, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Exception? scripted;
        lock (_gate) scripted = _script.Count > 0 ? _script.Dequeue() : null;
        if (scripted is not null) throw scripted;
        var taken = new List<byte[]>();
        while (taken.Count < maxMessages)
        {
            byte[] next;
            lock (_gate)
            {
                if (_ready.Count == 0) break;
                next = _ready[0];
                _ready.RemoveAt(0);
            }
            if (!await accept(next, cancellationToken))
            {
                lock (_gate) _ready.Insert(0, next);
                break;
            }
            taken.Add(next);
        }
        lock (_gate) _log.Add($"receive {taken.Count}");
        return new Receipt(this, taken);
    }

    public EtlRabbitMqErrorKind Classify(Exception exception) =>
        exception is IOException or TimeoutException ? EtlRabbitMqErrorKind.Transient : EtlRabbitMqErrorKind.Rejected;

    private sealed class Receipt(TestRabbitMqConsumer owner, List<byte[]> taken) : IEtlRabbitMqReceipt
    {
        private bool _settled;

        public int Count => taken.Count;

        public ValueTask AcknowledgeAsync(CancellationToken cancellationToken)
        {
            // As the driver does, a cancelled token abandons the acknowledgement.
            cancellationToken.ThrowIfCancellationRequested();
            if (owner.AcknowledgementFailure is { } failure)
                throw failure;
            lock (owner._gate)
            {
                _settled = true;
                owner.Acknowledged += taken.Count;
                owner._log.Add($"ack {taken.Count}");
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            lock (owner._gate)
            {
                if (_settled) return ValueTask.CompletedTask;
                _settled = true;
                owner._ready.InsertRange(0, taken);
                owner._log.Add($"requeue {taken.Count}");
            }
            return ValueTask.CompletedTask;
        }
    }
}
