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
    private long _protocolRequests;
    private long _flushes, _executes, _limitedExecutes, _namedExecutes;
    private bool _postgres;

    private Wire(string host, int port) => (_host, _port) = (host, port);

    public long Trips => Interlocked.Read(ref _trips);
    // PostgreSQL framing is independent of TCP segmentation. Sync and simple Query each end
    // a protocol group, including a BEGIN pipelined ahead of another group; this is not RTT.
    public long ProtocolRequests => Interlocked.Read(ref _protocolRequests);
    public long Flushes=>Interlocked.Read(ref _flushes);
    public long Executes=>Interlocked.Read(ref _executes);
    public long LimitedExecutes=>Interlocked.Read(ref _limitedExecutes);
    public long NamedExecutes=>Interlocked.Read(ref _namedExecutes);

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
        var wire=Start(match.Groups[1].Value, int.Parse(match.Groups[2].Value));
        wire._postgres=engine=="postgres";
        return wire;
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
                var frames = _postgres ? new PgFrames((kind,limit,named)=>
                {
                    if(kind is (byte)'S' or (byte)'Q')Interlocked.Increment(ref _protocolRequests);
                    if(kind is (byte)'H')Interlocked.Increment(ref _flushes);
                    if(kind is (byte)'E')
                    {
                        Interlocked.Increment(ref _executes);
                        if(limit!=0)Interlocked.Increment(ref _limitedExecutes);
                        if(named)Interlocked.Increment(ref _namedExecutes);
                    }
                }) : null;
                async Task Pump(System.Net.Sockets.NetworkStream from, System.Net.Sockets.NetworkStream to, bool fromClient)
                {
                    var buffer = new byte[65536];
                    try
                    {
                        for (int read; (read = await from.ReadAsync(buffer)) > 0;)
                        {
                            if (fromClient) { frames?.Add(buffer.AsSpan(0,read)); if (Interlocked.Exchange(ref serverSpoke, 0) == 1) Interlocked.Increment(ref _trips); }
                            else Interlocked.Exchange(ref serverSpoke, 1);
                            await to.WriteAsync(buffer.AsMemory(0, read));
                        }
                    }
                    catch (InvalidOperationException e) { Console.WriteLine("PostgreSQL frame meter refused input: "+e.Message); }
                    catch (Exception) { }
                    finally { client.Close(); server.Close(); }
                }
                await Task.WhenAll(Pump(client.GetStream(), server.GetStream(), true), Pump(server.GetStream(), client.GetStream(), false));
            });
        }
    }

    private sealed class PgFrames(Action<byte,int,bool> frame)
    {
        private readonly List<byte> _bytes=[];
        private bool _startup=true;
        internal void Add(ReadOnlySpan<byte> bytes)
        {
            foreach(var value in bytes)_bytes.Add(value);
            while(_bytes.Count>=(_startup?4:5))
            {
                var offset=_startup?0:1;
                var length=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_bytes).Slice(offset,4));
                if(length<4 || length>64*1024*1024)throw new InvalidOperationException("invalid plaintext PostgreSQL frame");
                var size=length+offset;if(_bytes.Count<size)return;
                if(_startup)
                {
                    // SSL/GSS negotiation is an untyped request before the actual startup packet.
                    var code=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_bytes).Slice(4,4));
                    if(code is not (80877103 or 80877104))_startup=false;
                }
                else
                {
                    var named=false;var limit=0;
                    if(_bytes[0]==(byte)'E')
                    {
                        var end=_bytes.IndexOf(0,5);named=end>5;
                        if(end<5 || end+5!=size)throw new InvalidOperationException("invalid Execute frame");
                        limit=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_bytes).Slice(end+1,4));
                    }
                    frame(_bytes[0],limit,named);
                }
                _bytes.RemoveRange(0,size);
            }
        }
    }
}
