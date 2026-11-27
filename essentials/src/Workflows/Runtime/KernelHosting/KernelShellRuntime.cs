using System.Text.Json.Nodes;
using CShells.Lifecycle;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Admission = Kernel.Foundation.Contracts.Admission;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed class KernelShellRuntime(KernelShellStorage storage, KernelServing serving, IServiceScopeFactory scopes,
    ILoggerFactory logs, PersistenceScope persistenceScope, BoundScope tenant, KernelRetentionActivation retention, TimeProvider clock,
    bool protectChildExecutables = false, BoundScope? testTenant = null, KernelEventPublicationRelay? events = null) : IAsyncDisposable
{
    private static readonly TimeSpan MaintenanceStopBudget = TimeSpan.FromSeconds(1);

    private IHostedService? _owner;
    private IAsyncDisposable? _lifetime;
    private HttpClient? _client;
    private KernelShellFoundation? _foundation;
    private IProtectedRootStarts? _publishedStarts;
    private IProtectedRootStarts? _testStarts;
    private readonly CancellationTokenSource _maintenanceStop = new();
    private Task? _maintenance;

    public async Task StartAsync(CancellationToken token)
    {
        var protection = protectChildExecutables || storage.TakesPreparedStarts
            ? new KernelExecutableProtection(scopes, persistenceScope, tenant, serving, storage, clock,
                logs.CreateLogger<KernelExecutableProtection>(), testTenant)
            : null;
        await storage.InstallAsync(token, storage.TakesPreparedStarts ? protection!.ProtectHeldStartAsync : null);
        _foundation = await storage.OpenAsync(token);
        var schedules = await CreateScheduleWorkerAsync(token);
        if (protection is not null) await protection.RequireAsync(token);
        await ServePublicationsAsync(token);
        if (events is not null) await events.RestoreAsync(token);

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
            AuthoredEventStarts = storage.TakesDirectTestRuns || storage.TakesEventStarts,
            ServedWorkflow = serving.ServedWorkflow,
            ResolveWorkflow = ResolveWorkflowAsync
        });
        var auxiliary = schedules is null ? null : new ScheduleActor(schedules.Value.Calendar, schedules.Value.Worker.CreateAcceptor());
        (_owner, _lifetime) = storage.CreateOwner(new WorkflowActor([model]), logs, retention.Window, auxiliary,
            protectChildExecutables ? protection!.ProtectAsync : null);
        await _owner.StartAsync(token);
        if (storage.TakesPreparedStarts) Volatile.Write(ref _publishedStarts, storage.BindPublishedStarts(tenant));
        if (testTenant is not null) Volatile.Write(ref _testStarts, storage.BindPublishedStarts(testTenant));
        var pass = new RetentionPass(Foundation.Reads, WorkflowActor.ActorType, retention.Window, Foundation.AdmitRetentionDelete);
        var worker = new KernelRetentionWorker(pass, tenant,
            token => retention.InitializeAsync(Foundation.RetentionPolicies, token),
            token => retention.RefreshAsync(Foundation.RetentionPolicies, token),
            retention.Unavailable, logs.CreateLogger<KernelRetentionWorker>());
        var maintenanceToken = _maintenanceStop.Token;
        List<Task> maintenance = [Task.Run(() => worker.RunAsync(maintenanceToken))];
        if (schedules is { } managed) maintenance.Add(Task.Run(() => managed.Worker.RunAsync(maintenanceToken)));
        if (protection is not null) maintenance.Add(Task.Run(() => protection.RunAsync(maintenanceToken)));
        if (testTenant is not null)
        {
            var expired = new KernelDirectTestRunRetirement(testTenant, Foundation.Reads, storage.OpenOperatorsAsync, clock,
                logs.CreateLogger<KernelDirectTestRunRetirement>());
            maintenance.Add(Task.Run(() => expired.RunAsync(maintenanceToken)));
        }
        if (events is not null) maintenance.Add(Task.Run(() => events.RunAsync(protection!, maintenanceToken)));
        _maintenance = Task.WhenAll(maintenance);
    }

    public async Task StopAsync(CancellationToken token)
    {
        Volatile.Write(ref _publishedStarts, null);
        Volatile.Write(ref _testStarts, null);
        using var maintenanceDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        maintenanceDeadline.CancelAfter(MaintenanceStopBudget);
        try { await StopMaintenanceAsync(maintenanceDeadline.Token); }
        finally
        {
            if (_owner is not null) await _owner.StopAsync(token);
        }
    }

    private async Task<bool> StopMaintenanceAsync(CancellationToken token)
    {
        if (_maintenance is null) return true;
        try
        {
            await _maintenanceStop.CancelAsync().WaitAsync(token);
            await _maintenance.WaitAsync(token);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logs.CreateLogger<KernelShellRuntime>().LogWarning("Kernel maintenance did not stop within the shell's shutdown deadline.");
            return false;
        }
    }

    public ValueTask<IIngress> IngressAsync(CancellationToken token) => ValueTask.FromResult(Foundation.Ingress);

    public ValueTask<Admission> StartPublishedAsync(string operationId, StartSpec start, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Volatile.Read(ref _publishedStarts) is { } starts
            ? starts.StartAsync(operationId, start, token)
            : ValueTask.FromResult(new Admission(AdmissionOutcome.Refused, Reason: RefusalReason.NodeBusy, RetryAfter: TimeSpan.FromSeconds(1)));
    }

    public ValueTask<Admission> StartDirectTestAsync(string operationId, StartSpec start, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Volatile.Read(ref _testStarts) is { } starts
            ? starts.StartAsync(operationId, start, token)
            : ValueTask.FromResult(new Admission(AdmissionOutcome.Refused, Reason: RefusalReason.NodeBusy, RetryAfter: TimeSpan.FromSeconds(1)));
    }

    public async ValueTask<Reply> StartHeldTestAsync(string operationId, StartSpec start, WorkflowTestRunHttpBudget budget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (testTenant is null || Volatile.Read(ref _testStarts) is null)
            return new(null, null, Error: "lost") { RefusalReason = RefusalReason.NodeBusy, RetryAfter = TimeSpan.FromSeconds(1) };
        var now = await storage.ReadClockAsync(token);
        var remaining = budget.Remaining;
        if (remaining <= TimeSpan.Zero) return new(null, null, Error: "timeout");
        var deadline = checked(now + (long)Math.Ceiling(remaining.TotalMilliseconds));
        return await Foundation.Ingress.RequestAsync(testTenant, new Target.Start(start), operationId, Kinds.Start, null,
            null, new(null, deadline), token);
    }

    public Task<RootStartProof> ConfirmPreparedTestAsync(ActorRef actor, string issuance, CancellationToken token) =>
        testTenant is null ? Task.FromResult(RootStartProof.Unresolved) :
            storage.ConfirmPreparedStartAsync(testTenant, actor, issuance, token);

    public ValueTask<IActorReads> ReadAsync(CancellationToken token) => ValueTask.FromResult(Foundation.Reads);

    public ValueTask<KernelRetentionStatus> ReadRetentionStatusAsync(CancellationToken token) =>
        retention.ReadStatusAsync(Foundation.RetentionPolicies, token);

    private KernelShellFoundation Foundation => _foundation ??
        throw new InvalidOperationException("The kernel shell has not opened its Actor Foundation.");

    private async ValueTask<(KernelScheduleReconciliationWorker Worker, ICronCalendar Calendar)?> CreateScheduleWorkerAsync(CancellationToken token)
    {
        if (!serving.Capabilities.Schedules) return null;
        await using var runtime = await scopes.CreateAsyncScopeAsync(persistenceScope);
        var services = runtime.ServiceProvider;
        _ = services.GetRequiredService<IRuntimeKernelScheduleControlScanStore>();
        _ = services.GetRequiredService<IRuntimeKernelScheduleOccurrenceStore>();
        _ = services.GetRequiredService<IRuntimeKernelScheduleOccurrenceDeliveryStore>();
        _ = services.GetRequiredService<IRuntimeKernelScheduleOccurrenceRetirementStore>();
        var scope = RuntimeKernelScheduleControl.CanonicalScope(new(persistenceScope, tenant.Tenant));
        var provider = await storage.OpenSchedulesAsync(tenant, token);
        var calendar = new CronosCalendar();
        return (new(scopes, scope, tenant, serving.Capabilities, calendar, provider, storage.ReadClockAsync,
            clock, logs.CreateLogger<KernelScheduleReconciliationWorker>()), calendar);
    }

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
        var executable = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(artifactId, token);
        // A stored actor already passed admission. The runtime only rebuilds its execution model here. Published
        // routes still use their strict mapping and separate serving authority.
        return executable is null ? null : NativeExecutableMapper.Map(executable, KernelHostCapabilities.All, definition: null,
            scopedHttpTransport: testTenant is not null);
    }

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _publishedStarts, null);
        Volatile.Write(ref _testStarts, null);
        using var stop = new CancellationTokenSource(MaintenanceStopBudget);
        try
        {
            if (await StopMaintenanceAsync(stop.Token)) _maintenanceStop.Dispose();
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

internal sealed class PrepareKernelShell(IShell shell, IShellRegistry registry, IServiceProvider services,
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
        _ = WorkflowsRuntimeKernelHostingFeature.HttpBasePath(WorkflowsRuntimeKernelHostingFeature.RequireHttpSettings(services));
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
