using System.Text.Json;
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
    private static readonly TimeSpan InlineSettlementBudget = TimeSpan.FromSeconds(5);

    private IHostedService? _owner;
    private IAsyncDisposable? _lifetime;
    private HttpClient? _client;
    private KernelShellFoundation? _foundation;
    private IProtectedRootStarts? _publishedStarts;
    private IProtectedRootStarts? _testStarts;
    private sealed record InlineStarts(InlineRootRequests Requests, NativeExecutionModel Model);
    private InlineStarts? _publishedInlineStarts;
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
            ValidateTestRunChildInputs = storage.TakesAttachedTestRuns ? ValidateTestRunChildInputsAsync : null,
            ResolveWorkflow = ResolveWorkflowAsync
        });
        InlineRootRequests? inline = null;
        if (storage.TakesEphemeralTestRuns)
        {
            await using var runtime = await scopes.CreateAsyncScopeAsync(persistenceScope);
            var recordCompletion = runtime.ServiceProvider.GetRequiredService<PersistenceModeOptions>().RecordCompletion;
            inline = new(tenant, WorkflowActor.ActorType, MatchesInlineMaterial, protection!.ProtectHeldStartAsync,
                testTenant ?? throw new InvalidOperationException("Inline Test Runs require the isolated tenant."),
                status => !recordCompletion && status == ActorStatus.Finished);
        }
        else if (storage.TakesInlineStarts)
            inline = new(tenant, WorkflowActor.ActorType, MatchesInlineMaterial, protection!.ProtectHeldStartAsync);

        bool MatchesInlineMaterial(StartSpec spec, object material) =>
            WorkflowActor.DescribeStart(spec.Type, spec.TypeData) is { } selected &&
            model.MatchesInlineMaterial(material, selected.Workflow, selected.Definition, selected.ExecutableSelection);
        var auxiliary = schedules is null ? null : new ScheduleActor(schedules.Value.Calendar, schedules.Value.Worker.CreateAcceptor());
        (_owner, _lifetime) = storage.CreateOwner(new WorkflowActor([model]), logs, retention.Window, auxiliary,
            protectChildExecutables || inline is not null ? protection!.ProtectAsync : null, inline);
        await _owner.StartAsync(token);
        if (storage.TakesPreparedStarts) Volatile.Write(ref _publishedStarts, storage.BindPublishedStarts(tenant));
        if (testTenant is not null) Volatile.Write(ref _testStarts, storage.BindPublishedStarts(testTenant));
        if (inline is not null) Volatile.Write(ref _publishedInlineStarts, new(inline, model));
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
        var inline = CloseAdmissions();
        using var maintenanceDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        maintenanceDeadline.CancelAfter(MaintenanceStopBudget);
        try { await StopMaintenanceAsync(maintenanceDeadline.Token); }
        finally
        {
            if (_owner is not null) await _owner.StopAsync(token);
            if (inline is not null) await inline.DrainSettlementsAsync(token);
        }
    }

    private InlineRootRequests? CloseAdmissions()
    {
        Volatile.Write(ref _publishedStarts, null);
        Volatile.Write(ref _testStarts, null);
        // Keep the closed edge so disposal can rejoin its settlements after an interrupted stop.
        var inline = Volatile.Read(ref _publishedInlineStarts)?.Requests;
        inline?.Close();
        return inline;
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

    internal async ValueTask<WorkflowPublishedStart?> ReadHeldPublicationAsync(KernelServedSlot slot, CancellationToken token)
    {
        if (serving.Find(slot.SlotId) != slot) return null;
        await using var runtime = await scopes.CreateAsyncScopeAsync(persistenceScope);
        var source = await runtime.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .FindAsync(WorkflowActivationReferenceIdentity.Create(slot.ActivationId), token);
        if (source is null || source.Scope != WorkflowExecutableReferenceScope.Published ||
            source.ArtifactId != slot.Workflow.Id || source.DefinitionId != slot.DefinitionId || source.SlotId != slot.SlotId ||
            source.ActivationId != slot.ActivationId || (source.TenantId ?? PersistenceScope.DefaultValue) != tenant.Tenant ||
            source.PublicationScope is { } sourceScope && sourceScope != persistenceScope || !source.IsLive(clock.GetUtcNow()))
            return null;
        // The same source and slot are rechecked by the protected pin transaction before birth.
        return KernelPublishedRunDispatcher.CreatePublication(source, slot, subjectId: null);
    }

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

    internal bool TakesEphemeralTestRuns => storage.TakesEphemeralTestRuns;
    internal bool TakesAttachedTestRuns => storage.TakesAttachedTestRuns;
    internal bool TakesHeldEphemeralHttp => storage.TakesHeldEphemeralHttp;

    internal async ValueTask<InlineStartOutcome> StartInlineTestAsync(WorkflowTestScope test, StartSpec start, Workflow executable,
        CancellationToken token)
    {
        if (Volatile.Read(ref _publishedInlineStarts) is { } inline)
            return await inline.Requests.StartAdditionalAsync(test.ScopeId, start, inline.Model.CaptureInlineMaterial(executable),
                outcome => SettleInlineTestOutcomeAsync(test, outcome), token);
        var unavailable = await SettleInlineTestOutcomeAsync(test, new(InlineStartDisposition.Unavailable));
        token.ThrowIfCancellationRequested();
        return unavailable;
    }

    internal async ValueTask<InlineHeldStartOutcome> StartInlineHeldTestAsync(WorkflowTestScope test, Guid attempt, StartSpec start,
        Workflow executable, WorkflowTestRunHttpBudget budget, CancellationToken token)
    {
        if (Volatile.Read(ref _publishedInlineStarts) is not { } inline)
            return new(await SettleUnqueuedAsync(new(InlineStartDisposition.Unavailable)), null);

        long now;
        try { now = await storage.ReadClockAsync(token); }
        catch
        {
            await SettleUnqueuedAsync(new(InlineStartDisposition.Unavailable));
            throw;
        }
        var remaining = budget.Remaining;
        if (remaining <= TimeSpan.Zero)
            return new(await SettleUnqueuedAsync(new(InlineStartDisposition.Unavailable)),
                new(null, null, Error: "timeout"));
        var deadline = checked(now + (long)Math.Ceiling(remaining.TotalMilliseconds));
        using var observation = CancellationTokenSource.CreateLinkedTokenSource(token);
        observation.CancelAfter(remaining);
        try
        {
            return await inline.Requests.StartAdditionalHeldAsync(test.ScopeId, start, inline.Model.CaptureInlineMaterial(executable), deadline,
                outcome => SettleInlineTestOutcomeAsync(test, outcome, attempt), SettleUnqueuedAsync, observation.Token);
        }
        catch (OperationCanceledException) when (observation.IsCancellationRequested && !token.IsCancellationRequested)
        {
            return new(new(InlineStartDisposition.Unknown), new(null, null, Error: "timeout"));
        }

        async ValueTask<InlineStartOutcome> SettleUnqueuedAsync(InlineStartOutcome outcome)
        {
            if (outcome.Disposition != InlineStartDisposition.Unavailable)
                return await SettleInlineTestOutcomeAsync(test, outcome, attempt);
            return await SettleInlineTestAsync(test, null, attempt) ? outcome : new(InlineStartDisposition.Unknown);
        }
    }

    private async ValueTask<InlineStartOutcome> SettleInlineTestOutcomeAsync(WorkflowTestScope test, InlineStartOutcome outcome, Guid? attempt = null)
    {
        var settlement = outcome switch
        {
            { Disposition: InlineStartDisposition.Volatile, Status: ActorStatus.Finished } => WorkflowTestRunInlineState.CompletedWithoutRecord,
            { Disposition: InlineStartDisposition.Volatile } => throw new InvalidOperationException("An inline Test Run without a record must have completed."),
            { Disposition: InlineStartDisposition.Refused } => WorkflowTestRunInlineState.Refused,
            { Disposition: InlineStartDisposition.Unavailable, RefusalReason: RefusalReason.RateLimited } => WorkflowTestRunInlineState.RateLimited,
            { Disposition: InlineStartDisposition.Unavailable } => WorkflowTestRunInlineState.Unavailable,
            _ => (WorkflowTestRunInlineState?)null
        };
        if (settlement is { } definite && !await SettleInlineTestAsync(test, definite, attempt))
            return new(InlineStartDisposition.Unknown);
        return outcome;
    }

    internal async ValueTask<bool> SettleInlineTestAsync(WorkflowTestScope test, WorkflowTestRunInlineState? outcome, Guid? attempt = null)
    {
        // The exact Test Run scope is durable and can outlive the original request's service scope.
        using var budget = new CancellationTokenSource(InlineSettlementBudget, clock);
        try
        {
            await using var runtime = await scopes.CreateAsyncScopeAsync(persistenceScope);
            var admissions = runtime.ServiceProvider.GetRequiredService<IWorkflowTestScopeAdmissionStore>();
            return attempt is { } held
                ? await admissions.SettleHeldInlineStartAsync(test, held, outcome, budget.Token)
                : await admissions.SettleInlineStartAsync(test, outcome!.Value, budget.Token);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested) { return false; }
        catch (Exception error)
        {
            logs.CreateLogger<KernelShellRuntime>().LogError(error, "Failed to settle inline Test Run {TestRunId}.", test.ScopeId);
            return false;
        }
    }

    internal ValueTask<InlineStartOutcome> StartInlinePublishedAsync(string operationId, StartSpec start, Workflow executable,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Volatile.Read(ref _publishedInlineStarts) is { } inline
            ? inline.Requests.StartAsync(operationId, start, inline.Model.CaptureInlineMaterial(executable), token)
            : ValueTask.FromResult(new InlineStartOutcome(InlineStartDisposition.Unavailable));
    }

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

    private async ValueTask<(JsonObject? Inputs, ActivityFault? Refusal)> ValidateTestRunChildInputsAsync(
        TestRunChildSelection selection, JsonObject? supplied, CancellationToken token)
    {
        await using var scope = await scopes.CreateAsyncScopeAsync(persistenceScope);
        var services = scope.ServiceProvider;
        var executable = await services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(selection.ArtifactId, token);
        if (executable is null || executable.Identity != NativeExecutableMapper.ReadMaterialSelection(selection.ExecutableSelection, selection.ArtifactId) ||
            executable.InputContract is not { Version: WorkflowExecutableInputContract.CurrentVersion } contract)
            return (null, new("dispatch.test.material-unavailable", "The selected child executable does not carry an available input contract."));

        var inputs = supplied?.Select(input => KeyValuePair.Create(input.Key, JsonSerializer.SerializeToElement(input.Value))) ?? [];
        var result = services.GetRequiredService<IWorkflowExecutableInputValidator>().Validate(contract, inputs);
        if (!result.IsValid)
            return (null, new("dispatch.test.invalid-input", string.Join(" ", result.Findings.Select(finding => finding.Message))));
        var normalized = new JsonObject();
        foreach (var input in result.NormalizedInputs)
            normalized.Add(input.Key, JsonNode.Parse(input.Value.GetRawText()));
        return (normalized, null);
    }

    public async ValueTask DisposeAsync()
    {
        var inline = CloseAdmissions();
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
            finally
            {
                try
                {
                    if (inline is not null) await DrainInlineSettlementsAsync(inline);
                }
                finally { _client?.Dispose(); }
            }
        }
    }

    private async Task DrainInlineSettlementsAsync(InlineRootRequests inline)
    {
        using var drain = new CancellationTokenSource(InlineSettlementBudget, clock);
        try { await inline.DrainSettlementsAsync(drain.Token); }
        catch (OperationCanceledException) when (drain.IsCancellationRequested)
        {
            logs.CreateLogger<KernelShellRuntime>().LogWarning("Inline Test Run settlements did not finish within the shell's disposal deadline.");
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
