using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Elsa.Activities.Http.Tests;

/// <summary>One request a <see cref="LoopbackHttpServer"/> received: its path and every header it carried.</summary>
internal sealed record RecordedRequest(string Path, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// A bodiless HTTP/1.1 server on the IPv4 loopback address, bound to a port the operating system picks, so a test can
/// observe the requests a real client (automatic redirects included) sends without a fixed port. Each response is
/// empty, carries an optional <c>Location</c>, and closes the connection. Stopped by <see cref="DisposeAsync"/>.
/// </summary>
internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Func<RecordedRequest, (int Status, Uri? Location)> _respond;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly List<Task> _connections = [];
    private readonly Task _accepting;
    private int _disposed;

    public LoopbackHttpServer(Func<RecordedRequest, (int Status, Uri? Location)> respond)
    {
        _respond = respond;
        _listener.Start();
        BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _accepting = AcceptAsync();
    }

    /// <summary>The server's origin, ending with a slash.</summary>
    public Uri BaseAddress { get; }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
                return _requests.ToArray();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting;
        Task[] connections;
        lock (_connections)
            connections = _connections.ToArray();
        await Task.WhenAll(connections);
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
            {
                return; // the listener was stopped while waiting
            }

            lock (_connections)
                _connections.Add(ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(_stop.Token);
            if (requestLine is null)
                return;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
            {
                // A line without a colon is not a header; it is skipped rather than recorded.
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator > 0)
                    headers[line[..separator]] = line[(separator + 1)..].Trim();
            }

            var request = new RecordedRequest(requestLine.Split(' ')[1], headers);
            lock (_requests)
                _requests.Add(request);

            var (status, location) = _respond(request);
            var response = new StringBuilder()
                .Append(FormattableString.Invariant($"HTTP/1.1 {status} Stub\r\nContent-Length: 0\r\nConnection: close\r\n"));
            if (location is not null)
                response.Append("Location: ").Append(location.AbsoluteUri).Append("\r\n");
            response.Append("\r\n");

            await stream.WriteAsync(Encoding.ASCII.GetBytes(response.ToString()), _stop.Token);
            await stream.FlushAsync(_stop.Token);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The client or the test tore the connection down; a test asserts on what was recorded.
        }
    }
}
