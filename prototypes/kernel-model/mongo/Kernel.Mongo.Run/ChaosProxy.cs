using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Kernel.Mongo.Run;

/// <summary>
/// A TCP proxy between the workers' client and the server that can cut a connection in the middle of whatever it
/// carries: a request on its way, an answer on its way back, a transaction between two statements. MongoDB has no
/// pg_terminate_backend; this is the wire-level counterpart.
/// </summary>
public sealed class ChaosProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _host;
    private readonly int _port;
    private readonly ConcurrentDictionary<int, Pair> _pairs = new();
    private readonly CancellationTokenSource _stop = new();
    private int _next;

    private sealed class Pair(TcpClient client, TcpClient server)
    {
        public long LastTraffic = Environment.TickCount64;
        public void Cut()
        {
            // An abortive close (RST), as when a peer or the network goes away.
            try { client.Client.LingerState = new LingerOption(true, 0); client.Close(); } catch (Exception) { }
            try { server.Client.LingerState = new LingerOption(true, 0); server.Close(); } catch (Exception) { }
        }
        public TcpClient Client => client;
        public TcpClient Server => server;
    }

    public ChaosProxy(string host, int port)
    {
        (_host, _port) = (host, port);
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) { return; }
            _ = Task.Run(async () =>
            {
                var server = new TcpClient { NoDelay = true };
                client.NoDelay = true;
                try { await server.ConnectAsync(_host, _port); }
                catch (Exception) { client.Close(); return; }
                var id = Interlocked.Increment(ref _next);
                var pair = new Pair(client, server);
                _pairs[id] = pair;
                await Task.WhenAny(Pump(client.GetStream(), server.GetStream(), pair), Pump(server.GetStream(), client.GetStream(), pair));
                pair.Cut();
                _pairs.TryRemove(id, out _);
            });
        }
    }

    private static async Task Pump(NetworkStream from, NetworkStream to, Pair pair)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            for (int read; (read = await from.ReadAsync(buffer)) > 0;)
            {
                pair.LastTraffic = Environment.TickCount64;
                await to.WriteAsync(buffer.AsMemory(0, read));
            }
        }
        catch (Exception) { }
    }

    /// <summary>Cut one connection that carried traffic in the last <paramref name="withinMs"/> milliseconds. Returns how many were cut.</summary>
    public int CutBusy(Random random, int withinMs = 30)
    {
        var now = Environment.TickCount64;
        var busy = _pairs.Values.Where(p => now - p.LastTraffic <= withinMs).ToList();
        if (busy.Count == 0) return 0;
        busy[random.Next(busy.Count)].Cut();
        return 1;
    }

    public ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        foreach (var pair in _pairs.Values) pair.Cut();
        return default;
    }
}
