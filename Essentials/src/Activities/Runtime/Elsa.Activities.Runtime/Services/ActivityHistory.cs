using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Actual callback observations carried by its existing outcome checkpoint, never an extra boundary.</summary>
internal sealed class ActivityHistory(RuntimeSchedulerWorkItem work, string activityId, string? attemptId, TimeProvider clock,
    RuntimeHistoryCausation? cause, CancellationToken cancellationToken)
{
    private readonly List<RuntimeHistorySourceFact> _facts = [];
    private bool _started;
    private string? _attemptId = attemptId;
    private DateTimeOffset _returnedAt;

    internal static async ValueTask<ActivityHistory?> CreateAsync(IServiceProvider services, RuntimeSchedulerWorkItem work,
        ActivityExecutionState state, TimeProvider clock, CancellationToken cancellationToken, RuntimeHistoryCausation? cause = null)
    {
        var policy = await ReadPolicyAndValidateAsync(services, work.WorkflowExecutionId, cancellationToken);
        if (policy is null) return null;
        return new(work, state.InvocationId, state.Attempts?.LastOrDefault(attempt => attempt.EndedAt is null)?.AttemptId,
            clock, cause is null ? new(CommandId: work.CommandId, SchedulerWorkItemId: work.WorkItemId) : cause with { CommandId = work.CommandId, SchedulerWorkItemId = work.WorkItemId }, cancellationToken);
    }

    internal static async ValueTask<RuntimeHistoryCapturePolicy?> ReadPolicyAndValidateAsync(IServiceProvider services,
        string workflowId, CancellationToken cancellationToken)
    {
        try
        {
            // Native admission is authoritative even when the optional capture module was removed. No history
            // query is resolved for an uncaptured run, and a required pin is checked before callback effects.
            var nativeStore = services.GetService<IWorkflowExecutionStateStore>();
            var native = nativeStore is null ? null : await nativeStore.FindAsync(workflowId, cancellationToken);
            if (native?.HistoryCapturePolicy is not { } policy) return null;
            if (services.GetService<IRuntimeHistorySourceContributor>() is null || !RuntimeHistorySourceFacts.IsFinalProfile(policy) ||
                !services.GetServices<IRuntimeCheckpointCommitEnricher>().OfType<IRuntimeHistoryCaptureSupport>().Any(source => source.CanCapture(policy)) ||
                services.GetService<IRuntimeHistoryStore>() is not { } reader ||
                services.GetRequiredService<IRuntimeCheckpointCommitStore>().CanCommitHistory(reader) != true)
            {
                if (policy.Required)
                    throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The callback cannot acknowledge its admitted history profile.");
                return null;
            }
            return policy;
        }
        catch (Exception exception) when (IsUnavailablePolicyRead(exception))
        { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Callback history source metadata is unavailable.", exception); }
    }

    private static bool IsUnavailablePolicyRead(Exception exception)
    {
        if (exception is OperationCanceledException or InvalidDataException or JsonException or ArgumentException or
            RuntimeSchedulerWorkUnavailableException or RuntimeCheckpointInconsistentDurabilityException)
            return false;
        if (exception is IOException or DbException or TimeoutException or NotSupportedException) return true;
        // Native adapters keep a provider cause behind a contextual InvalidOperationException. Bare contract/scope
        // refusals and wrapped corruption stay hard; the wrapper must not turn a read outage into acknowledged poison.
        return exception is InvalidOperationException && exception.InnerException is { } inner && inner is not NotSupportedException &&
            (IsUnavailablePolicyRead(inner) || inner is InvalidOperationException { InnerException: null });
    }

    internal static async ValueTask<T> ObserveAsync<T>(ActivityHistory? history, Func<ValueTask<T>> callback, Func<T, string> kind)
    {
        history?.Record(RuntimeHistorySourceFactKind.ActivityContinuationStarted);
        if (history is not null) history._started = true;
        T result;
        try { result = await callback(); }
        catch (OperationCanceledException) when (history is null || history.IsCancellationRequested) { throw; }
        catch
        {
            history?.Record(RuntimeHistorySourceFactKind.ActivityContinuationFaulted, "callback-threw");
            throw;
        }
        if (history is not null)
            history._returnedAt = history.Record(RuntimeHistorySourceFactKind.ActivityContinuationReturned, kind(result));
        return result;
    }

    private bool IsCancellationRequested => cancellationToken.IsCancellationRequested;

    internal void SetAttempt(string? actualAttemptId) => _attemptId = string.IsNullOrWhiteSpace(actualAttemptId) ? null : actualAttemptId;

    internal void Outcome(RuntimeHistorySourceFactKind kind, IReadOnlyList<string>? outcomes = null)
    { if (_started) Record(kind, outcomes: outcomes); }

    internal IReadOnlyList<RuntimeHistorySourceFact> Materialize(RuntimeStructuralContinuation? continuation = null) =>
        Array.AsReadOnly(_facts.Where(fact => fact.Kind is RuntimeHistorySourceFactKind.ActivityContinuationStarted or RuntimeHistorySourceFactKind.ActivityContinuationReturned)
            .Concat(_started ? continuation?.MaterializeHistorySourceFacts(_returnedAt) ?? [] : [])
            .Concat(_facts.Where(fact => fact.Kind is not (RuntimeHistorySourceFactKind.ActivityContinuationStarted or RuntimeHistorySourceFactKind.ActivityContinuationReturned))).ToArray());

    private DateTimeOffset Record(RuntimeHistorySourceFactKind kind, string? reason = null, IReadOnlyList<string>? outcomes = null)
    {
        var at = clock.GetUtcNow();
        _facts.Add(new(kind, RuntimeHistoryIdentity.CreateBinding([work.WorkItemId, _attemptId, kind.ToString()]), at,
            activityId, _attemptId, cause, reasonCode: reason, outcomeNames: outcomes));
        return at;
    }
}
