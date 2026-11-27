using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.Foundation.Postgres;

/// <summary>
/// Live cross-node replies, carried through a PostgreSQL listener without storing a response in the kernel.
/// The runtime calls Send only once the reply may be released. A disconnected receiver has no replay to catch up.
/// </summary>
internal sealed class PostgresReplyRelay(Func<KernelDb> create, string node, PostgresReplyFrames frames,
    Action<Reply> receive, Action<Exception>? onFault = null)
{
    private sealed record Outbound(string Node, Reply Reply);

    private readonly Channel<Outbound> _queue = Channel.CreateBounded<Outbound>(
        new BoundedChannelOptions(64) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _published = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _closing;

    internal static async Task<PostgresReplyRelay> OpenAsync(Func<KernelDb> create, string node,
        Action<Reply> receive, Action<Exception>? onFault, CancellationToken token)
    {
        await using var db = create();
        await using var pinned = await EfStore.BeginPinnedRootOperationAsync(db, token);
        var issuer = await PostgresRootAdmission.ReadRootIssuerKeyAsync(db, token);
        try
        {
            if (issuer.Length != 32) throw new StoreFormatException("The live reply carrier needs a valid store issuer.");
            var key = HMACSHA256.HashData(issuer, "elsa/kernel/postgresql/live-replies/v1"u8);
            return new(create, node, new(key), receive, onFault);
        }
        finally { CryptographicOperations.ZeroMemory(issuer); }
    }

    internal Task ReadyAsync(CancellationToken token) => _ready.Task.WaitAsync(token);

    internal void Send(string destination, Reply reply)
    {
        if (!_queue.Writer.TryWrite(new(destination, reply)) && Volatile.Read(ref _closing) == 0)
            Report(new IOException("The live PostgreSQL reply queue is full."));
    }

    internal async Task FlushAsync(CancellationToken kill)
    {
        Complete();
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(kill);
        bound.CancelAfter(TimeSpan.FromSeconds(2));
        try { await _published.Task.WaitAsync(bound.Token); }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            if (!kill.IsCancellationRequested) Report(new TimeoutException("The live PostgreSQL replies did not drain before shutdown."));
        }
    }

    internal async Task RunAsync(CancellationToken token)
    {
        try { await Task.WhenAll(ListenAsync(token), PublishAsync(token)); }
        finally
        {
            Complete();
            while (_queue.Reader.TryRead(out _)) { }
        }
    }

    private void Complete()
    {
        Interlocked.Exchange(ref _closing, 1);
        _queue.Writer.TryComplete();
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var channel = frames.Channel(node);
                await using var db = create();
                var source = (NpgsqlConnection)db.Database.GetDbConnection();
                var settings = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Pooling = false, Multiplexing = false };
                await using var connection = await source.CloneWithAsync(settings.ConnectionString, token);
                await connection.OpenAsync(token);
                using var reader = frames.Open(channel);
                connection.Notification += OnNotification;
                try
                {
                    // Derivation yields only ASCII letters, digits and underscores. LISTEN takes an identifier.
                    await using var listen = new NpgsqlCommand("LISTEN " + channel, connection);
                    await listen.ExecuteNonQueryAsync(token);
                    _ready.TrySetResult();
                    while (!token.IsCancellationRequested) await connection.WaitAsync(token);
                }
                finally { connection.Notification -= OnNotification; }

                void OnNotification(object sender, NpgsqlNotificationEventArgs args)
                {
                    if (!string.Equals(args.Channel, channel, StringComparison.Ordinal)) return;
                    try
                    {
                        if (reader.Read(args.Payload) is { } reply) receive(reply);
                    }
                    catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
                    {
                        Report(new InvalidDataException("The live PostgreSQL reply carrier could not read a message.", error));
                    }
                }
            }
            catch (Exception) when (token.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                if (_ready.TrySetException(error)) return;
                Report(error);
                try { await Task.Delay(TimeSpan.FromMilliseconds(250), token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }
        _ready.TrySetCanceled(token);
    }

    private async Task PublishAsync(CancellationToken token)
    {
        try
        {
            await foreach (var outgoing in _queue.Reader.ReadAllAsync(token))
            {
                try
                {
                    var channel = frames.Channel(outgoing.Node);
                    var payloads = frames.Encode(channel, outgoing.Reply);
                    await using var db = create();
                    var connection = (NpgsqlConnection)db.Database.GetDbConnection();
                    await connection.OpenAsync(token);
                    // Keep all chunks for one reply in one transaction, in send order. The owner checkpoint
                    // never joins this transaction, so sending or losing the reply cannot move its fence.
                    await using var transaction = await connection.BeginTransactionAsync(token);
                    await using var batch = new NpgsqlBatch(connection, transaction);
                    foreach (var payload in payloads)
                    {
                        var command = new NpgsqlBatchCommand("SELECT pg_catalog.pg_notify($1, $2)");
                        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = channel });
                        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = payload });
                        batch.BatchCommands.Add(command);
                    }
                    await batch.ExecuteNonQueryAsync(token);
                    await transaction.CommitAsync(token);
                }
                catch (Exception error)
                {
                    if (!token.IsCancellationRequested) Report(error);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { _published.TrySetResult(); }
    }

    private void Report(Exception error)
    {
        try { onFault?.Invoke(error); }
        catch (Exception) { }
    }
}
