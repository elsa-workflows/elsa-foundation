using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// What the persistence-mode test activities report. Each test registers its own as a singleton, so a count or a
/// one-time action belongs to that test's host alone.
/// </summary>
public sealed class ActivityRunLog
{
    private int _executions;
    private int _ownershipTaken;
    private int _hostStopped;

    /// <summary>How often <see cref="CountingReplaySafeActivity"/> ran.</summary>
    public int Executions => Volatile.Read(ref _executions);

    internal void RecordExecution() => Interlocked.Increment(ref _executions);

    internal bool TakeOwnershipOnce() => Interlocked.Exchange(ref _ownershipTaken, 1) == 0;

    internal bool StopHostOnce() => Interlocked.Exchange(ref _hostStopped, 1) == 0;
}

/// <summary>A ReplaySafe leaf that counts how often it runs, so a test can tell a run that executed once from one that was started again.</summary>
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class CountingReplaySafeActivity(ActivityRunLog runs) : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        runs.RecordExecution();
        return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }
}

/// <summary>
/// A ReplaySafe leaf that takes its run's ownership the first time it runs, the way another node does when it
/// considers this node gone.
/// </summary>
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class LeaseStealingReplaySafeActivity(IRuntimeExecutionOwnershipService ownership, ActivityRunLog runs) : Activity<ActivityUnit>
{
    protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (runs.TakeOwnershipOnce())
            await ownership.AcquireAsync(context.WorkflowExecutionId);
        return ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done);
    }
}

/// <summary>Test compositions around the activity invoke handler.</summary>
public static class InvokeHandlerInterruptions
{
    /// <summary>
    /// Stops the host the first time the invoke handler is about to run <paramref name="executableNodeId"/>: the drain
    /// is cancelled there, as a shutdown cancels it, and nothing the pass held in memory is committed.
    /// </summary>
    public static void StopHostOnFirstInvokeOf(IServiceCollection services, string executableNodeId) =>
        Decorate(services, (inner, serviceProvider) => new HostStoppingInvocationHandler(
            inner, executableNodeId, serviceProvider.GetRequiredService<ActivityRunLog>()));

    /// <summary>Makes the invoke handler throw for one node, the way a broken handler or an unreadable payload does.</summary>
    public static void FailInvocationOf(IServiceCollection services, string executableNodeId) =>
        Decorate(services, (inner, _) => new FailingInvocationHandler(inner, executableNodeId));

    private static void Decorate(
        IServiceCollection services,
        Func<IWorkflowSchedulerWorkHandler, IServiceProvider, IWorkflowSchedulerWorkHandler> decorate)
    {
        var descriptor = services.Single(candidate =>
            candidate.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            candidate.ImplementationType == typeof(WorkflowInvokeActivitySchedulerWorkHandler));
        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(
            typeof(IWorkflowSchedulerWorkHandler),
            serviceProvider => decorate(
                ActivatorUtilities.CreateInstance<WorkflowInvokeActivitySchedulerWorkHandler>(serviceProvider),
                serviceProvider),
            descriptor.Lifetime));
    }

    private static bool Invokes(RuntimeSchedulerWorkItem workItem, string executableNodeId) =>
        workItem.Payload is { } payload &&
        payload.TryGetProperty(nameof(RuntimeInvokeActivityCommandPayload.ExecutableNodeId), out var nodeId) &&
        nodeId.GetString() == executableNodeId;

    private sealed class FailingInvocationHandler(IWorkflowSchedulerWorkHandler inner, string executableNodeId) : IWorkflowSchedulerWorkHandler
    {
        public string Name => inner.Name;

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => inner.CanHandle(workItem);

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            Invokes(workItem, executableNodeId)
                ? throw new InvalidOperationException($"The invoke handler failed for node '{executableNodeId}'.")
                : inner.HandleAsync(workItem, cancellationToken);
    }

    private sealed class HostStoppingInvocationHandler(
        IWorkflowSchedulerWorkHandler inner,
        string executableNodeId,
        ActivityRunLog runs) : IWorkflowSchedulerWorkHandler
    {
        public string Name => inner.Name;

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => inner.CanHandle(workItem);

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            Invokes(workItem, executableNodeId) && runs.StopHostOnce()
                ? throw new OperationCanceledException("The host stopped during the pass.")
                : inner.HandleAsync(workItem, cancellationToken);
    }
}
