using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Extensions;
using Elsa.Foundation.Workflows.Agent.Contracts;
using Elsa.Foundation.Workflows.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Workflows.Agent.Extensions;

public static class WorkflowAgentServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationWorkflowsAgent(this IServiceCollection services)
    {
        services.AddFoundationAgentAbstractions();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentCapabilityProvider, WorkflowAgentCapabilityProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentContextProvider, DefaultWorkflowAgentContextProvider>());
        services.TryAddScoped<IWorkflowAgentContextProvider, DefaultWorkflowAgentContextProvider>();
        services.TryAddScoped<IWorkflowRevisionProvider, DefaultWorkflowRevisionProvider>();
        services.TryAddScoped<IWorkflowChangePermissionEvaluator, DenyAllWorkflowChangePermissionEvaluator>();
        services.TryAddScoped<IWorkflowChangeProposalService, DefaultWorkflowChangeProposalService>();

        return services;
    }
}
