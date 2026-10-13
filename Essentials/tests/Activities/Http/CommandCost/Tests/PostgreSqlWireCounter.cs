using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Elsa.Activities.Http.CommandCost;

/// <summary>What a PostgreSQL client and server exchanged through <see cref="PostgreSqlWireCounter"/>.</summary>
/// <param name="Connections">Physical connections opened.</param>
/// <param name="HandshakeRoundTrips">Round trips spent opening connections, up to the server's first ReadyForQuery.</param>
/// <param name="RoundTrips">
/// Round trips after the handshake: each time the client sent and then waited for the server to answer. Messages the
/// client writes together, such as the pooled-connection reset Npgsql prepends to the next command, share one.
/// </param>
/// <param name="Responses">ReadyForQuery messages after the handshake: one per simple query and per extended-protocol Sync.</param>
/// <param name="Executes">Extended-protocol Execute messages: one per statement the client ran that way.</param>
/// <param name="SimpleQueries">Simple-protocol Query messages, by their text up to the first 40 characters.</param>
internal sealed record PostgreSqlWireCounts(
    int Connections,
    int HandshakeRoundTrips,
    int RoundTrips,
    int Responses,
    int Executes,
    IReadOnlyDictionary<string, int> SimpleQueries)
{
    public static readonly PostgreSqlWireCounts Zero = new(0, 0, 0, 0, 0, new Dictionary<string, int>());

    public int SimpleQueryCount => SimpleQueries.Values.Sum();

    public PostgreSqlWireCounts Plus(PostgreSqlWireCounts other, int sign = 1) => new(
        Connections + sign * other.Connections,
        HandshakeRoundTrips + sign * other.HandshakeRoundTrips,
        RoundTrips + sign * other.RoundTrips,
        Responses + sign * other.Responses,
        Executes + sign * other.Executes,
        SimpleQueries.Keys.Union(other.SimpleQueries.Keys)
            .Select(text => (Text: text, Count: SimpleQueries.GetValueOrDefault(text) + sign * other.SimpleQueries.GetValueOrDefault(text)))
            .Where(entry => entry.Count != 0)
            .ToDictionary(entry => entry.Text, entry => entry.Count));
}

/// <summary>
/// A TCP relay between Npgsql and a PostgreSQL server that reads the protocol messages passing through it, so a
/// measurement counts the round trips a run costs on the network instead of deriving them from the commands. The
/// client must disable TLS, or the relay cannot read the messages. Every connection the measured host opens goes
/// through the relay, so it also sees traffic the EF interceptor does not, such as COMMIT, savepoints and the reset of a
/// pooled connection.
/// </summary>
internal sealed class PostgreSqlWireCounter : IAsyncDisposable
{
    private const int SslRequestCode = 80877103;
    private const int GssEncRequestCode = 80877104;
    private const int CancelRequestCode = 80877102;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _upstreamHost;
    private readonly int _upstreamPort;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _relays = [];
    private readonly Task _accepting;
    private readonly ConcurrentDictionary<string, int> _simpleQueries = new(StringComparer.Ordinal);
    private int _connections;
    private int _handshakeRoundTrips;
    private int _roundTrips;
    private int _responses;
    private int _executes;

    public PostgreSqlWireCounter(string upstreamHost, int upstreamPort)
    {
        _upstreamHost = upstreamHost;
        _upstreamPort = upstreamPort;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public PostgreSqlWireCounts Counts => new(
        Volatile.Read(ref _connections),
        Volatile.Read(ref _handshakeRoundTrips),
        Volatile.Read(ref _roundTrips),
        Volatile.Read(ref _responses),
        Volatile.Read(ref _executes),
        _simpleQueries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();
        try
        {
            await _accepting;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        Task[] relays;
        lock (_relays)
            relays = [.. _relays];
        await Task.WhenAll(relays.Select(relay => relay.ContinueWith(_ => { }, TaskScheduler.Default)));
        _stopping.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            lock (_relays)
                _relays.Add(RelayAsync(client));
        }
    }

    private async Task RelayAsync(TcpClient client)
    {
        using var _ = client;
        using var server = new TcpClient();
        await server.ConnectAsync(_upstreamHost, _upstreamPort, _stopping.Token);
        Interlocked.Increment(ref _connections);
        client.NoDelay = true;
        server.NoDelay = true;

        var connection = new Connection(this);
        await using var clientStream = client.GetStream();
        await using var serverStream = server.GetStream();
        var upstream = PumpAsync(clientStream, serverStream, connection.ReadClient);
        var downstream = PumpAsync(serverStream, clientStream, connection.ReadServer);
        await Task.WhenAny(upstream, downstream);
        client.Close();
        server.Close();
        await Task.WhenAll(upstream.ContinueWith(_ => { }, TaskScheduler.Default), downstream.ContinueWith(_ => { }, TaskScheduler.Default));
    }

    private async Task PumpAsync(NetworkStream from, NetworkStream to, Action<ReadOnlyMemory<byte>> observe)
    {
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await from.ReadAsync(buffer, _stopping.Token);
            if (read == 0)
                return;
            observe(buffer.AsMemory(0, read));
            await to.WriteAsync(buffer.AsMemory(0, read), _stopping.Token);
        }
    }

    /// <summary>
    /// The protocol state of one connection. Messages are counted from the bytes as they pass, whatever TCP segments
    /// carry them. A round trip completes when the server answers after the client sent something; the client never
    /// sends again before that answer, so this counts each wait once however many messages the client wrote together.
    /// </summary>
    private sealed class Connection(PostgreSqlWireCounter counter)
    {
        private readonly MessageReader _client = new(untypedUntilStartup: true);
        private readonly MessageReader _server = new(untypedUntilStartup: false);
        private readonly Lock _gate = new();
        private int _pendingSingleByteAnswers;
        private bool _handshakeDone;
        private bool _awaitingAnswer;

        public void ReadClient(ReadOnlyMemory<byte> bytes)
        {
            lock (_gate)
            {
                _awaitingAnswer = true;
                foreach (var message in _client.Read(bytes.Span))
                {
                    if (message.Type == 0)
                    {
                        // Before the startup message, the server answers a TLS or GSS request with one untyped byte.
                        if (message.UntypedCode is SslRequestCode or GssEncRequestCode)
                            _pendingSingleByteAnswers++;
                        continue;
                    }

                    if (message.Type == (byte)'E')
                        Interlocked.Increment(ref counter._executes);
                    else if (message.Type == (byte)'Q')
                        counter._simpleQueries.AddOrUpdate(message.Text!, 1, (_, count) => count + 1);
                }
            }
        }

        public void ReadServer(ReadOnlyMemory<byte> bytes)
        {
            lock (_gate)
            {
                if (_awaitingAnswer)
                {
                    _awaitingAnswer = false;
                    Interlocked.Increment(ref _handshakeDone ? ref counter._roundTrips : ref counter._handshakeRoundTrips);
                }

                var span = bytes.Span;
                while (_pendingSingleByteAnswers > 0 && span.Length > 0)
                {
                    _pendingSingleByteAnswers--;
                    span = span[1..];
                }

                foreach (var message in _server.Read(span))
                {
                    if (message.Type != (byte)'Z')
                        continue;
                    if (_handshakeDone)
                        Interlocked.Increment(ref counter._responses);
                    _handshakeDone = true;
                }
            }
        }
    }

    private readonly record struct Message(byte Type, int UntypedCode, string? Text);

    /// <summary>
    /// Reassembles length-prefixed protocol messages from a byte stream. Until the startup message, client messages
    /// carry no type byte; every later message has a type byte and a length.
    /// </summary>
    private sealed class MessageReader(bool untypedUntilStartup)
    {
        private readonly List<byte> _pending = [];
        private bool _untyped = untypedUntilStartup;

        public IReadOnlyList<Message> Read(ReadOnlySpan<byte> bytes)
        {
            _pending.AddRange(bytes);
            var messages = new List<Message>();
            var offset = 0;
            while (true)
            {
                var available = _pending.Count - offset;
                if (_untyped)
                {
                    if (available < 8)
                        break;
                    var length = ReadInt32(offset);
                    if (available < length)
                        break;
                    var code = ReadInt32(offset + 4);
                    messages.Add(new Message(0, code, null));
                    offset += length;
                    if (code is not (SslRequestCode or GssEncRequestCode or CancelRequestCode))
                        _untyped = false;
                    continue;
                }

                if (available < 5)
                    break;
                var messageLength = ReadInt32(offset + 1);
                if (available < messageLength + 1)
                    break;
                var type = _pending[offset];
                messages.Add(new Message(type, 0, type == (byte)'Q' ? QueryText(offset + 5, messageLength - 4) : null));
                offset += messageLength + 1;
            }

            _pending.RemoveRange(0, offset);
            return messages;
        }

        private string QueryText(int offset, int length)
        {
            var text = Encoding.UTF8.GetString(_pending.GetRange(offset, Math.Max(0, length - 1)).ToArray());
            return text.Length > 40 ? text[..40] : text;
        }

        private int ReadInt32(int offset)
        {
            Span<byte> value = stackalloc byte[4];
            for (var index = 0; index < 4; index++)
                value[index] = _pending[offset + index];
            return BinaryPrimitives.ReadInt32BigEndian(value);
        }
    }
}
