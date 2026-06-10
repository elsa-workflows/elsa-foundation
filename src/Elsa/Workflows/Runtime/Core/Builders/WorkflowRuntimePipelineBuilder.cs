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
        UseBuiltIn<RuntimeWorkflowSchedulingMiddleware>(RuntimeWorkflowPipelineSlots.Scheduling);
        UseBuiltIn<RuntimeWorkflowCheckpointMiddleware>(RuntimeWorkflowPipelineSlots.Checkpoint);
        UseBuiltIn<RuntimeWorkflowPostCommitMiddleware>(RuntimeWorkflowPipelineSlots.PostCommit);
    }

    public WorkflowRuntimePipelineBuilder Use<TMiddleware>(
        string slotName,
        int order = 0,
        string? name = null)
        where TMiddleware : IWorkflowRuntimeMiddleware
    {
        AddRegistration(typeof(TMiddleware), slotName, order, name, isBuiltIn: false);
        return this;
    }

    private void UseBuiltIn<TMiddleware>(string slotName)
        where TMiddleware : IWorkflowRuntimeMiddleware =>
        AddRegistration(typeof(TMiddleware), slotName, order: 0, name: null, isBuiltIn: true);
}
