using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Executables;
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
        IWorkflowExecutableSourceReferenceStore references, TimeProvider clock, IWorkflowTriggerBindingStore? bindings = null) =>
        new PublicationActivationSwitch(authority, references, clock,
            reservationState: null, reservationAccess: null, reservationPolicies: [], triggerBindingStore: bindings);
}
