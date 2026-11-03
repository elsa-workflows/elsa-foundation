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

    public ValueTask<IEtlRabbitMqReceipt> ReceiveAsync(string connectionString, string queue, int maxMessages, long maxBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_script.Count > 0) throw _script.Dequeue();
            var taken = new List<byte[]>();
            long bytes = 0;
            var tooLarge = false;
            while (taken.Count < maxMessages && _ready.Count > 0)
            {
                if (bytes + _ready[0].Length + 1 > maxBytes)
                {
                    tooLarge = taken.Count == 0;
                    break;
                }
                bytes += _ready[0].Length + 1;
                taken.Add(_ready[0]);
                _ready.RemoveAt(0);
            }
            _log.Add($"receive {taken.Count}");
            return ValueTask.FromResult<IEtlRabbitMqReceipt>(new Receipt(this, taken, tooLarge));
        }
    }

    public EtlRabbitMqErrorKind Classify(Exception exception) =>
        exception is IOException or TimeoutException ? EtlRabbitMqErrorKind.Transient : EtlRabbitMqErrorKind.Rejected;

    private sealed class Receipt(TestRabbitMqConsumer owner, List<byte[]> taken, bool tooLarge) : IEtlRabbitMqReceipt
    {
        private bool _settled;

        public IReadOnlyList<ReadOnlyMemory<byte>> Bodies { get; } = taken.Select(body => (ReadOnlyMemory<byte>)body).ToArray();
        public bool FirstMessageTooLarge => tooLarge;

        public ValueTask AcknowledgeAsync(CancellationToken cancellationToken)
        {
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
