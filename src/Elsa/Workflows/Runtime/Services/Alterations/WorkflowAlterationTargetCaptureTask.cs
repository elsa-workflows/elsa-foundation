using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Advances durable capture by one bounded page. Targets become claimable only when the store seals the completed
/// cohort, so retrying this task never starts work over an unsealed scan.
/// </summary>
public sealed class WorkflowAlterationTargetCaptureTask
{
    private readonly IWorkflowAlterationStore _store;
    private readonly IWorkflowExecutionStateStore _workflowExecutions;
    private readonly TimeProvider _timeProvider;
    private readonly WorkflowAlterationTargetCaptureOptions _options;
    private readonly IActivityExecutionStateStore? _activityExecutionStateStore;
    private readonly IWorkflowAlterationPayloadProtector? _payloadProtector;

    public WorkflowAlterationTargetCaptureTask(
        IWorkflowAlterationStore store,
        IWorkflowExecutionStateStore workflowExecutions,
        TimeProvider? timeProvider = null,
        WorkflowAlterationTargetCaptureOptions? options = null,
        IActivityExecutionStateStore? activityExecutionStateStore = null,
        IWorkflowAlterationPayloadProtector? payloadProtector = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(workflowExecutions);
        _store = store;
        _workflowExecutions = workflowExecutions;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new WorkflowAlterationTargetCaptureOptions();
        _activityExecutionStateStore = activityExecutionStateStore;
        _payloadProtector = payloadProtector;
    }

    public async ValueTask<WorkflowAlterationPlanState?> CaptureNextAsync(
        string planId,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        for (var retry = 0; retry < _options.MaxConcurrencyRetries; retry++)
        {
            var plan = await _store.FindPlanAsync(planId, cancellationToken);
            if (plan is null || plan.Status != WorkflowAlterationPlanStatus.CapturingTargets)
                return plan;

            var activityConcurrencyIds = ReadActivityConcurrencyIds(plan);
            var page = await ReadPageAsync(plan, pageSize, activityConcurrencyIds, cancellationToken);
            try
            {
                var captured = await _store.CaptureAsync(plan.PlanId, plan.Revision, page.Targets, page.NextCursor, cancellationToken);
                return page.HasNext
                    ? captured
                    : await _store.SealAsync(captured.PlanId, captured.Revision, _timeProvider.GetUtcNow(), cancellationToken);
            }
            catch (WorkflowAlterationConcurrencyException) when (retry < _options.MaxConcurrencyRetries - 1)
            {
                // Another capture delivery progressed the durable cursor. Re-read it rather than replaying the page.
                await Task.Delay(_options.GetRetryDelay(retry), _timeProvider, cancellationToken);
            }
        }

        return await _store.FailUnsealedCaptureAsync(
            planId,
            new WorkflowAlterationSafeFailure("CaptureRetryExhausted", "Target capture did not stabilize."),
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async ValueTask<CapturePage> ReadPageAsync(
        WorkflowAlterationPlanState plan,
        int pageSize,
        IReadOnlyCollection<string> activityConcurrencyIds,
        CancellationToken cancellationToken)
    {
        if (plan.Target.Query is { } query)
        {
            var page = await _workflowExecutions.QueryAlterationCapturePageAsync(
                new WorkflowExecutionAlterationCaptureQuery(plan.AuthorityScope.TenantPartition, query, pageSize, plan.CaptureCursor),
                cancellationToken);
            return new CapturePage(
                await ToCapturedTargetsAsync(page.Items, activityConcurrencyIds, cancellationToken),
                page.NextCursor,
                page.HasNext);
        }

        var start = DecodeExplicitCursor(plan.CaptureCursor);
        var executionIds = plan.Target.ExecutionIds.Skip(start).Take(pageSize).ToArray();
        var targets = new List<WorkflowAlterationCapturedTarget>(executionIds.Length);
        foreach (var executionId in executionIds)
        {
            var state = await _workflowExecutions.FindAsync(executionId, cancellationToken);
            targets.Add(state is not null &&
                        StringComparer.Ordinal.Equals(
                            state.TenantId ?? WorkflowExecutionPartition.DefaultValue,
                            plan.AuthorityScope.TenantPartition)
                ? await ToCapturedTargetAsync(state, activityConcurrencyIds, cancellationToken)
                : new WorkflowAlterationCapturedTarget(
                    executionId,
                    plan.AuthorityScope.TenantPartition,
                    safeFailure: new WorkflowAlterationSafeFailure("TargetNotFound", "The requested target is unavailable.")));
        }

        var next = start + executionIds.Length;
        return new CapturePage(targets, next < plan.Target.ExecutionIds.Count ? EncodeExplicitCursor(next) : null, next < plan.Target.ExecutionIds.Count);
    }

    private async ValueTask<IReadOnlyCollection<WorkflowAlterationCapturedTarget>> ToCapturedTargetsAsync(
        IReadOnlyCollection<WorkflowExecutionState> states,
        IReadOnlyCollection<string> activityConcurrencyIds,
        CancellationToken cancellationToken)
    {
        var targets = new List<WorkflowAlterationCapturedTarget>(states.Count);
        foreach (var state in states)
            targets.Add(await ToCapturedTargetAsync(state, activityConcurrencyIds, cancellationToken));
        return targets;
    }

    private async ValueTask<WorkflowAlterationCapturedTarget> ToCapturedTargetAsync(
        WorkflowExecutionState state,
        IReadOnlyCollection<string> activityConcurrencyIds,
        CancellationToken cancellationToken)
    {
        var activities = new List<ActivityExecutionState>(activityConcurrencyIds.Count);
        if (_activityExecutionStateStore is not null)
        {
            foreach (var activityExecutionId in activityConcurrencyIds)
            {
                var activity = await _activityExecutionStateStore.FindAsync(state.WorkflowExecutionId, activityExecutionId, cancellationToken);
                if (activity is not null)
                    activities.Add(activity);
            }
        }
        return new WorkflowAlterationCapturedTarget(
            state.WorkflowExecutionId,
            state.TenantId ?? WorkflowExecutionPartition.DefaultValue,
            new WorkflowAlterationCapturedConcurrency(
                WorkflowAlterationCapturedConcurrencyValidator.WorkflowRevision(state),
                state.RootVariableFrame?.Revision,
                state.PinnedExecutable,
                WorkflowAlterationCapturedConcurrencyValidator.CaptureActivities(activities)));
    }

    private IReadOnlyCollection<string> ReadActivityConcurrencyIds(WorkflowAlterationPlanState plan)
    {
        if (_payloadProtector is null)
            return [];

        var plaintext = _payloadProtector.Unprotect(
            plan.PlanId,
            plan.AuthorityScope.TenantPartition,
            plan.CanonicalRequestHash,
            plan.ProtectedPayload);
        using var document = JsonDocument.Parse(plaintext);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var envelope in document.RootElement.GetProperty("alterations").EnumerateArray())
        {
            var kind = envelope.GetProperty("kind").GetString();
            var payload = envelope.GetProperty("payload");
            var propertyName = kind switch
            {
                "ScheduleActivity" => "parentActivityExecutionId",
                "RescheduleActivity" => "sourceActivityExecutionId",
                _ => null
            };
            if (propertyName is not null &&
                payload.TryGetProperty(propertyName, out var activityId) &&
                activityId.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(activityId.GetString()))
            {
                ids.Add(activityId.GetString()!);
            }
        }

        return ids;
    }

    private static int DecodeExplicitCursor(string? cursor)
    {
        if (cursor is null)
            return 0;
        const string prefix = "explicit:";
        if (!cursor.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(cursor[prefix.Length..], out var start) || start < 0)
            throw new ArgumentException("The explicit alteration capture cursor is invalid.", nameof(cursor));
        return start;
    }

    private static string EncodeExplicitCursor(int start) => $"explicit:{start}";

    private sealed record CapturePage(
        IReadOnlyCollection<WorkflowAlterationCapturedTarget> Targets,
        string? NextCursor,
        bool HasNext);
}

public sealed class WorkflowAlterationCaptureRetryExhaustedException(string planId)
    : InvalidOperationException($"Alteration target capture for plan '{planId}' did not stabilize after bounded retries.");

/// <summary>Bounded optimistic-concurrency retry policy for one capture page delivery.</summary>
public sealed class WorkflowAlterationTargetCaptureOptions
{
    public int MaxConcurrencyRetries { get; init; } = 3;
    public TimeSpan InitialRetryBackoff { get; init; } = TimeSpan.FromMilliseconds(25);

    internal TimeSpan GetRetryDelay(int retry)
    {
        if (MaxConcurrencyRetries <= 0)
            throw new InvalidOperationException("Alteration target capture requires at least one concurrency attempt.");
        if (InitialRetryBackoff < TimeSpan.Zero)
            throw new InvalidOperationException("Alteration target capture retry backoff cannot be negative.");
        return TimeSpan.FromTicks(checked(InitialRetryBackoff.Ticks * (1L << retry)));
    }
}
