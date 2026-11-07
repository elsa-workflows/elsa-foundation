using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workflows.Runtime.Services.Executions;

// Both hosts stop detached work before disposing persistence; repeated host/shell stops share the provider's join.
internal sealed class InProcessWorkflowActorsLifecycle(InProcessWorkflowExecutionActorProvider provider) :
    IHostedService, IShellTerminator
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => provider.StopAsync(cancellationToken);

    public Task TerminateAsync(CancellationToken cancellationToken = default) => StopAsync(cancellationToken);

    internal static void AddTo(IServiceCollection services)
    {
        services.TryAddSingleton(InProcessWorkflowExecutionActorProvider.Create);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(InProcessWorkflowActorsLifecycle)))
            return;
        services.AddSingleton<InProcessWorkflowActorsLifecycle>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<InProcessWorkflowActorsLifecycle>());
        var index = services.Count(descriptor => descriptor.ServiceType == typeof(IShellTerminator));
        services.AddSingleton<IShellTerminator>(provider => provider.GetRequiredService<InProcessWorkflowActorsLifecycle>());
        // Tasks' Start/0 terminator stops recurring producers first; persistence remains usable at this boundary.
        services.AddSingleton(new ShellTerminatorRegistration(typeof(InProcessWorkflowActorsLifecycle),
            LifecyclePhase.Start, -1, index, IsExplicit: true, "in-process actor re-drive stop"));
    }
}
