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
/// <c>/workflows/http</c>, the address Studio shows for an HttpEndpoint) starts the workflow its route serves on the
/// kernel and holds the caller for its response, up to <c>Elsa:CorePreview:HttpTimeout</c> (default 30 s). It is mapped on
/// the root host, apart from the shells; on the kernel shell the old core's HTTP endpoint middleware passes these paths on
/// (<see cref="WorkflowsRuntimeKernelFeature"/>). Only start routes are served: a wait route, or no route, is not found.
/// </summary>
internal sealed class KernelHttpEdgeHost(KernelServing serving, Func<KernelDb> open, PostgresDirectRequests requests,
    Contract.BoundScope tenant, string basePath, TimeSpan timeout)
{
    internal const string BasePathKey = "Elsa:CorePreview:HttpBasePath";
    internal const string TimeoutKey = "Elsa:CorePreview:HttpTimeout";
    internal const string DefaultBasePath = "/workflows/http";
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

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
        JsonNode? body = null;
        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, token);
            if (buffer.Length > 0)
            {
                buffer.Position = 0;
                try { body = await JsonNode.ParseAsync(buffer, cancellationToken: token); }
                catch (JsonException) { return Plain(400, "the request body is not JSON"); }
            }
        }

        NativeHttpRequest message;
        try
        {
            message = new NativeHttpRequest(request.Method, request.Path, body)
            {
                Query = HttpFields.Query(QueryPairs(request.QueryString.Value)),
                Headers = HttpFields.RequestHeaders(request.Headers.SelectMany(field => field.Value.Select(value => KeyValuePair.Create(field.Key, value ?? ""))))
            };
        }
        catch (HttpFieldsException refused) { return Plain(refused.OverBound ? 431 : 400, refused.Message); }

        // The route is matched on the path as the client sent it, relative to the base path, so the matcher decodes it once.
        var sent = SentPath(context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? request.Path.ToUriComponent());
        if (!sent.StartsWith(BasePath, StringComparison.OrdinalIgnoreCase)) return KernelHttpEdge.NotFound;
        if (KernelHttpEdge.Match(serving, message, sent[BasePath.Length..]) is not { } start) return KernelHttpEdge.NotFound;

        var foundation = await FoundationAsync(token);
        var deadline = await requests.ReadClockAsync(token) + (long)timeout.TotalMilliseconds;
        // Each request is its own operation: an HTTP edge picks a fresh id, so a retried request is a new start.
        var reply = await foundation.RequestAsync(tenant, new Contract.Target.Start(start.Spec), Guid.NewGuid().ToString("N"),
            Contract.Kinds.Start, null, null, new Contract.RequestOptions(null, deadline, start.RespondBeforeCommit), token);
        return KernelHttpEdge.Answer(reply);
    }

    private async ValueTask<PostgresPreviewFoundation> FoundationAsync(CancellationToken token)
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
