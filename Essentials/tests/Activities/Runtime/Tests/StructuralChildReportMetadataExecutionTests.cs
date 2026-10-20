using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// A structural child that finishes from a child evaluation reports to its parent with the scheduling metadata its
/// parent gave it, not with the metadata it gave its own children (spec 258: a nested compensation scope stamps the
/// same routing keys as the scope around it).
/// </summary>
public sealed class StructuralChildReportMetadataExecutionTests
{
    private const string Key = "test.routing";

    [Fact]
    public async Task ACompletingStructuralChildReportsWithTheMetadataItsParentGaveIt()
    {
        var log = new ReportLog();
        await using var harness = NewHarness(log, new MiddleDirective(Fault: false));

        var run = await harness.RunAsync(Graph(), allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        Assert.Equal(["completed:node-middle:outer"], log.Entries);
    }

    [Fact]
    public async Task AFaultingStructuralChildReportsWithTheMetadataItsParentGaveIt()
    {
        var log = new ReportLog();
        await using var harness = NewHarness(log, new MiddleDirective(Fault: true));

        await harness.RunAsync(Graph(), allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);

        Assert.Equal(["faulted:node-middle:outer"], log.Entries);
    }

    private static WorkflowExecutable Graph() =>
        StructuralExecutionTestSupport.NewExecutable(
            StructuralExecutionTestSupport.NewStructuralNode("node-outer", typeof(StampingParentActivity),
                StructuralExecutionTestSupport.NewStructuralNode("node-middle", typeof(StampingMiddleActivity),
                    WorkflowExecutionHarness.NewProbeNode("node-leaf"))));

    private static WorkflowExecutionHarness NewHarness(ReportLog log, MiddleDirective directive) =>
        WorkflowExecutionHarness.Create()
            .ConfigureServices(services =>
            {
                services.AddSingleton(log);
                services.AddSingleton(directive);
            })
            .WithProbeLeaf()
            .Build("actexec-outer", "actexec-middle", "actexec-leaf");

    public sealed class ReportLog
    {
        private readonly List<string> _entries = [];
        public IReadOnlyList<string> Entries { get { lock (_entries) return _entries.ToArray(); } }
        public void Add(string entry) { lock (_entries) _entries.Add(entry); }
    }

    public sealed record MiddleDirective(bool Fault);

    /// <summary>Stamps <c>outer</c> on its child and records the routing value its child reports with.</summary>
    public sealed class StampingParentActivity(ReportLog log) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityChildFaultHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId,
                new Dictionary<string, string> { [Key] = "outer" });
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            log.Add($"completed:{context.CompletedChildExecutableNodeId}:{Routing(context.ParentContext)}");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            log.Add($"faulted:{context.FaultedChildExecutableNodeId}:{Routing(context.ParentContext)}");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        private static string? Routing(IActivityExecutionContext context) =>
            ((IRuntimeActivityExecutionContext)context).SchedulerWorkItem.CommandMetadata.GetValueOrDefault(Key);
    }

    /// <summary>Stamps <c>middle</c> on its own child, then completes or faults when that child completes.</summary>
    public sealed class StampingMiddleActivity(MiddleDirective directive) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId,
                new Dictionary<string, string> { [Key] = "middle" });
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(directive.Fault
                ? RuntimeStructuralContinuation.Faulted(new ActivityFault("test.middle-faulted", "The middle activity faulted."))
                : RuntimeStructuralContinuation.Complete());
    }
}
