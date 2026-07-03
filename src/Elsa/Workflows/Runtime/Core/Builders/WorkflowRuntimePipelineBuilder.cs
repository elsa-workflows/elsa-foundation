using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Middleware;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Builders;

public sealed class WorkflowRuntimePipelineBuilder : RuntimePipelinePlanBuilder
{
    public WorkflowRuntimePipelineBuilder() : base(RuntimePipelineKind.Workflow, RuntimeWorkflowPipelineSlots.All)
    {
        UseBuiltIn<RuntimeWorkflowLoadStateMiddleware>(RuntimeWorkflowPipelineSlots.LoadState);
        UseBuiltIn<RuntimeWorkflowInvokeMiddleware>(RuntimeWorkflowPipelineSlots.Invoke);
        UseBuiltIn<RuntimeWorkflowSchedulingMiddleware>(RuntimeWorkflowPipelineSlots.Scheduling);
        UseBuiltIn<RuntimeWorkflowCheckpointMiddleware>(RuntimeWorkflowPipelineSlots.Checkpoint);
        UseBuiltIn<RuntimeWorkflowPostCommitMiddleware>(RuntimeWorkflowPipelineSlots.PostCommit);
    }

    protected override Type MiddlewareInterfaceType => typeof(IWorkflowRuntimeMiddleware);

    public WorkflowRuntimePipelineBuilder Use<TMiddleware>(
        string slotName,
        int order = 0,
        string? name = null)
        where TMiddleware : IWorkflowRuntimeMiddleware
    {
        AddRegistration(typeof(TMiddleware), slotName, order, name, isBuiltIn: false);
        return this;
    }

    /// <summary>Replaces middleware <typeparamref name="TOld"/> (e.g. a built-in placeholder) with <typeparamref name="TNew"/> at the same placement.</summary>
    public WorkflowRuntimePipelineBuilder Replace<TOld, TNew>()
        where TOld : IWorkflowRuntimeMiddleware
        where TNew : IWorkflowRuntimeMiddleware
    {
        ReplaceRegistration(typeof(TOld), typeof(TNew));
        return this;
    }

    /// <summary>Removes middleware <typeparamref name="TMiddleware"/> from the pipeline.</summary>
    public WorkflowRuntimePipelineBuilder Remove<TMiddleware>()
        where TMiddleware : IWorkflowRuntimeMiddleware
    {
        RemoveRegistration(typeof(TMiddleware));
        return this;
    }

    private void UseBuiltIn<TMiddleware>(string slotName)
        where TMiddleware : IWorkflowRuntimeMiddleware =>
        AddRegistration(typeof(TMiddleware), slotName, order: 0, name: null, isBuiltIn: true);
}
