using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Middleware;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Builders;

public sealed class ActivityRuntimePipelineBuilder : RuntimePipelinePlanBuilder
{
    public ActivityRuntimePipelineBuilder() : base(RuntimePipelineKind.Activity, RuntimeActivityPipelineSlots.All)
    {
        UseBuiltIn<RuntimeActivityLoadStateMiddleware>(RuntimeActivityPipelineSlots.LoadState);
        UseBuiltIn<RuntimeActivityInputEvaluationMiddleware>(RuntimeActivityPipelineSlots.InputEvaluation);
        UseBuiltIn<RuntimeActivityInvokeMiddleware>(RuntimeActivityPipelineSlots.Invoke);
        UseBuiltIn<RuntimeActivityOutputCaptureMiddleware>(RuntimeActivityPipelineSlots.OutputCapture);
        UseBuiltIn<RuntimeActivitySchedulingMiddleware>(RuntimeActivityPipelineSlots.Scheduling);
        UseBuiltIn<RuntimeActivityCheckpointMiddleware>(RuntimeActivityPipelineSlots.Checkpoint);
        UseBuiltIn<RuntimeActivityPostCommitMiddleware>(RuntimeActivityPipelineSlots.PostCommit);
    }

    public ActivityRuntimePipelineBuilder Use<TMiddleware>(
        string slotName,
        int order = 0,
        string? name = null)
        where TMiddleware : IActivityRuntimeMiddleware
    {
        AddRegistration(typeof(TMiddleware), slotName, order, name, isBuiltIn: false);
        return this;
    }

    private void UseBuiltIn<TMiddleware>(string slotName)
        where TMiddleware : IActivityRuntimeMiddleware =>
        AddRegistration(typeof(TMiddleware), slotName, order: 0, name: null, isBuiltIn: true);
}
