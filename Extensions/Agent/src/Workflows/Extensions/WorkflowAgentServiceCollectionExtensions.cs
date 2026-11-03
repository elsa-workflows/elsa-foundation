using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Workflows.Services;
using Elsa.Agent.Workflows.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Workflows.Extensions;

public static class WorkflowAgentServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationWorkflowsAgent(this IServiceCollection services)
    {
        // The agent harness/provider is a single-implementation contract: a concrete harness feature
        // (e.g. GitHub Copilot) registers the one IAgentProvider. The workflow feature only contributes
        // capabilities and context. DeterministicWorkflowAgentProvider stays a test-only seam.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentCapabilityProvider, WorkflowAgentCapabilityProvider>());
        // The context source reads the owning domains through their Core contracts only: the workflow definition
        // authorizer (Workflows Design) and the activity authoring catalog (Activities Design). It registers no
        // fallback for either; a missing owner is reported, never substituted.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentContextProvider, DefaultWorkflowAgentContextProvider>());

        // Workflow authoring tools the agent can call once the provider surfaces tool calls. They are policy-gated
        // by the tool invoker (read-only runs inline; mutating becomes a proposal).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, WorkflowReadGraphTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, WorkflowSetDesignerPositionTool>());
        // The reviewed Operation Batch for the open working draft (spec 1193 slice C). Applied only by the originating editor.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, WorkflowApplyOperationBatchTool>());

        return services;
    }
}
