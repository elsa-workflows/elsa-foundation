using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Api.AspNetCore;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Native;
using Microsoft.AspNetCore.Http.Features;
using Contract = Kernel.Foundation.Contracts;
using NativeHttpRequest = Kernel.Native.HttpRequest;
using WorkflowActor = Kernel.WorkflowActor.WorkflowActor;

namespace Elsa.Workbench.CorePreview;

/// <summary>
/// The Core Preview's HTTP route host: with publications on, a request under <c>Elsa:CorePreview:HttpBasePath</c> (default
/// <c>/workflows/http</c>, the address Studio shows for an HttpEndpoint, which must be the default shell's HTTP base path)
/// starts the workflow its route serves on the kernel and holds the caller for its response, up to
/// <c>Elsa:CorePreview:HttpTimeout</c> (default 30 s). It is mapped on the root host, apart from the shells; on the kernel
/// shell the old core's HTTP endpoint middleware passes these paths on (<see cref="WorkflowsRuntimeKernelFeature"/>). Only
/// start routes are served: a wait route, or no route, is not found. The route is matched before the body is read; a body
/// over <c>Elsa:CorePreview:HttpMaxRequestBodyBytes</c> (default 1 MiB, as on the old core) is refused with 413.
/// </summary>
internal sealed class KernelHttpEdgeHost(KernelServing serving, Func<KernelDb> open, PostgresDirectRequests requests,
    Contract.BoundScope tenant, string basePath, TimeSpan timeout, long maxRequestBodyBytes)
{
    internal const string BasePathKey = "Elsa:CorePreview:HttpBasePath";
    internal const string TimeoutKey = "Elsa:CorePreview:HttpTimeout";
    internal const string MaxRequestBodyBytesKey = "Elsa:CorePreview:HttpMaxRequestBodyBytes";
    internal const string DefaultBasePath = "/workflows/http";
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    internal const long DefaultMaxRequestBodyBytes = 1024 * 1024;

    private readonly SemaphoreSlim _opening = new(1, 1);
    private PostgresPreviewFoundation? _foundation;

    internal string BasePath { get; } = basePath;

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        if (endpoints.ServiceProvider.GetService<KernelHttpEdgeHost>() is not { } edge) return;
        endpoints.Map(edge.BasePath + "/{**path}", edge.HandleAsync)
            .WithHostOwner("Elsa.Workbench")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .AllowPublic("kernel workflow HTTP endpoints", "Starts the published workflow an HTTP endpoint route serves on the Core Preview kernel and answers with its response.");
    }

    private async Task HandleAsync(HttpContext context)
    {
        var token = context.RequestAborted;
        var answer = await AnswerAsync(context, token);
        var response = context.Response;
        foreach (var (name, value) in answer.Headers) response.Headers[name] = value;
        response.StatusCode = answer.StatusCode;
        if (answer.Body is null) return;
        response.ContentType = answer.ContentType;
        await response.WriteAsync(answer.Body, token);
    }

    private async Task<KernelHttpAnswer> AnswerAsync(HttpContext context, CancellationToken token)
    {
        var request = context.Request;
        // The route is matched on the path as the client sent it, relative to the base path, so the matcher decodes it once.
        // It is matched first: a path no route serves reads no body.
        var sent = SentPath(context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? request.Path.ToUriComponent());
        if (!sent.StartsWith(BasePath, StringComparison.OrdinalIgnoreCase)) return KernelHttpEdge.NotFound;
        if (KernelHttpEdge.Route(serving, request.Method, sent[BasePath.Length..]) is not { } route) return KernelHttpEdge.NotFound;

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
        if (KernelHttpEdge.Start(serving, route, message) is not { } start) return KernelHttpEdge.NotFound;

        var foundation = await FoundationAsync(token);
        var deadline = await requests.ReadClockAsync(token) + (long)timeout.TotalMilliseconds;
        // Each request is its own operation: an HTTP edge picks a fresh id, so a retried request is a new start.
        var reply = await foundation.RequestAsync(tenant, new Contract.Target.Start(start.Spec), Guid.NewGuid().ToString("N"),
            Contract.Kinds.Start, null, null, new Contract.RequestOptions(null, deadline, start.RespondBeforeCommit), token);
        return KernelHttpEdge.Answer(reply);
    }

    /// <summary>The foundation the edge requests through, opened once when first needed; the kernel's run reads use it too.</summary>
    internal async ValueTask<PostgresPreviewFoundation> FoundationAsync(CancellationToken token)
    {
        if (Volatile.Read(ref _foundation) is { } opened) return opened;
        await _opening.WaitAsync(token);
        try
        {
            return _foundation ??= await PostgresPreviewHosting.OpenFoundationAsync(open, WorkflowActor.ActorType, requests, cancellationToken: token);
        }
        finally { _opening.Release(); }
    }

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

    private static bool IsJson(string mediaType) =>
        mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);

    // The strict decoder of a charset this runtime ships, strict UTF-8 without one, or null for one it does not know.
    private static Encoding? EncodingOf(string? charset)
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

    private static List<KeyValuePair<string, string>> QueryPairs(string? queryString)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var pair in new Microsoft.AspNetCore.WebUtilities.QueryStringEnumerable(queryString))
            pairs.Add(KeyValuePair.Create(pair.DecodeName().ToString(), pair.DecodeValue().ToString()));
        return pairs;
    }

    /// <summary>The base path as configured: absolute, without a trailing slash, never the root.</summary>
    internal static string NormalizeBasePath(string? configured)
    {
        var path = "/" + (string.IsNullOrWhiteSpace(configured) ? DefaultBasePath : configured).Trim().Trim('/');
        return path == "/" ? throw new InvalidOperationException($"{BasePathKey} must name a path below the root.") : path;
    }
}
