using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Testing;

/// <summary>Test compositions around the activity invoke handler.</summary>
public static class InvokeHandlerInterruptions
{
    /// <summary>
    /// Stops the host the first time the invoke handler is about to run <paramref name="executableNodeId"/>: the drain
    /// is cancelled there, as a shutdown cancels it, and nothing the pass held in memory is committed. Once per
    /// service collection, so a host built from it stops once.
    /// </summary>
    public static void StopHostOnFirstInvokeOf(IServiceCollection services, string executableNodeId)
    {
        var stopped = new OneTime();
        Decorate(services, inner => new HostStoppingInvocationHandler(inner, executableNodeId, stopped));
    }

    /// <summary>Makes the invoke handler throw for one node, the way a broken handler or an unreadable payload does.</summary>
    public static void FailInvocationOf(IServiceCollection services, string executableNodeId) =>
        Decorate(services, inner => new FailingInvocationHandler(inner, executableNodeId));

    private static void Decorate(
        IServiceCollection services,
        Func<IWorkflowSchedulerWorkHandler, IWorkflowSchedulerWorkHandler> decorate)
    {
        var descriptor = services.Single(candidate =>
            candidate.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            candidate.ImplementationType == typeof(WorkflowInvokeActivitySchedulerWorkHandler));
        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(
            typeof(IWorkflowSchedulerWorkHandler),
            serviceProvider => decorate(ActivatorUtilities.CreateInstance<WorkflowInvokeActivitySchedulerWorkHandler>(serviceProvider)),
            descriptor.Lifetime));
    }

    private static bool Invokes(RuntimeSchedulerWorkItem workItem, string executableNodeId) =>
        workItem.Payload is { } payload &&
        payload.TryGetProperty(nameof(RuntimeInvokeActivityCommandPayload.ExecutableNodeId), out var nodeId) &&
        nodeId.GetString() == executableNodeId;

    private sealed class OneTime
    {
        private int _taken;

        public bool Take() => Interlocked.Exchange(ref _taken, 1) == 0;
    }

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
        OneTime stopped) : IWorkflowSchedulerWorkHandler
    {
        public string Name => inner.Name;

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => inner.CanHandle(workItem);

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            Invokes(workItem, executableNodeId) && stopped.Take()
                ? throw new OperationCanceledException("The host stopped during the pass.")
                : inner.HandleAsync(workItem, cancellationToken);
    }
}
