using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Runtime.Hosting;
using Microsoft.Extensions.Hosting;

namespace Kernel.Foundation.Postgres;

/// <summary>Runs one ordinary PostgreSQL preview node for the lifetime of its application host.</summary>
public sealed class PostgresPreviewHostedService(Func<KernelDb> create, string node, IActorType actor,
    Action<string> endProcess, LoopOptions? options = null) : IHostedService, IAsyncDisposable
{
    private readonly object _lifecycle = new();
    private readonly CancellationTokenSource _cancelStartup = new();
    private Task<RuntimeNode>? _startup;
    private Task? _stopRequested;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
        {
            if (_stopRequested is not null)
                throw new InvalidOperationException("The PostgreSQL preview service has stopped.");
            return _startup ??= StartNodeAsync(cancellationToken);
        }
    }

    private async Task<RuntimeNode> StartNodeAsync(CancellationToken cancellationToken)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancelStartup.Token);
        return await PostgresPreviewHosting.StartAsync(create, node, actor, endProcess, options, startup.Token);
    }

    internal RuntimeNode Runtime => _startup is { IsCompletedSuccessfully: true } started
        ? started.Result
        : throw new InvalidOperationException("The PostgreSQL preview service has not started.");

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task<RuntimeNode>? startup;
        Task stopping;
        lock (_lifecycle)
        {
            startup = _startup;
            stopping = _stopRequested ??= _cancelStartup.CancelAsync();
        }
        await stopping;
        if (startup is null) return;

        RuntimeNode runtime;
        try { runtime = await startup; }
        catch (Exception) { return; } // Startup reports its error and the launcher cleans up a partial node.
        await runtime.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        lock (_lifecycle) _cancelStartup.Dispose();
    }
}
