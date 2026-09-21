using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Explicit complete traversals for commands whose business semantics require every matching record.</summary>
public static class RuntimeOperationalStorePagingExtensions
{
    public static async ValueTask<IReadOnlyList<ExecutionLivenessState>> ListAllAsync(
        this IExecutionLivenessStateStore store,
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var states = new List<ExecutionLivenessState>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListPageAsync(
                new ExecutionLivenessStatePageQuery(
                    workflowExecutionId,
                    RuntimeStorePageRequest.MaximumLimit,
                    continuationToken),
                cancellationToken);
            states.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return states;
    }

    public static async ValueTask<IReadOnlyList<ExecutionLivenessState>> ListAllAsync(
        this IExecutionLivenessStateStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var states = new List<ExecutionLivenessState>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListAllPageAsync(
                new RuntimeStorePageRequest(
                    RuntimeStorePageRequest.MaximumLimit,
                    continuationToken),
                cancellationToken);
            states.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return states;
    }

    public static async ValueTask<IReadOnlyList<DurableTimer>> ListAllAsync(
        this IDurableTimerStore store,
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var timers = new List<DurableTimer>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListPageAsync(
                new DurableTimerPageQuery(
                    workflowExecutionId,
                    RuntimeStorePageRequest.MaximumLimit,
                    continuationToken),
                cancellationToken);
            timers.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return timers;
    }

    public static async ValueTask<IReadOnlyList<RecurringTriggerSchedule>> ListAllByActivationAsync(
        this IRecurringTriggerScheduleStore store,
        string activationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var schedules = new List<RecurringTriggerSchedule>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListByActivationPageAsync(
                new RecurringTriggerScheduleActivationPageQuery(
                    activationId,
                    RuntimeStorePageRequest.MaximumLimit,
                    continuationToken),
                cancellationToken);
            schedules.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return schedules;
    }

    public static async ValueTask<IReadOnlyList<RecurringTriggerSchedule>> ListAllByArtifactAsync(
        this IRecurringTriggerScheduleStore store,
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var schedules = new List<RecurringTriggerSchedule>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListByArtifactPageAsync(
                new RecurringTriggerScheduleArtifactPageQuery(
                    artifactId,
                    RuntimeStorePageRequest.MaximumLimit,
                    continuationToken),
                cancellationToken);
            schedules.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return schedules;
    }
}
