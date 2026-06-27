using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Extensions;
using Elsa.Agent.Workflows.Contracts;
using Elsa.Agent.Workflows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Workflows.Extensions;

public static class WorkflowAgentServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationWorkflowsAgent(this IServiceCollection services)
    {
        services.AddFoundationAgentAbstractions();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentProvider, DeterministicWorkflowAgentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentCapabilityProvider, WorkflowAgentCapabilityProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentContextProvider, DefaultWorkflowAgentContextProvider>());
        services.TryAddScoped<IWorkflowAgentContextProvider, DefaultWorkflowAgentContextProvider>();
        services.TryAddScoped<IWorkflowActivityCatalogProvider, DefaultWorkflowActivityCatalogProvider>();
        services.TryAddScoped<IWorkflowRevisionProvider, DefaultWorkflowRevisionProvider>();
        services.TryAddScoped<IWorkflowChangePermissionEvaluator, DenyAllWorkflowChangePermissionEvaluator>();
        services.TryAddScoped<IWorkflowChangeProposalService, DefaultWorkflowChangeProposalService>();
        services.TryAddScoped<IWorkflowGraphOperationBatchRiskClassifier, DefaultWorkflowGraphOperationBatchRiskClassifier>();
        services.TryAddScoped<IWorkflowAuthoringAuditService, DefaultWorkflowAuthoringAuditService>();

        return services;
    }
}
