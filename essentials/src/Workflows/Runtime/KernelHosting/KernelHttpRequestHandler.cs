using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Api.AspNetCore;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Contract = Kernel.Foundation.Contracts;
using NativeHttpRequest = Kernel.Native.HttpRequest;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Adapts ASP.NET Core workflow HTTP requests to the Foundation contract.</summary>
public sealed class KernelHttpRequestHandler(KernelServing serving,
    Func<CancellationToken, ValueTask<Contract.IIngress>> openIngress,
    Func<CancellationToken, ValueTask<long>> readClock,
    Contract.BoundScope tenant, string basePath, TimeSpan timeout, long maxRequestBodyBytes, bool enforceRequestTimeout = false)
{
    /// <summary>Serves a published workflow HTTP endpoint with the actor foundation's held reply.</summary>
    public async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        var clientAborted = context.RequestAborted;
        // Match on the path as sent, before reading a body or starting a route timeout.
        var sent = SentPath(context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? request.Path.ToUriComponent());
        var onBase = sent.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) &&
                     (sent.Length == basePath.Length || sent[basePath.Length] == '/');
        var route = onBase ? serving.MatchHttp(request.Method, sent[basePath.Length..]) : null;
        if (route is not { } match)
        {
            await WriteAsync(context, KernelHttpEdge.NotFound, clientAborted);
            return;
        }

        if (!enforceRequestTimeout)
        {
            await WriteAsync(context, await AnswerAsync(context, match.Route, match.Slot, null, clientAborted), clientAborted);
            return;
        }

        // The production timeout covers reading the request, waiting for the actor and delivering the response.
        var matchedAt = Stopwatch.GetTimestamp();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(clientAborted);
        deadline.CancelAfter(timeout);
        context.RequestAborted = deadline.Token;
        try
        {
            var answer = await AnswerAsync(context, match.Route, match.Slot, matchedAt, deadline.Token);
            await WriteAsync(context, answer, deadline.Token);
        }
        catch (OperationCanceledException) when (clientAborted.IsCancellationRequested)
        {
            // A disconnected client receives no synthetic response.
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested)
        {
            await FinishTimeoutAsync(context, clientAborted, error);
        }
        catch (RequestDeadlineElapsed error)
        {
            await FinishTimeoutAsync(context, clientAborted, error);
        }
        finally
        {
            context.RequestAborted = clientAborted;
        }
    }

    private static async Task WriteAsync(HttpContext context, KernelHttpAnswer answer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var response = context.Response;
        foreach (var (name, value) in answer.Headers) response.Headers[name] = value;
        response.StatusCode = answer.StatusCode;
        if (answer.Body is null) return;
        response.ContentType = answer.ContentType;
        await response.WriteAsync(answer.Body, token);
    }

    private static async Task FinishTimeoutAsync(HttpContext context, CancellationToken clientAborted, Exception cause)
    {
        if (clientAborted.IsCancellationRequested) return;

        context.RequestAborted = clientAborted;
        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }

        context.Response.Clear();
        var fault = new TimeoutException("The HTTP endpoint request timeout elapsed before the response completed.", cause);
        var handleTimeoutAsync = context.RequestServices?.GetService<WorkflowHttpEndpointSettings>()?.HandleTimeoutAsync;
        if (handleTimeoutAsync is null)
        {
            context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
            return;
        }

        try { await handleTimeoutAsync(context, fault, clientAborted); }
        catch (OperationCanceledException) when (clientAborted.IsCancellationRequested) { }
    }

    private async Task<KernelHttpAnswer> AnswerAsync(HttpContext context, NativeHttpRouteMatch route, KernelServedSlot? slot,
        long? matchedAt, CancellationToken token)
    {
        var request = context.Request;
        IReadOnlyDictionary<string, string> query, headers;
        try { query = HttpFields.Query(QueryPairs(request.QueryString.Value)); }
        catch (HttpFieldsException refused) { return Plain(refused.OverBound ? 414 : 400, refused.Message); }
        try { headers = HttpFields.RequestHeaders(request.Headers.SelectMany(field => field.Value.Select(value => KeyValuePair.Create(field.Key, value ?? "")))); }
        catch (HttpFieldsException refused) { return Plain(refused.OverBound ? 431 : 400, refused.Message); }

        JsonNode? body = null;
        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            if (request.ContentLength > maxRequestBodyBytes) return TooLarge();
            var read = await ReadBodyAsync(request.Body, token);
            if (read is null) return TooLarge();
            if (read.Length > 0)
            {
                // A text body is the string it holds, as the HttpEndpoint's ParsedContent reads one; a JSON body, or one with no
                // content type, is parsed; any other body is not one this edge reads.
                var type = MediaTypeHeaderValue.TryParse(request.ContentType, out var media) ? media : null;
                if (type?.MediaType is { } mediaType && mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                {
                    // The charsets .NET ships (UTF-8, UTF-16, UTF-32, ASCII, Latin-1); UTF-8 without one.
                    var charset = type.CharSet?.Trim('"');
                    if (EncodingOf(charset) is not { } encoding) return Plain(415, $"this endpoint does not read text in the charset {charset}");
                    try { body = JsonValue.Create(encoding.GetString(read)); }
                    catch (DecoderFallbackException) { return Plain(400, $"the request body is not text in {encoding.WebName}"); }
                }
                else if (type?.MediaType is null || IsJson(type.MediaType))
                {
                    try { body = JsonNode.Parse(read); }
                    catch (JsonException) { return Plain(400, "the request body is not JSON"); }
                }
                else return Plain(415, $"this endpoint reads JSON and text bodies, not {type.MediaType}");
            }
        }

        var message = new NativeHttpRequest(request.Method, request.Path, body) { Query = query, Headers = headers };
        Contract.Target target;
        string kind;
        string? payload;
        bool respondBeforeCommit;
        var selected = serving.ProtectsHttpStarts && slot is { Revision: > 0 } ? slot : null;
        var publication = selected?.HttpPublication;
        if (selected is not null && publication is null) return KernelHttpEdge.NotFound;
        try
        {
            if (KernelHttpEdge.SelectStart(serving, route, message, selected) is { } start)
                (target, kind, payload, respondBeforeCommit) =
                    (new Contract.Target.Start(WorkflowActor.Start(start.Start, publication)), Contract.Kinds.Start, null, start.RespondBeforeCommit);
            else if (KernelHttpEdge.Wait(serving, route, message) is { } wait)
                (target, kind, payload, respondBeforeCommit) = (new Contract.Target.Event(wait.Key), Contract.Kinds.Event, wait.Payload, wait.RespondBeforeCommit);
            else return KernelHttpEdge.NotFound;
        }
        catch (HttpFieldsException refused) { return Plain(refused.OverBound ? 414 : 400, refused.Message); }

        var ingress = await openIngress(token);
        var now = await readClock(token);
        var remaining = matchedAt is { } since ? timeout - Stopwatch.GetElapsedTime(since) : timeout;
        if (remaining <= TimeSpan.Zero) throw new RequestDeadlineElapsed();
        var milliseconds = matchedAt is null ? remaining.TotalMilliseconds : Math.Ceiling(remaining.TotalMilliseconds);
        var deadline = now + (long)milliseconds;
        // Each HTTP request is its own operation. Its id carries no caller data or route values.
        var reply = await ingress.RequestAsync(tenant, target, Guid.NewGuid().ToString("N"), kind, payload, null,
            new Contract.RequestOptions(null, deadline, respondBeforeCommit), token);
        if (matchedAt is not null && reply.Error == "timeout") throw new RequestDeadlineElapsed();
        if (publication is not null && reply.RefusalReason == Contract.RefusalReason.TargetMismatch)
            return KernelHttpEdge.NotFound;
        return KernelHttpEdge.Answer(reply);
    }

    private sealed class RequestDeadlineElapsed : TimeoutException { }

    private static KernelHttpAnswer Plain(int status, string text) =>
        new(status, "text/plain; charset=utf-8", text, new Dictionary<string, string>());

    private KernelHttpAnswer TooLarge() => Plain(413, $"the request body is over {maxRequestBodyBytes:N0} bytes");

    // The body, or null when it is over the bound: no more than one byte past it is read.
    private async Task<byte[]?> ReadBodyAsync(Stream source, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (await source.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, maxRequestBodyBytes - buffer.Length + 1)), token) is var count and > 0)
        {
            buffer.Write(chunk, 0, count);
            if (buffer.Length > maxRequestBodyBytes) return null;
        }
        return buffer.ToArray();
    }

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static bool IsJson(string mediaType) =>
        mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);

    // The strict decoder of a charset this runtime ships, strict UTF-8 without one, or null for one it does not know.
    internal static Encoding? EncodingOf(string? charset)
    {
        if (string.IsNullOrEmpty(charset)) return StrictUtf8;
        try { return Encoding.GetEncoding(charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
        catch (ArgumentException) { return null; }
    }

    // The path of the request target as the client sent it: without the scheme and authority of an absolute-form target
    // and without the query, and with the literal '.' and '..' segments removed as Kestrel removes them.
    private static string SentPath(string target)
    {
        var path = target.Split('?', 2)[0];
        if (path is not ['/', ..] && path.IndexOf("://", StringComparison.Ordinal) is var scheme and >= 0)
            path = path.IndexOf('/', scheme + 3) is var start and >= 0 ? path[start..] : "/";
        var kept = new List<string>();
        foreach (var segment in path.Split('/'))
            if (segment == "..") { if (kept.Count > 1) kept.RemoveAt(kept.Count - 1); }
            else if (segment != ".") kept.Add(segment);
        return string.Join('/', kept);
    }

    internal static List<KeyValuePair<string, string>> QueryPairs(string? queryString)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var pair in new Microsoft.AspNetCore.WebUtilities.QueryStringEnumerable(queryString))
            pairs.Add(KeyValuePair.Create(pair.DecodeName().ToString(), pair.DecodeValue().ToString()));
        return pairs;
    }
}
