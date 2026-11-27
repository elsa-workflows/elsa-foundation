using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Provider operations already bound to the shell's Actor Foundation tenant and store.</summary>
/// <param name="ApplyControlAsync">Applies one exact control revision. A null header is a Stop. Accepted and Duplicate
/// acknowledge a durable monotone control receipt. StaleSelection means an exact newer revision superseded it.
/// Compare the durable receipt before issuing another write on repeat scans.</param>
/// <param name="AdmitOccurrenceAsync">Admits the exact immutable start. Accepted and Duplicate return the same
/// durable birth reference, even if the original inbox row has gone.</param>
/// <param name="ConfirmRetiredAsync">Proves irreversible retirement of the exact birth and release of dependent work.
/// A missing actor alone is insufficient. False retains the executable pin.</param>
internal sealed record KernelScheduleProvider(
    Func<RuntimeKernelScheduleControl, ScheduleHeader?, CancellationToken, ValueTask<Admission>> ApplyControlAsync,
    Func<RuntimeKernelScheduleOccurrence, StartSpec, CancellationToken, ValueTask<Admission>> AdmitOccurrenceAsync,
    Func<RuntimeKernelScheduleOccurrence, ActorRef, CancellationToken, ValueTask<bool>> ConfirmRetiredAsync);

/// <summary>The shell-owned schedule reconciliation loop. Start it after the provider can route both actor types.</summary>
internal sealed class KernelScheduleReconciliationWorker
{
    private const int PageSize = 32;
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ControlSweep = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetirementSweep = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly TriggerScope _controlScope;
    private readonly TriggerScope[] _occurrenceScopes;
    private readonly BoundScope _actors;
    private readonly KernelHostCapabilities _capabilities;
    private readonly ICronCalendar? _calendar;
    private readonly KernelScheduleProvider _provider;
    private readonly Func<CancellationToken, ValueTask<long>> _readScheduleClock;
    private readonly TimeProvider _clock;
    private readonly ILogger<KernelScheduleReconciliationWorker> _logger;
    private int _running;

    internal KernelScheduleReconciliationWorker(IServiceScopeFactory scopes, TriggerScope scope, BoundScope actors,
        KernelHostCapabilities capabilities, ICronCalendar? calendar, KernelScheduleProvider provider,
        Func<CancellationToken, ValueTask<long>> readScheduleClock, TimeProvider clock, ILogger<KernelScheduleReconciliationWorker> logger)
    {
        KernelScheduleAuthority.RequireBoundScope(scope, actors);
        _scopes = scopes;
        _controlScope = RuntimeKernelScheduleControl.CanonicalScope(scope);
        _occurrenceScopes = scope.Persistence.Value == PersistenceScope.DefaultValue
            ? [_controlScope, _controlScope with { TenantId = PersistenceScope.DefaultValue }]
            : [scope];
        _actors = actors;
        _capabilities = capabilities;
        _calendar = calendar;
        _provider = provider;
        _readScheduleClock = readScheduleClock;
        _clock = clock;
        _logger = logger;
    }

    internal IScheduleOccurrenceAcceptor CreateAcceptor() =>
        new KernelScheduleOccurrenceAcceptor(_controlScope, _actors, _readScheduleClock, (occurrence, readClock, token) =>
            WithRuntimeAsync(services => services.GetRequiredService<IRuntimeKernelScheduleOccurrenceStore>()
                .TryAcceptAsync(occurrence, readClock, token)));

    internal async Task RunAsync(CancellationToken token)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
            throw new InvalidOperationException("Only one schedule reconciliation loop may run for this shell.");
        try
        {
            long? controlsPausedAt = null;
            var pending = _occurrenceScopes.Select(_ => new Scan<RuntimeKernelScheduleOccurrenceCursor>(Tick)).ToArray();
            var retired = _occurrenceScopes.Select(_ => new Scan<RuntimeKernelScheduleOccurrenceCursor>(RetirementSweep)).ToArray();
            using var timer = new PeriodicTimer(Tick, _clock);
            do
            {
                token.ThrowIfCancellationRequested();
                if (controlsPausedAt is null || _clock.GetElapsedTime(controlsPausedAt.Value) >= ControlSweep)
                    controlsPausedAt = await ScanControlsAsync(token) ? null : _clock.GetTimestamp();
                for (var i = 0; i < _occurrenceScopes.Length; i++)
                {
                    var scope = _occurrenceScopes[i];
                    await ScanPageAsync(pending[i], ct => ReadPendingTailAsync(scope, ct),
                        (after, through, ct) => ReadPendingAsync(scope, after, through, ct), "occurrence delivery scan", token);
                    await ScanPageAsync(retired[i], ct => ReadMaterializedTailAsync(scope, ct),
                        (after, through, ct) => ReadRetirementAsync(scope, after, through, ct), "occurrence retirement scan", token);
                }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _logger.LogError(error, "Kernel schedule reconciliation stopped for tenant {Tenant}. Restart the shell after correcting the fault.",
                _actors.Tenant);
        }
        finally { Volatile.Write(ref _running, 0); }
    }

    private async ValueTask<bool> ScanControlsAsync(CancellationToken token)
    {
        try
        {
            IRuntimeKernelScheduleControlScanPage? page;
            using (var read = Limit(token))
                page = await WithRuntimeAsync(services => services.GetRequiredService<IRuntimeKernelScheduleControlScanStore>()
                    .ReadScanPageAsync(_controlScope, PageSize, read.Token));
            if (page is null) return false;
            RequirePage(page.Items.Count);
            foreach (var control in page.Items)
                await VisitAsync(ct => ApplyControlAsync(control, ct), "apply control", control.ScheduleKey, token);

            using var checkpoint = Limit(token);
            var advanced = await WithRuntimeAsync(services => services.GetRequiredService<IRuntimeKernelScheduleControlScanStore>()
                .TryAdvanceScanAsync(_controlScope, page, checkpoint.Token));
            return !advanced || page.HasMore;
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            _logger.LogWarning(error, "Kernel schedule control scan failed for tenant {Tenant}. It will retry.", _actors.Tenant);
            return false;
        }
    }

    private async ValueTask ApplyControlAsync(RuntimeKernelScheduleControl control, CancellationToken token)
    {
        if (RuntimeKernelScheduleControl.CanonicalScope(control.Scope) != _controlScope)
        {
            WrongTenant("apply control", control.ScheduleKey);
            return;
        }
        ScheduleHeader? header = null;
        if (control.Owner is not null)
        {
            header = await WithRuntimeAsync(services => KernelScheduleAuthority.BindAsync(control, _controlScope, _actors,
                services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(), services.GetRequiredService<IWorkflowExecutableStore>(),
                _capabilities, _calendar, services.GetRequiredService<IPersistenceAccessContextAccessor>(), _clock, token));
            if (header is null)
            {
                _logger.LogDebug("Schedule {ScheduleKey} revision {Revision} no longer has its exact publication source.", control.ScheduleKey, control.Revision);
                return;
            }
        }
        var result = await _provider.ApplyControlAsync(control, header, token);
        if (result is { Outcome: AdmissionOutcome.Refused, Reason: RefusalReason.StaleSelection })
        {
            _logger.LogDebug("Schedule {ScheduleKey} revision {Revision} was superseded by a newer control.", control.ScheduleKey, control.Revision);
            return;
        }
        if (result.Outcome is not (AdmissionOutcome.Accepted or AdmissionOutcome.Duplicate))
            Refused("apply control", control.ScheduleKey, result.Reason);
    }

    private ValueTask<RuntimeKernelScheduleOccurrenceCursor?> ReadPendingTailAsync(TriggerScope scope, CancellationToken token) =>
        WithRuntimeAsync(services => services.GetRequiredService<IRuntimeKernelScheduleOccurrenceDeliveryStore>().ReadPendingTailAsync(scope, token));

    private async ValueTask<RuntimeKernelScheduleOccurrenceCursor?> ReadPendingAsync(TriggerScope scope,
        RuntimeKernelScheduleOccurrenceCursor? after, RuntimeKernelScheduleOccurrenceCursor through, CancellationToken token)
    {
        using var read = Limit(token);
        var items = await WithRuntimeAsync(services => services.GetRequiredService<IRuntimeKernelScheduleOccurrenceDeliveryStore>()
            .ListPendingAsync(scope, after, through, PageSize, read.Token));
        RequirePage(items.Count);
        foreach (var item in items)
            await VisitAsync(ct => DeliverAsync(scope, item.Occurrence, ct), "admit occurrence", item.Occurrence.Id, token);
        return items.Count == PageSize ? items[^1].Cursor : null;
    }

    private async ValueTask DeliverAsync(TriggerScope scope, RuntimeKernelScheduleOccurrence occurrence, CancellationToken token)
    {
        if (occurrence.Scope != scope)
        {
            WrongTenant("admit occurrence", occurrence.Id);
            return;
        }
        var start = KernelScheduleAuthority.ReadPinnedStart(occurrence.PinnedStart, _actors.Tenant, occurrence.Executable,
            occurrence.TriggerNode, occurrence.ScheduleKey) ?? throw new InvalidDataException("A committed occurrence has an invalid pinned workflow start.");
        var result = await _provider.AdmitOccurrenceAsync(occurrence, start, token);
        if (result.Outcome is not (AdmissionOutcome.Accepted or AdmissionOutcome.Duplicate))
        {
            Refused("admit occurrence", occurrence.Id, result.Reason);
            return;
        }
        if (result.Actor is not { } actor || !KernelScheduleAuthority.IsReference(actor.Id))
            throw new InvalidDataException("The provider did not return the exact actor reference for a scheduled birth.");

        await using var runtime = await _scopes.CreateAsyncScopeAsync(scope.Persistence);
        await runtime.ServiceProvider.GetRequiredService<IRuntimeKernelScheduleOccurrenceDeliveryStore>()
            .MarkMaterializedAsync(occurrence, actor.Id, token);
    }

    private ValueTask<RuntimeKernelScheduleOccurrenceCursor?> ReadMaterializedTailAsync(TriggerScope scope, CancellationToken token) =>
        WithRuntimeAsync(services => services.GetRequiredService<IRuntimeKernelScheduleOccurrenceRetirementStore>().ReadMaterializedTailAsync(scope, token));

    private async ValueTask<RuntimeKernelScheduleOccurrenceCursor?> ReadRetirementAsync(TriggerScope scope,
        RuntimeKernelScheduleOccurrenceCursor? after, RuntimeKernelScheduleOccurrenceCursor through, CancellationToken token)
    {
        using var read = Limit(token);
        var items = await WithRuntimeAsync(services => services.GetRequiredService<IRuntimeKernelScheduleOccurrenceRetirementStore>()
            .ListMaterializedAsync(scope, after, through, PageSize, read.Token));
        RequirePage(items.Count);
        foreach (var item in items)
            await VisitAsync(ct => RetireAsync(scope, item, ct), "confirm retirement", item.Occurrence.Id, token);
        return items.Count == PageSize ? items[^1].Cursor : null;
    }

    private async ValueTask RetireAsync(TriggerScope scope, RuntimeKernelScheduleMaterializedOccurrence item, CancellationToken token)
    {
        if (item.Occurrence.Scope != scope)
        {
            WrongTenant("confirm retirement", item.Occurrence.Id);
            return;
        }
        _ = await WithRuntimeAsync(services => services.GetRequiredService<IRuntimeKernelScheduleOccurrenceRetirementStore>()
            .TryRetireAsync(item.Occurrence, item.ActorReference, ConfirmRetiredAsync, token));

        ValueTask<bool> ConfirmRetiredAsync(RuntimeKernelScheduleOccurrence occurrence, string reference, CancellationToken ct) =>
            occurrence == item.Occurrence && reference == item.ActorReference && KernelScheduleAuthority.IsReference(reference)
                ? _provider.ConfirmRetiredAsync(occurrence, new ActorRef(reference), ct)
                : ValueTask.FromResult(false);
    }

    private async ValueTask ScanPageAsync<T>(Scan<T> scan, Func<CancellationToken, ValueTask<T?>> tail,
        Func<T?, T, CancellationToken, ValueTask<T?>> page, string operation, CancellationToken token) where T : class
    {
        if (scan.PausedAt is { } paused && _clock.GetElapsedTime(paused) < scan.Pause) return;
        try
        {
            if (scan.Through is null)
            {
                using var read = Limit(token);
                scan.Through = await tail(read.Token);
            }
            // Keep the initial tail across pages and retries, so a live append cannot postpone replay indefinitely.
            var next = scan.Through is { } through ? await page(scan.After, through, token) : null;
            if (next is not null && Equals(scan.After, next))
                throw new InvalidDataException("A schedule reconciliation cursor did not advance.");
            scan.After = next;
            if (next is null) scan.Through = null;
            scan.PausedAt = next is null ? _clock.GetTimestamp() : null;
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            scan.PausedAt = _clock.GetTimestamp();
            _logger.LogWarning(error, "Kernel schedule {Operation} failed for tenant {Tenant}. It will retry.", operation, _actors.Tenant);
        }
    }

    private async ValueTask VisitAsync(Func<CancellationToken, ValueTask> apply, string operation, string identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var attempt = Limit(token);
        try { await apply(attempt.Token); }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            _logger.LogWarning(error, "Kernel schedule {Operation} failed for {Identity} in tenant {Tenant}. It will retry on a later scan.",
                operation, identity, _actors.Tenant);
        }
    }

    private async ValueTask<T> WithRuntimeAsync<T>(Func<IServiceProvider, ValueTask<T>> operation)
    {
        await using var scope = await _scopes.CreateAsyncScopeAsync(_controlScope.Persistence);
        return await operation(scope.ServiceProvider);
    }

    private void Refused(string operation, string identity, RefusalReason? reason)
    {
        switch (reason)
        {
            case RefusalReason.TargetMismatch: WrongTenant(operation, identity); break;
            case RefusalReason.Conflict:
                _logger.LogWarning("Kernel schedule {Operation} found a different durable intent for {Identity} in tenant {Tenant}.",
                    operation, identity, _actors.Tenant);
                break;
            default:
                _logger.LogWarning("Kernel schedule {Operation} was refused for {Identity} in tenant {Tenant}: {Reason}.",
                    operation, identity, _actors.Tenant, reason);
                break;
        }
    }

    private void WrongTenant(string operation, string identity) =>
        _logger.LogWarning("Kernel schedule {Operation} refused {Identity} because its tenant or exact target did not match {Tenant}.",
            operation, identity, _actors.Tenant);

    private static void RequirePage(int count)
    {
        if (count > PageSize)
            throw new InvalidDataException("The schedule reconciliation page exceeded its requested bound.");
    }

    private static CancellationTokenSource Limit(CancellationToken token)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        source.CancelAfter(OperationBudget);
        return source;
    }

    private sealed class Scan<T>(TimeSpan pause) where T : class
    {
        internal TimeSpan Pause => pause;
        internal T? After { get; set; }
        internal T? Through { get; set; }
        internal long? PausedAt { get; set; }
    }
}
