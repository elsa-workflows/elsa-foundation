using System.Text.Json;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Builders;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Middleware;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Covers the module-contribution DX for the runtime execution pipelines: attribute-declared placement with explicit
/// override, the deterministic-ordering + collision contract, Replace/Remove, and the end-to-end DI path where a
/// contributed middleware actually runs through the feature-composed pipeline.
/// </summary>
public sealed class RuntimeMiddlewareContributionTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddActivityRuntimeMiddleware_UsesAttributePlacementByDefault()
    {
        var services = new ServiceCollection();
        services.AddActivityRuntimeMiddleware<AttributedActivityMiddleware>();

        var contribution = Assert.Single(services.BuildServiceProvider().GetServices<ActivityRuntimeMiddlewareContribution>());
        Assert.Equal(typeof(AttributedActivityMiddleware), contribution.MiddlewareType);
        Assert.Equal(RuntimeActivityPipelineSlots.Invoke, contribution.Slot);
        Assert.Equal(-100, contribution.Order);
        Assert.Equal("attributed", contribution.Name);
    }

    [Fact]
    public void AddActivityRuntimeMiddleware_ExplicitArgumentsOverrideAttribute()
    {
        var services = new ServiceCollection();
        services.AddActivityRuntimeMiddleware<AttributedActivityMiddleware>(slot: RuntimeActivityPipelineSlots.OutputCapture, order: 50, name: "override");

        var contribution = Assert.Single(services.BuildServiceProvider().GetServices<ActivityRuntimeMiddlewareContribution>());
        Assert.Equal(RuntimeActivityPipelineSlots.OutputCapture, contribution.Slot);
        Assert.Equal(50, contribution.Order);
        Assert.Equal("override", contribution.Name);
    }

    [Fact]
    public void AddActivityRuntimeMiddleware_ThrowsWhenNoPlacementIsAvailable()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddActivityRuntimeMiddleware<UnplacedActivityMiddleware>());
    }

    [Fact]
    public async Task ContributedActivityMiddleware_RunsThroughFeatureComposedPipeline()
    {
        // The real DI path: register through the feature, contribute a middleware, and dispatch a real work item through
        // the feature-built dispatcher/pipeline. Proves the extension surface is reachable end-to-end, not just by hand.
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddActivityRuntimeMiddleware<RecordingActivityMiddleware>(RuntimeActivityPipelineSlots.Invoke, order: -100);
        await using var provider = services.BuildServiceProvider();

        var dispatcher = provider.GetRequiredService<IRuntimeExecutionPipelineDispatcher>();
        var handler = new RecordingHandler();
        await dispatcher.DispatchAsync(NewWorkItem(WorkflowExecutionCommandKind.InvokeActivity), handler);

        Assert.Equal(1, provider.GetRequiredService<RecordingActivityMiddleware>().Invocations);
        Assert.Equal(["work-1"], handler.WorkItemIds);

        var plan = provider.GetRequiredService<IRuntimeActivityExecutionPipeline>().Plan;
        var contributed = Assert.Single(plan.Steps, step => step.MiddlewareType == typeof(RecordingActivityMiddleware));
        Assert.Equal(RuntimeActivityPipelineSlots.Invoke, contributed.Slot.Name);
        Assert.Equal(-100, contributed.Order);
        Assert.False(contributed.IsBuiltIn);
    }

    [Fact]
    public void BuildPlan_OrdersMiddlewareDeterministically_RegardlessOfRegistrationOrder()
    {
        var forward = new ActivityRuntimePipelineBuilder()
            .Use<FirstActivityMiddleware>(RuntimeActivityPipelineSlots.Invoke, order: -100)
            .Use<SecondActivityMiddleware>(RuntimeActivityPipelineSlots.Invoke, order: -50)
            .BuildPlan();
        var reversed = new ActivityRuntimePipelineBuilder()
            .Use<SecondActivityMiddleware>(RuntimeActivityPipelineSlots.Invoke, order: -50)
            .Use<FirstActivityMiddleware>(RuntimeActivityPipelineSlots.Invoke, order: -100)
            .BuildPlan();

        Assert.Equal(
            forward.Steps.Select(step => step.MiddlewareType),
            reversed.Steps.Select(step => step.MiddlewareType));

        var invokeSteps = forward.Steps.Where(step => step.Slot.Name == RuntimeActivityPipelineSlots.Invoke).ToArray();
        Assert.Equal(typeof(FirstActivityMiddleware), invokeSteps[0].MiddlewareType);
        Assert.Equal(typeof(SecondActivityMiddleware), invokeSteps[1].MiddlewareType);
        Assert.Equal(typeof(RuntimeActivityInvokeMiddleware), invokeSteps[2].MiddlewareType); // built-in at order 0, after the negatives
    }

    [Fact]
    public void BuildPlan_ThrowsWhenTwoDistinctMiddlewareShareSlotAndOrder()
    {
        var builder = new ActivityRuntimePipelineBuilder()
            .Use<FirstActivityMiddleware>(RuntimeActivityPipelineSlots.Invoke, order: 5)
            .Use<SecondActivityMiddleware>(RuntimeActivityPipelineSlots.Invoke, order: 5);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.BuildPlan());
        Assert.Contains("Invoke", exception.Message);
        Assert.Contains(nameof(FirstActivityMiddleware), exception.Message);
        Assert.Contains(nameof(SecondActivityMiddleware), exception.Message);
    }

    [Fact]
    public void BuildPlan_ThrowsWhenModuleCollidesWithBuiltInAtOrderZero()
    {
        var builder = new ActivityRuntimePipelineBuilder()
            .Use<FirstActivityMiddleware>(RuntimeActivityPipelineSlots.Invoke); // order defaults to 0, same as the built-in

        var exception = Assert.Throws<InvalidOperationException>(() => builder.BuildPlan());
        Assert.Contains(nameof(RuntimeActivityInvokeMiddleware), exception.Message);
        Assert.Contains("negative order", exception.Message);
    }

    [Fact]
    public void Builder_ReplaceSwapsBuiltInAtSamePlacement()
    {
        var plan = new ActivityRuntimePipelineBuilder()
            .Replace<RuntimeActivityInvokeMiddleware, FirstActivityMiddleware>()
            .BuildPlan();

        var invokeStep = Assert.Single(plan.Steps, step => step.Slot.Name == RuntimeActivityPipelineSlots.Invoke);
        Assert.Equal(typeof(FirstActivityMiddleware), invokeStep.MiddlewareType);
        Assert.Equal(0, invokeStep.Order);
        Assert.DoesNotContain(plan.Steps, step => step.MiddlewareType == typeof(RuntimeActivityInvokeMiddleware));
    }

    [Fact]
    public void Builder_RemoveDropsMiddleware_AndThrowsWhenAbsent()
    {
        var plan = new ActivityRuntimePipelineBuilder()
            .Remove<RuntimeActivityPostCommitMiddleware>()
            .BuildPlan();

        Assert.DoesNotContain(plan.Steps, step => step.MiddlewareType == typeof(RuntimeActivityPostCommitMiddleware));
        Assert.Throws<InvalidOperationException>(() => new ActivityRuntimePipelineBuilder().Remove<FirstActivityMiddleware>());
    }

    [Fact]
    public void Builder_UseRejectsTypeThatDoesNotImplementThePipelineContract()
    {
        var builder = new ActivityRuntimePipelineBuilder();

        Assert.Throws<ArgumentException>(() => builder.Use(typeof(RuntimeWorkflowLoadStateMiddleware), RuntimeActivityPipelineSlots.Invoke, -1));
    }

    private RuntimeSchedulerWorkItem NewWorkItem(WorkflowExecutionCommandKind commandKind)
    {
        using var document = JsonDocument.Parse("""{"workItemId":"work-1"}""");
        return new(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:command-1",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 1,
            payload: document.RootElement.Clone());
    }

    [RuntimeMiddleware(RuntimeActivityPipelineSlots.Invoke, Order = -100, Name = "attributed")]
    private sealed class AttributedActivityMiddleware : ActivityRuntimeMiddlewareBase;

    private sealed class UnplacedActivityMiddleware : ActivityRuntimeMiddlewareBase;

    private sealed class FirstActivityMiddleware : ActivityRuntimeMiddlewareBase;

    private sealed class SecondActivityMiddleware : ActivityRuntimeMiddlewareBase;

    private sealed class RecordingActivityMiddleware : ActivityRuntimeMiddlewareBase
    {
        public int Invocations { get; private set; }

        public override ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate next)
        {
            Invocations++;
            return next(context);
        }
    }

    private sealed class RecordingHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(RecordingHandler);
        public List<string> WorkItemIds { get; } = [];

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            WorkItemIds.Add(workItem.WorkItemId);
            return ValueTask.CompletedTask;
        }
    }
}
