using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Workflows.Services;
using Elsa.Weaver.Workflows.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Workflows.Extensions;

public static class WorkflowWeaverServiceCollectionExtensions
{
    public static IServiceCollection AddWeaverWorkflows(this IServiceCollection services)
    {
        // The agent harness/provider is a single-implementation contract: a concrete harness feature
        // (e.g. GitHub Copilot) registers the one IWeaverProvider. The workflow feature only contributes
        // capabilities and context. DeterministicWorkflowWeaverProvider stays a test-only seam.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverCapabilityProvider, WorkflowWeaverCapabilityProvider>());
        // The context source reads the owning domains through their Core contracts only: the workflow definition
        // authorizer (Workflows Design) and the activity authoring catalog (Activities Design). It registers no
        // fallback for either; a missing owner is reported, never substituted.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverContextProvider, DefaultWorkflowWeaverContextProvider>());

        // Workflow authoring tools the agent can call once the provider surfaces tool calls. They are policy-gated
        // by the tool invoker (read-only runs inline; mutating becomes a proposal).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowReadGraphTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowSetDesignerPositionTool>());
        // The reviewed Operation Batch for the open working draft (spec 1193 slice C). Applied only by the originating editor.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowApplyOperationBatchTool>());

        // The workflow library, create dialog and template list (spec 1193 slice E). The context source reads the library's
        // authority through Workflows Design Core (IWorkflowLibraryCommands) and registers no fallback for it.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverContextProvider, WorkflowLibraryContextProvider>());
        // Reviewed library writes through the library itself, per-target outcomes, review in every mode.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowLibraryAssignTagsTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowLibrarySetFavoriteTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowLibraryMoveToFolderTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowLibrarySaveViewTool>());
        // Reviewed, revertible changes to a page's own working state: the visible list and the open create dialog.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowLibrarySetListStateTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowCreateDialogPrefillTool>());

        // One exact persisted version, read-only (spec 1193 slice F): the context source reads it through Workflows Design Core
        // (IWorkflowVersionReader) under the owning definition authorizer, and registers no fallback for either.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverContextProvider, WorkflowVersionContextProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, WorkflowReadVersionTool>());

        return services;
    }
}
