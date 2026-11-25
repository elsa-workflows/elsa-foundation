using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class StructuralJsonWarningDurabilityTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task Callback_conversion_warnings_survive_completion_cancellation_and_returned_faults(bool notification, bool fault, bool complete)
    {
        await using var harness = WorkflowExecutionHarness.Create().WithProbeLeaf()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new Directive(notification, fault, complete));
                services.AddSingleton(new ParentNotificationDirective { InvokeCodes = ["convert"] });
            }).Build("actexec-parent", "actexec-child", "actexec-leaf");
        var child = notification
            ? StructuralExecutionTestSupport.NewStructuralNode("node-child", typeof(NotifyingStructuralChildActivity), WorkflowExecutionHarness.NewProbeNode("node-leaf"))
            : WorkflowExecutionHarness.NewProbeNode("node-child");
        var executable = WorkflowExecutionHarness.NewExecutable(
            StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(WarnedParent), child));
        // A notification can cancel the root before its child's already queued work is drained.
        var run = await harness.RunAsync(executable, allowPendingWorkOnTerminalCompletion: notification && !fault);
        var parent = run.State("node-parent");
        Assert.Equal(complete ? ActivityExecutionStatus.Completed : fault ? ActivityExecutionStatus.Faulted : ActivityExecutionStatus.Cancelled, parent.Status);
        if (notification && !fault && !complete)
        {
            Assert.Equal(WorkflowExecutionStatus.Cancelled, run.WorkflowState!.Status);
            Assert.Single(await harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                .ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId)));
        }
        var warning = Assert.Single(parent.Warnings!);
        Assert.Equal("json-unmatched-member", warning.Code);
        Assert.Equal("args.order[\"NAME\"]", warning.Details!["path"]);
        var persisted = await harness.Services.GetRequiredService<IActivityExecutionStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, parent.Execution.ActivityExecutionId);
        Assert.Equal(warning, Assert.Single(persisted!.Warnings!));
    }

    public sealed record Directive(bool Notification, bool Fault, bool Complete);
    public sealed class WarnedParent(Directive directive) : StructuralActivity, IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildNotificationHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            context.ScheduleChildActivity(Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities).ExecutableNodeId,
                context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }
        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(directive.Notification ? RuntimeStructuralContinuation.Defer : ConvertAndContinue((IRuntimeActivityExecutionContext)context.ParentContext));
        public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification) =>
            ValueTask.FromResult(ConvertAndContinue(context));
        private RuntimeStructuralContinuation ConvertAndContinue(IRuntimeActivityExecutionContext context)
        {
            using var document = JsonDocument.Parse("""{"NAME":"Ada"}""");
            _ = JsonConversionContract.For(typeof(Order)).Read(document.RootElement, typeof(Order), diagnostic =>
                context.Warnings?.Report(ExecutionWarning.FromJsonConversion(diagnostic.Code, diagnostic.Path, diagnostic.TargetType, diagnostic.Converter, "args.order")));
            return directive.Complete ? RuntimeStructuralContinuation.Complete()
                : directive.Fault ? RuntimeStructuralContinuation.Faulted(new ActivityFault("test.conversion.fault", "returned fault"))
                : RuntimeStructuralContinuation.Cancel("returned cancellation");
        }
    }
    private sealed record Order(string? Name);
}
