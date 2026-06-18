using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.Workflows.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Workflows;

public static class WorkflowsStudioServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowsStudio(this IServiceCollection services)
    {
        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeWorkflowsStudioModule>();
    }
}
