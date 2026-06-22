using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.Weaver.Workflows.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Weaver.Workflows;

public static class WeaverWorkflowsStudioServiceCollectionExtensions
{
    public static IServiceCollection AddWeaverWorkflowsStudio(this IServiceCollection services)
    {
        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeWeaverWorkflowsStudioModule>();
    }
}
