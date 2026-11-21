using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.Logging;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Services.Bookmarks;

/// <summary>
/// Default <see cref="IDurableTimerScheduler"/>. A thin, testable seam that forwards to the configured
/// <see cref="IDurableTimerStore"/>, isolating suspending activities from the store contract.
/// </summary>
public sealed class DurableTimerScheduler : IDurableTimerScheduler
{
    private readonly IDurableTimerStore _store;
    internal sealed record HistoryContext(IWorkflowExecutionStateStore Executions, Func<IActivityExecutionStateStore?>? Activities,
        IRuntimeCheckpointCommitStore? Native, IRuntimeHistoryObservationWriter? Writer, IRuntimeHistoryStore? Reader, IPersistenceAccessContextAccessor? Access);
    internal sealed record AdmissionContext(IWorkflowExecutionStateStore Executions, IPersistenceAccessContextAccessor? Access);
    internal Func<AdmissionContext?>? AdmissionProviders { get; init; }
    internal Func<HistoryContext?>? HistoryProviders { get; init; }

    public bool CanRecordHistory(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader) =>
        HistoryProviders?.Invoke() is { } owners && OwnsHistoryChain(owners, writer, reader);

    private bool OwnsHistoryChain(HistoryContext owners, IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader) =>
        ReferenceEquals(owners.Writer, writer) && ReferenceEquals(owners.Reader, reader) &&
        writer.CanRecordHistory(reader) && owners.Native is { } native && native.CanCommitHistory(reader) &&
        _store is IDurableTimerHistoryStore { SupportsHistoryResponsibility: true } source &&
        source.CanInspectHistorySources(reader) && source.CanDischargeUnrecordedCompletion(native);

    private AdmissionContext? ResolveAdmission() => AdmissionProviders is { } resolve ? resolve() :
        HistoryProviders?.Invoke() is { } owners ? new(owners.Executions, owners.Access) : null;

    private readonly IDeferredEventPublisher? _events;
    private readonly ILogger<DurableTimerScheduler> _logger = NullLogger<DurableTimerScheduler>.Instance;

    public DurableTimerScheduler(IDurableTimerStore store) => _store = store;

    public DurableTimerScheduler(IDurableTimerStore store, IDeferredEventPublisher events,
        ILogger<DurableTimerScheduler>? logger = null) : this(store)
    {
        _events = events;
        _logger = logger ?? NullLogger<DurableTimerScheduler>.Instance;
    }

    public async ValueTask<DurableTimer> ScheduleAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadHistoryAsync(timer.WorkflowExecutionId, () => ValueTask.FromResult(ResolveAdmission()), cancellationToken) is { } admission)
        {
            var captured = admission.Access?.Current;
            void CheckAccess()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (captured is not null && admission.Access!.Current != captured)
                    throw new InvalidOperationException("Timer scheduling changed persistence access.");
            }
            var execution = await ReadHistoryAsync(timer.WorkflowExecutionId,
                () => admission.Executions.FindAsync(timer.WorkflowExecutionId, cancellationToken), cancellationToken);
            CheckAccess();
            if (execution?.HistoryCapturePolicy is { } policy && execution.HistoryCaptureEnabledAtAdmission != false &&
                policy.SupportedKinds.Intersect(DurableTimerHistoryFacts.Kinds, StringComparer.Ordinal).Any())
            {
                var owners = await ReadHistoryAsync(timer.WorkflowExecutionId,
                    () => ValueTask.FromResult(HistoryProviders?.Invoke()), cancellationToken);
                CheckAccess();
                if (!DurableTimerHistoryFacts.Kinds.All(kind => policy.SupportedKinds.Contains(kind, StringComparer.Ordinal)) ||
                    owners is null || !ReferenceEquals(owners.Executions, admission.Executions) || !ReferenceEquals(owners.Access, admission.Access) ||
                    owners.Writer is null || owners.Reader is null || !OwnsHistoryChain(owners, owners.Writer, owners.Reader))
                    throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The original timer capture chain is unavailable.");
                if (timer.ActivityExecutionId is { } activityId)
                {
                    var activity = await ReadHistoryAsync<ActivityExecutionState?>(timer.WorkflowExecutionId, async () =>
                    {
                        var activities = owners.Activities?.Invoke();
                        return activities is null ? null : await activities.FindAsync(timer.WorkflowExecutionId, activityId, cancellationToken);
                    }, cancellationToken);
                    CheckAccess();
                    if (activity is null) throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The timer's concrete activity occurrence is unavailable.");
                    timer = timer with { ExecutionScopeId = activity.ExecutionScopeId };
                }
                timer = timer with { HistoryResponsibility = new(policy, [DurableTimerHistoryFacts.CreateScheduled(timer, policy)!.Event], false) };
            }
            CheckAccess();
        }
        var stored = await _store.SaveAsync(timer, cancellationToken);
        if (_events is not null)
        {
            try { await _events.Publish(new DurableTimerScheduled(stored.DueTime), cancellationToken); }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                _logger.LogWarning(exception, "Durable timer scheduled notification failed; periodic recovery remains available");
            }
        }
        return stored;
    }
    private static async ValueTask<T> ReadHistoryAsync<T>(string run, Func<ValueTask<T>> read, CancellationToken token)
    {
        try { var value = await read(); token.ThrowIfCancellationRequested(); return value; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { token.ThrowIfCancellationRequested(); throw; }
        catch (Exception error) when (RuntimeHistoryReadFailures.IsMalformed(error))
        { token.ThrowIfCancellationRequested(); throw new RuntimeHistoryIntegrityException(run, "the timer producer could not interpret original admission or occurrence", error); }
        catch (Exception error) when (RuntimeHistoryReadFailures.IsUnavailable(error) || error is OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Original timer admission or occurrence is unavailable.",
                new RuntimeHistoryUnavailableException(run, "timer-schedule-admission", error));
        }
    }

}
