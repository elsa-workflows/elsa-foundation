/// <summary>
/// A TCP proxy in front of the database that counts round trips: a round trip begins each time the client sends after
/// the server last spoke on that connection (or for the first time). Requests the driver pipelines without waiting
/// for an answer count as one. Connection handshakes count too. Admin connections do not go through it.
/// </summary>
sealed class Wire
{
    private readonly System.Net.Sockets.TcpListener _listener = new(System.Net.IPAddress.Loopback, 0);
    private readonly string _host;
    private readonly int _port;
    private long _trips;

    private Wire(string host, int port) => (_host, _port) = (host, port);

    public long Trips => Interlocked.Read(ref _trips);

    public static Wire Start(string host, int port)
    {
        var wire = new Wire(host, port);
        wire._listener.Start();
        _ = Task.Run(wire.AcceptAsync);
        return wire;
    }

    public int LocalPort => ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;

    public static Wire Start(string engine, string connection)
    {
        var match = engine == "sqlserver"
            ? System.Text.RegularExpressions.Regex.Match(connection, @"Server=([^,;]+),(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            : System.Text.RegularExpressions.Regex.Match(connection, @"(?:Host|server)=([^;]+);.*?Port=(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return Start(match.Groups[1].Value, int.Parse(match.Groups[2].Value));
    }

    public string Rewrite(string connection)
    {
        var port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
        return System.Text.RegularExpressions.Regex.Replace(connection, @"(Server=[^,;]+,)\d+|(Port=)\d+", m => (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) + port, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            var client = await _listener.AcceptTcpClientAsync();
            _ = Task.Run(async () =>
            {
                using var server = new System.Net.Sockets.TcpClient();
                using var _ = client;
                client.NoDelay = true;
                server.NoDelay = true;
                await server.ConnectAsync(_host, _port);
                var serverSpoke = 1;     // so the client's first send is a round trip
                async Task Pump(System.Net.Sockets.NetworkStream from, System.Net.Sockets.NetworkStream to, bool fromClient)
                {
                    var buffer = new byte[65536];
                    try
                    {
                        for (int read; (read = await from.ReadAsync(buffer)) > 0;)
                        {
                            if (fromClient) { if (Interlocked.Exchange(ref serverSpoke, 0) == 1) Interlocked.Increment(ref _trips); }
                            else Interlocked.Exchange(ref serverSpoke, 1);
                            await to.WriteAsync(buffer.AsMemory(0, read));
                        }
                    }
                    catch (Exception) { }
                    finally { client.Close(); server.Close(); }
                }
                await Task.WhenAll(Pump(client.GetStream(), server.GetStream(), true), Pump(server.GetStream(), client.GetStream(), false));
            });
        }
    }
}
