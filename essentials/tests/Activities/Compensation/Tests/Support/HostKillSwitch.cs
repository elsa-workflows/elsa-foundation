using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>
/// Stops a host in the middle of an activity invocation, after the activity's body ran and before anything the pass
/// produced is committed: the invoke handler's cancellation token is cancelled and the invocation ends as a shutdown
/// ends it. This is the "effect applied, completion not committed" crash.
/// </summary>
public sealed class HostKillSwitch
{
    private CancellationTokenSource _source = new();

    public CancellationToken Token => _source.Token;

    public void Kill() => _source.Cancel();

    /// <summary>Routes every invoke through a token the switch can cancel. One switch per host generation.</summary>
    public static void Install(IServiceCollection services, HostKillSwitch killSwitch)
    {
        services.AddSingleton(killSwitch);
        var descriptor = services.Single(candidate =>
            candidate.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            candidate.ImplementationType == typeof(WorkflowInvokeActivitySchedulerWorkHandler));
        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(
            typeof(IWorkflowSchedulerWorkHandler),
            serviceProvider => new KillableInvokeHandler(
                ActivatorUtilities.CreateInstance<WorkflowInvokeActivitySchedulerWorkHandler>(serviceProvider),
                killSwitch),
            descriptor.Lifetime));
    }

    private sealed class KillableInvokeHandler(IWorkflowSchedulerWorkHandler inner, HostKillSwitch killSwitch) : IWorkflowSchedulerWorkHandler
    {
        public string Name => inner.Name;

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => inner.CanHandle(workItem);

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, killSwitch.Token);
            await inner.HandleAsync(workItem, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
        }
    }
}
