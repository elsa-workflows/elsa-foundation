using System.Text.Json.Nodes;
using CShells.Lifecycle;
using Elsa.Activities.Http.Options;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed class KernelShellRuntime(KernelShellStorage storage, KernelServing serving, IServiceScopeFactory scopes,
    ILoggerFactory logs, PersistenceScope persistenceScope, BoundScope tenant, KernelRetentionActivation retention) : IAsyncDisposable
{
    private static readonly TimeSpan RetentionStopBudget = TimeSpan.FromSeconds(1);

    private IHostedService? _owner;
    private IAsyncDisposable? _lifetime;
    private HttpClient? _client;
    private KernelShellFoundation? _foundation;
    private readonly CancellationTokenSource _retentionStop = new();
    private Task? _retention;

    public async Task StartAsync(CancellationToken token)
    {
        await storage.InstallAsync(token);
        _foundation = await storage.OpenAsync(token);
        await ServePublicationsAsync(token);

        _client = NativeHttpOptions.CreateClient();
        var expressions = new PortableNativeExpressions(() =>
        {
            var scope = scopes.CreateAsyncScope();
            return (scope.ServiceProvider, scope);
        });
        var model = new NativeExecutionModel(serving.Registry, new NativeExecutionOptions
        {
            Scripts = new ScopedScripts(scopes),
            Http = new NativeHttpOptions { Client = _client },
            Expressions = expressions,
            SelfSends = serving.Capabilities.SelfSends,
            UseStructuralJournal = Foundation.Reads is IActorSequencedJournalReads { SupportsSequencedJournal: true },
            ServedWorkflow = serving.ServedWorkflow,
            ResolveWorkflow = ResolveWorkflowAsync
        });
        (_owner, _lifetime) = storage.CreateOwner(new WorkflowActor([model]), logs, retention.Window);
        await _owner.StartAsync(token);
        var pass = new RetentionPass(Foundation.Reads, WorkflowActor.ActorType, retention.Window, Foundation.AdmitRetentionDelete);
        var worker = new KernelRetentionWorker(pass, tenant,
            token => retention.InitializeAsync(Foundation.RetentionPolicies, Foundation.Reads, token), retention.Unavailable, logs.CreateLogger<KernelRetentionWorker>());
        _retention = Task.Run(() => worker.RunAsync(_retentionStop.Token));
    }

    public async Task StopAsync(CancellationToken token)
    {
        using var retentionDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        retentionDeadline.CancelAfter(RetentionStopBudget);
        try { await StopRetentionAsync(retentionDeadline.Token); }
        finally
        {
            if (_owner is not null) await _owner.StopAsync(token);
        }
    }

    private async Task<bool> StopRetentionAsync(CancellationToken token)
    {
        if (_retention is null) return true;
        try
        {
            await _retentionStop.CancelAsync().WaitAsync(token);
            await _retention.WaitAsync(token);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logs.CreateLogger<KernelShellRuntime>().LogWarning("Kernel retention did not stop within the shell's shutdown deadline.");
            return false;
        }
    }

    public ValueTask<IIngress> IngressAsync(CancellationToken token) => ValueTask.FromResult(Foundation.Ingress);

    public ValueTask<IActorReads> ReadAsync(CancellationToken token) => ValueTask.FromResult(Foundation.Reads);

    private KernelShellFoundation Foundation => _foundation ??
        throw new InvalidOperationException("The kernel shell has not opened its Actor Foundation.");

    private async Task ServePublicationsAsync(CancellationToken token)
    {
        await using var scope = await scopes.CreateAsyncScopeAsync(persistenceScope);
        if (scope.ServiceProvider.GetRequiredService<IWorkflowActivationCoordinator>() is not KernelActivationCoordinator coordinator)
            throw new InvalidOperationException("The kernel shell requires the kernel publication coordinator.");
        var results = await coordinator.ServeActiveSlotsAsync(token);
        var failures = results.Where(result => !result.Succeeded).ToArray();
        var logger = logs.CreateLogger<KernelShellRuntime>();
        foreach (var result in failures)
            logger.LogError("Definition {DefinitionId} slot {SlotName} did not resume serving on the kernel: {Diagnostic}",
                result.Slot.WorkflowDefinitionId, result.Slot.SlotName, result.Diagnostic);
        if (failures.Length > 0)
            throw new InvalidOperationException("The kernel shell could not serve all active publications. See the host log for the affected slots.");
    }

    private async ValueTask<Workflow?> ResolveWorkflowAsync(string artifactId, CancellationToken token)
    {
        await using var scope = await scopes.CreateAsyncScopeAsync(persistenceScope);
        return await NativeExecutableMapper.MapStoredAsync(scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>(), artifactId, token);
    }

    public async ValueTask DisposeAsync()
    {
        using var stop = new CancellationTokenSource(RetentionStopBudget);
        try
        {
            if (await StopRetentionAsync(stop.Token)) _retentionStop.Dispose();
        }
        finally
        {
            try
            {
                if (_lifetime is not null) await _lifetime.DisposeAsync();
            }
            finally { _client?.Dispose(); }
        }
    }

    private sealed class ScopedScripts(IServiceScopeFactory scopes) : INativeScripts
    {
        public async ValueTask<JsonNode?> EvaluateAsync(string script, JsonObject arguments,
            CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            var evaluator = scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>();
            return await new JintNativeScripts(evaluator).EvaluateAsync(script, arguments, cancellationToken);
        }
    }
}

internal sealed class PrepareKernelShell(IShell shell, IShellRegistry registry, IOptions<HttpEndpointOptions> http,
    ILogger<PrepareKernelShell> logger) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        const string defaultShell = "default";
        if (!StringComparer.Ordinal.Equals(shell.Descriptor.Name, defaultShell))
            throw new InvalidOperationException("Kernel hosting is available on the default shell only.");
        if (registry.GetActive(defaultShell) is { } active && active.Descriptor.Generation != shell.Descriptor.Generation)
        {
            logger.LogWarning("Kernel hosting requires a host restart to replace the active shell {ShellName}.", defaultShell);
            throw new InvalidOperationException("Kernel hosting requires a host restart to replace the active shell.");
        }
        _ = WorkflowsRuntimeKernelHostingFeature.HttpBasePath(http.Value);
        return Task.CompletedTask;
    }
}

internal sealed class StartKernelShell(KernelShellRuntime runtime) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken) => runtime.StartAsync(cancellationToken);
}

internal sealed class StopKernelShell(KernelShellRuntime runtime) : IShellTerminator
{
    public Task TerminateAsync(CancellationToken cancellationToken) => runtime.StopAsync(cancellationToken);
}
