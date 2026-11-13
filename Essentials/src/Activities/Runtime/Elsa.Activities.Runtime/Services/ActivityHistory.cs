using Elsa.Workflows.Runtime.Core.Models;

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
        var policy = await ActivityHistoryPreflight.ReadPolicyAndValidateAsync(services, work.WorkflowExecutionId, cancellationToken);
        return Create(policy, work, state, clock, cancellationToken, cause);
    }

    internal static ActivityHistory? Create(RuntimeHistoryCapturePolicy? policy, RuntimeSchedulerWorkItem work,
        ActivityExecutionState state, TimeProvider clock, CancellationToken cancellationToken, RuntimeHistoryCausation? cause = null)
    {
        if (!RuntimeHistorySourceFacts.IsFinalProfile(policy)) return null;
        return new(work, state.InvocationId, state.Attempts?.LastOrDefault(attempt => attempt.EndedAt is null)?.AttemptId,
            clock, cause is null ? new(CommandId: work.CommandId, SchedulerWorkItemId: work.WorkItemId) : cause with { CommandId = work.CommandId, SchedulerWorkItemId = work.WorkItemId }, cancellationToken);
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
