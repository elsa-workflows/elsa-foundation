using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Elsa.Etl.Tests.Support;

/// <summary>One real loopback HTTP request captured by the test server.</summary>
internal sealed record EtlHttpLoopbackRequest(string Method, string RawTarget,
    IReadOnlyDictionary<string, string> Headers, byte[] Body)
{
    public override string ToString() => nameof(EtlHttpLoopbackRequest);
}

/// <summary>A deterministic loopback HTTP response, optionally delayed after headers are sent.</summary>
internal sealed class EtlHttpLoopbackResponse
{
    private const int MaximumBodyBytes = 1024 * 1024;

    public EtlHttpLoopbackResponse(int statusCode, string body,
        IReadOnlyDictionary<string, string>? headers = null, TimeSpan? delayBeforeBody = null)
    {
        StatusCode = statusCode;
        Body = Encoding.UTF8.GetBytes(body ?? throw new ArgumentNullException(nameof(body)));
        if (Body.Length > MaximumBodyBytes)
            throw new ArgumentOutOfRangeException(nameof(body));
        Headers = headers is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        DelayBeforeBody = delayBeforeBody;
    }

    public int StatusCode { get; }
    public byte[] Body { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public TimeSpan? DelayBeforeBody { get; }
}

/// <summary>Hosts a real HTTP listener on an ephemeral loopback port for isolated transport tests.</summary>
internal sealed class EtlHttpLoopbackServer : IAsyncDisposable
{
    private const int MaximumRequestBodyBytes = 1024 * 1024;
    private const int MaximumRequestHeaders = 128;
    private const int MaximumRequestHeaderBytes = 32 * 1024;
    private readonly HttpListener _listener;
    private readonly Uri _baseUri;
    private readonly Func<EtlHttpLoopbackRequest, CancellationToken, ValueTask<EtlHttpLoopbackResponse>> _responder;
    private readonly ConcurrentQueue<EtlHttpLoopbackRequest> _requests = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<long, Task> _handlers = new();
    private readonly Task _acceptLoop;
    private long _nextHandlerId;

    private EtlHttpLoopbackServer(HttpListener listener, Uri baseUri,
        Func<EtlHttpLoopbackRequest, CancellationToken, ValueTask<EtlHttpLoopbackResponse>> responder)
    {
        _listener = listener;
        _baseUri = baseUri;
        _responder = responder;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public IReadOnlyList<EtlHttpLoopbackRequest> Requests => _requests.ToArray();

    public static Task<EtlHttpLoopbackServer> StartAsync(
        Func<EtlHttpLoopbackRequest, CancellationToken, ValueTask<EtlHttpLoopbackResponse>> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var baseUri = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
        var listener = new HttpListener();
        listener.Prefixes.Add(baseUri.AbsoluteUri);
        listener.Start();
        return Task.FromResult(new EtlHttpLoopbackServer(listener, baseUri, responder));
    }

    public Uri CreateUri(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return new Uri(_baseUri, target.StartsWith('/') ? target : "/" + target);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            _stop.Cancel();
            _listener.Close();
        }

        try { await _acceptLoop; }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) { }
        var pendingHandlers = _handlers.Values.ToArray();
        if (pendingHandlers.Length > 0)
            await Task.WhenAll(pendingHandlers);
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(_stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
            {
                return;
            }

            var id = Interlocked.Increment(ref _nextHandlerId);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _handlers[id] = completion.Task;
            _ = HandleTrackedAsync(id, context, completion);
        }
    }

    private async Task HandleTrackedAsync(long id, HttpListenerContext context, TaskCompletionSource completion)
    {
        try
        {
            await HandleAsync(context, _stop.Token);
            completion.TrySetResult();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            completion.TrySetResult();
        }
        catch (Exception)
        {
            try { context.Response.Abort(); }
            catch (ObjectDisposedException) { }
            completion.TrySetResult();
        }
        finally
        {
            _handlers.TryRemove(id, out _);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        await using var body = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await context.Request.InputStream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                break;
            if (body.Length > MaximumRequestBodyBytes - read)
                throw new InvalidDataException();
            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (context.Request.Headers.Count > MaximumRequestHeaders)
            throw new InvalidDataException();
        foreach (var name in context.Request.Headers.AllKeys)
        {
            if (name is not null)
                headers[name] = context.Request.Headers.GetValues(name) is { } values ? string.Join(",", values) : string.Empty;
        }
        if (headers.Sum(pair => pair.Key.Length + pair.Value.Length) > MaximumRequestHeaderBytes)
            throw new InvalidDataException();

        var request = new EtlHttpLoopbackRequest(context.Request.HttpMethod, context.Request.RawUrl ?? "/",
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(headers), body.ToArray());
        _requests.Enqueue(request);
        var response = await _responder(request, cancellationToken);

        context.Response.StatusCode = response.StatusCode;
        context.Response.SendChunked = response.DelayBeforeBody is not null;
        if (!context.Response.SendChunked)
            context.Response.ContentLength64 = response.Body.LongLength;
        foreach (var pair in response.Headers)
        {
            if (pair.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                context.Response.ContentType = pair.Value;
            else if (pair.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))
                context.Response.RedirectLocation = pair.Value;
            else
                context.Response.AppendHeader(pair.Key, pair.Value);
        }

        await context.Response.OutputStream.FlushAsync(cancellationToken);
        if (response.DelayBeforeBody is { } delay && delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
        if (response.Body.Length > 0)
            await context.Response.OutputStream.WriteAsync(response.Body, cancellationToken);
        await context.Response.OutputStream.FlushAsync(cancellationToken);
        context.Response.Close();
    }
}
