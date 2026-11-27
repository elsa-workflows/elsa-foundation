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
        services.RemoveAll<IWorkflowActivationSwitch>();
        services.AddScoped(sp => decorate(sp, ActivatorUtilities.CreateInstance<InMemoryWorkflowActivationSwitch>(sp)));
    }
}
