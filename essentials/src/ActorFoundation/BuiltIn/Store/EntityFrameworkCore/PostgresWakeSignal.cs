using System.Globalization;
using System.Threading.Channels;
using Npgsql;

namespace Kernel.EntityFramework;

/// <summary>
/// A best-effort PostgreSQL transport for the built-in runtime. It sends a virtual partition number after admission
/// has committed. Separate sessions and a bounded queue keep notification failures outside the inbox transaction.
/// </summary>
public sealed class PostgresWakeSignal : IWakeSignal
{
    private const string Topic = "elsa_kernel_wake";
    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(250);
    private readonly int _partitions;
    private readonly string _listener;
    private readonly string _sender;
    private readonly InProcessWakeSignal _received = new();
    private readonly Channel<int> _pending = Channel.CreateBounded<int>(new BoundedChannelOptions(256)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });
    private int _started;

    public PostgresWakeSignal(string connectionString, int partitions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partitions);
        _partitions = partitions;
        var connection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "elsa-kernel-wake-listener",
            Pooling = false,
            Multiplexing = false,
            Enlist = false,
            KeepAlive = 5,
        };
        _listener = connection.ConnectionString;
        connection.ApplicationName = "elsa-kernel-wake-sender";
        _sender = connection.ConnectionString;
    }

    public void Listen(Action<InstanceRef> woken) => _received.Listen(woken);

    public void Send(InstanceRef actor)
    {
        var partition = actor.Partition;
        if ((uint)partition < (uint)_partitions) _pending.Writer.TryWrite(partition);
    }

    /// <summary>Run beside one runtime node and stop with that node.</summary>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("A wake signal belongs to one runtime node.");
        return Task.WhenAll(ReceiveAsync(cancellationToken), PublishAsync(cancellationToken));
    }

    private async Task ReceiveAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_listener);
                connection.Notification += Receive;
                await connection.OpenAsync(stop);
                await using var listen = new NpgsqlCommand("LISTEN " + Topic, connection);
                await listen.ExecuteNonQueryAsync(stop);
                while (!stop.IsCancellationRequested) await connection.WaitAsync(stop);
            }
            catch (Exception error) when (error is NpgsqlException or TimeoutException)
            {
                await Task.Delay(Retry, stop);
            }
        }
    }

    private void Receive(object sender, NpgsqlNotificationEventArgs notice)
    {
        if (notice.Channel == Topic &&
            int.TryParse(notice.Payload, NumberStyles.None, CultureInfo.InvariantCulture, out var partition) &&
            (uint)partition < (uint)_partitions)
        {
            // The built-in runtime uses the reference only to route the hint to a partition.
            _received.Send(InstanceRef.In(partition, "wake"));
        }
    }

    private async Task PublishAsync(CancellationToken stop)
    {
        while (await _pending.Reader.WaitToReadAsync(stop))
        {
            try
            {
                await using var connection = new NpgsqlConnection(_sender);
                await connection.OpenAsync(stop);
                await using var command = new NpgsqlCommand("SELECT pg_notify('" + Topic + "', $1)", connection);
                var payload = new NpgsqlParameter<string> { TypedValue = "" };
                command.Parameters.Add(payload);
                while (_pending.Reader.TryRead(out var partition))
                {
                    payload.TypedValue = partition.ToString(CultureInfo.InvariantCulture);
                    await command.ExecuteNonQueryAsync(stop);
                }
            }
            catch (Exception error) when (error is NpgsqlException or TimeoutException)
            {
                await Task.Delay(Retry, stop);
            }
        }
    }
}
