using Elsa.Api.AspNetCore;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.KernelHosting;
using Contract = Kernel.Foundation.Contracts;

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
internal sealed class KernelHttpEdgeHost
{
    internal const string BasePathKey = "Elsa:CorePreview:HttpBasePath";
    internal const string TimeoutKey = "Elsa:CorePreview:HttpTimeout";
    internal const string MaxRequestBodyBytesKey = "Elsa:CorePreview:HttpMaxRequestBodyBytes";
    internal const string DefaultBasePath = "/workflows/http";
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    internal const long DefaultMaxRequestBodyBytes = 1024 * 1024;

    private readonly SemaphoreSlim _opening = new(1, 1);
    private readonly TaskCompletionSource _installed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Foundation? _foundation;

    private sealed record Foundation(Contract.IIngress Ingress, Contract.IActorReads Reads);

    private readonly KernelHttpRequestHandler _handler;
    private readonly Func<CancellationToken, ValueTask<(Contract.IIngress Ingress, Contract.IActorReads Reads)>> _openFoundation;

    internal KernelHttpEdgeHost(KernelServing serving,
        Func<CancellationToken, ValueTask<(Contract.IIngress Ingress, Contract.IActorReads Reads)>> openFoundation,
        Func<CancellationToken, ValueTask<long>> readClock,
        Contract.BoundScope tenant, string basePath, TimeSpan timeout, long maxRequestBodyBytes)
    {
        BasePath = basePath;
        _openFoundation = openFoundation;
        _handler = new(serving, IngressAsync, readClock, tenant, basePath, timeout, maxRequestBodyBytes);
    }

    internal string BasePath { get; }

    private async ValueTask<Contract.IIngress> IngressAsync(CancellationToken token) => (await FoundationAsync(token)).Ingress;

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        if (endpoints.ServiceProvider.GetService<KernelHttpEdgeHost>() is not { } edge) return;
        endpoints.Map(edge.BasePath + "/{**path}", edge._handler.HandleAsync)
            .WithHostOwner("Elsa.Workbench")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .AllowPublic("kernel workflow HTTP endpoints", "Starts the published workflow an HTTP endpoint route serves on the Core Preview kernel and answers with its response.");
    }

    /// <summary>The store is installed and the foundation may open.</summary>
    internal void StoreInstalled() => _installed.TrySetResult();

    /// <summary>
    /// The foundation the edge requests through, opened once when first needed; the kernel's run reads use it too. A request
    /// that comes while the host still installs its store waits for the install, so it is not refused by a store not yet there.
    /// </summary>
    internal async ValueTask<Contract.IActorReads> ReadsAsync(CancellationToken token) => (await FoundationAsync(token)).Reads;

    internal Task WaitForStoreAsync(CancellationToken token) => _installed.Task.WaitAsync(token);

    private async ValueTask<Foundation> FoundationAsync(CancellationToken token)
    {
        if (Volatile.Read(ref _foundation) is { } opened) return opened;
        await WaitForStoreAsync(token);
        await _opening.WaitAsync(token);
        try
        {
            if (_foundation is not null) return _foundation;
            var (ingress, reads) = await _openFoundation(token);
            var foundation = new Foundation(ingress, reads);
            Volatile.Write(ref _foundation, foundation);
            return foundation;
        }
        finally { _opening.Release(); }
    }

    /// <summary>The base path as configured: absolute, without a trailing slash, never the root.</summary>
    internal static string NormalizeBasePath(string? configured)
    {
        var path = "/" + (string.IsNullOrWhiteSpace(configured) ? DefaultBasePath : configured).Trim().Trim('/');
        return path == "/" ? throw new InvalidOperationException($"{BasePathKey} must name a path below the root.") : path;
    }
}
