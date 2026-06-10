using Elsa.Workflows.Runtime.Core.Builders;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Middleware;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePipelineContractTests
{
    [Fact]
    public void WorkflowPipelineBuilder_RegistersMiddlewareByStableSlotAndOrder()
    {
        var plan = new WorkflowRuntimePipelineBuilder()
            .Use<CustomWorkflowSchedulingMiddleware>(RuntimeWorkflowPipelineSlots.Scheduling, order: -10, name: "custom-scheduler")
            .BuildPlan();

        var schedulingSteps = plan.Steps
            .Where(step => step.Slot.Name == RuntimeWorkflowPipelineSlots.Scheduling)
            .ToArray();

        Assert.Equal(RuntimePipelineKind.Workflow, plan.PipelineKind);
        Assert.Equal(typeof(CustomWorkflowSchedulingMiddleware), schedulingSteps[0].MiddlewareType);
        Assert.Equal(typeof(RuntimeWorkflowSchedulingMiddleware), schedulingSteps[1].MiddlewareType);
        Assert.Equal("custom-scheduler", schedulingSteps[0].Name);
        Assert.False(schedulingSteps[0].IsBuiltIn);
        Assert.True(schedulingSteps[1].IsBuiltIn);
    }

    [Fact]
    public void ActivityPipelineBuilder_RegistersMiddlewareByStableSlotAndOrder()
    {
        var plan = new ActivityRuntimePipelineBuilder()
            .Use<CustomActivityInvokeMiddleware>(RuntimeActivityPipelineSlots.Invoke, order: 10)
            .BuildPlan();

        var invokeSteps = plan.Steps
            .Where(step => step.Slot.Name == RuntimeActivityPipelineSlots.Invoke)
            .ToArray();

        Assert.Equal(RuntimePipelineKind.Activity, plan.PipelineKind);
        Assert.Equal(typeof(RuntimeActivityInvokeMiddleware), invokeSteps[0].MiddlewareType);
        Assert.Equal(typeof(CustomActivityInvokeMiddleware), invokeSteps[1].MiddlewareType);
        Assert.Equal(10, invokeSteps[1].Order);
    }

    [Fact]
    public void PipelinePlans_ExposeBuiltInPlaceholderSlots()
    {
        var workflowPlan = new WorkflowRuntimePipelineBuilder().BuildPlan();
        var activityPlan = new ActivityRuntimePipelineBuilder().BuildPlan();

        Assert.Equal(
            RuntimeWorkflowPipelineSlots.All.Select(slot => slot.Name),
            workflowPlan.Steps.Where(step => step.IsBuiltIn).Select(step => step.Slot.Name));
        Assert.Equal(
            RuntimeActivityPipelineSlots.All.Select(slot => slot.Name),
            activityPlan.Steps.Where(step => step.IsBuiltIn).Select(step => step.Slot.Name));
        Assert.Contains(workflowPlan.Steps, step => step.MiddlewareType == typeof(RuntimeWorkflowCheckpointMiddleware));
        Assert.Contains(activityPlan.Steps, step => step.MiddlewareType == typeof(RuntimeActivityOutputCaptureMiddleware));
    }

    [Fact]
    public void PipelineBuilders_RejectUnknownSlots()
    {
        var workflowBuilder = new WorkflowRuntimePipelineBuilder();
        var activityBuilder = new ActivityRuntimePipelineBuilder();

        Assert.Throws<ArgumentException>(() => workflowBuilder.Use<CustomWorkflowSchedulingMiddleware>(null!));
        Assert.Throws<ArgumentException>(() => activityBuilder.Use<CustomActivityInvokeMiddleware>(null!));
        Assert.Throws<ArgumentException>(() => workflowBuilder.Use<CustomWorkflowSchedulingMiddleware>("UnknownSlot"));
        Assert.Throws<ArgumentException>(() => activityBuilder.Use<CustomActivityInvokeMiddleware>("UnknownSlot"));
    }

    [Fact]
    public void WorkflowAndActivityMiddleware_UseDistinctContextTypes()
    {
        var workflowContextParameter = InvokeContextType<IWorkflowRuntimeMiddleware>(nameof(IWorkflowRuntimeMiddleware.InvokeAsync));
        var activityContextParameter = InvokeContextType<IActivityRuntimeMiddleware>(nameof(IActivityRuntimeMiddleware.InvokeAsync));

        Assert.Equal(typeof(WorkflowRuntimePipelineContext), workflowContextParameter);
        Assert.Equal(typeof(ActivityRuntimePipelineContext), activityContextParameter);
        Assert.NotEqual(workflowContextParameter, activityContextParameter);
    }

    private static Type InvokeContextType<TMiddleware>(string methodName)
    {
        var method = typeof(TMiddleware).GetMethod(methodName);

        Assert.NotNull(method);
        return method.GetParameters()[0].ParameterType;
    }

    private sealed class CustomWorkflowSchedulingMiddleware : WorkflowRuntimeMiddlewareBase;

    private sealed class CustomActivityInvokeMiddleware : ActivityRuntimeMiddlewareBase;
}
