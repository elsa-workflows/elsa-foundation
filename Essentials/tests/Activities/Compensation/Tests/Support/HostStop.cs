using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>
/// Stops a host once, when a matching work item is about to be evaluated: nothing of that evaluation commits, and the
/// work item stays queued for the next host generation. This is the "committed, not yet evaluated" crash.
/// </summary>
public static class HostStop
{
    /// <summary>Before the first <paramref name="code"/> notification reaches its scope.</summary>
    public static Action<IServiceCollection> BeforeNotification(string code) =>
        services => Install<WorkflowNotifyParentActivitySchedulerWorkHandler>(services, workItem =>
            workItem.Payload is { } payload && payload.GetRawText().Contains($"\"{code}\"", StringComparison.Ordinal));

    /// <summary>Before the first descendant-fault delivery reaches its receiver (spec 258 R-3).</summary>
    public static Action<IServiceCollection> BeforeDescendantFault() =>
        services => Install<WorkflowParentActivityCompletionSchedulerWorkHandler>(services, workItem =>
            workItem.CommandMetadata.ContainsKey(RuntimeMetadataKeys.DescendantFault));

    /// <summary>The hand-over notification code, for <see cref="BeforeNotification"/>.</summary>
    public static string Register => CompensationScope.Notifications.Register;

    private static void Install<THandler>(IServiceCollection services, Func<RuntimeSchedulerWorkItem, bool> matches) where THandler : IWorkflowSchedulerWorkHandler
    {
        var descriptor = services.Single(candidate =>
            candidate.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            candidate.ImplementationType == typeof(THandler));
        services.Remove(descriptor);
        var stopped = 0;
        services.Add(ServiceDescriptor.Describe(
            typeof(IWorkflowSchedulerWorkHandler),
            serviceProvider => new StoppingHandler(
                ActivatorUtilities.CreateInstance<THandler>(serviceProvider),
                workItem => matches(workItem) && Interlocked.Exchange(ref stopped, 1) == 0),
            descriptor.Lifetime));
    }

    private sealed class StoppingHandler(IWorkflowSchedulerWorkHandler inner, Func<RuntimeSchedulerWorkItem, bool> stopNow) : IWorkflowSchedulerWorkHandler
    {
        public string Name => inner.Name;

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => inner.CanHandle(workItem);

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            stopNow(workItem)
                ? throw new OperationCanceledException("The host stopped during the pass.")
                : inner.HandleAsync(workItem, cancellationToken);
    }
}
