using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

/// <summary>
/// Decorates the in-memory <see cref="IWorkflowActivationSwitch"/> a host composes, which moves every slot (#2230), so a
/// test can fail or race a slot transition where the runtime makes it.
/// </summary>
internal static class ActivationSwitchDecoration
{
    public static void Decorate(this IServiceCollection services, Func<IServiceProvider, IWorkflowActivationSwitch, IWorkflowActivationSwitch> decorate)
    {
        var original = services.Single(item => item.ServiceType == typeof(IWorkflowActivationSwitch));
        services.Remove(original);
        services.AddScoped(sp => decorate(sp, (IWorkflowActivationSwitch)
            ActivatorUtilities.CreateInstance(sp, original.ImplementationType!)));
    }

    public static IWorkflowActivationSwitch Create(IWorkflowActivationAuthority authority,
        IWorkflowExecutableSourceReferenceStore references, TimeProvider clock, IWorkflowTriggerBindingStore? bindings = null)
    {
        var services = new ServiceCollection();
        WorkflowPublicationDefaults.Add(services);
        var implementation = services.Single(item => item.ServiceType == typeof(IWorkflowActivationSwitch)).ImplementationType
            ?? throw new InvalidOperationException("Workflow publication defaults must register an activation switch implementation.");

        // These tests construct an isolated switch over their exact stores, without a host reservation scope.
        return (IWorkflowActivationSwitch)Activator.CreateInstance(implementation,
            [authority, references, clock, null, null, Array.Empty<IIngressReservationPolicy>(), bindings, null])!;
    }
}
