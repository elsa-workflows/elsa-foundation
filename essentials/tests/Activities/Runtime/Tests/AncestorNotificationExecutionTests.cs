using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// An ancestor notification reaches the nearest committed ancestor with the requested consumer key, skipping every
/// composite in between, and is evaluated before the requester's direct parent sees the requester complete.
/// </summary>
public sealed class AncestorNotificationExecutionTests
{
    private const string AncestorConsumerKey = "test/ancestor";

    [Fact]
    public async Task NotificationSkipsIntermediateCompositeAndReachesAncestor()
    {
        var log = new AncestorNotificationLog();
        await using var harness = NewHarness(log, new AncestorDirective { CancelLiveChildrenOnNotify = true }, "actexec-ancestor", "actexec-relay", "actexec-notifier");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                AncestorNode("node-ancestor",
                    StructuralExecutionTestSupport.NewStructuralNode("node-relay", typeof(RelayActivity),
                        LeafNode("node-notifier")))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-ancestor");
        run.AssertCompleted("node-notifier");
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("node-relay").Status);
        Assert.Equal(["ancestor:node-ancestor:notified:exit:node-notifier"], log.Entries);
    }

    [Fact]
    public async Task NotificationIsEvaluatedBeforeTheRequestersDirectParentSeesItComplete()
    {
        var log = new AncestorNotificationLog();
        await using var harness = NewHarness(log, new AncestorDirective(), "actexec-ancestor", "actexec-relay", "actexec-notifier");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                AncestorNode("node-ancestor",
                    StructuralExecutionTestSupport.NewStructuralNode("node-relay", typeof(RelayActivity),
                        LeafNode("node-notifier")))),
            allowPendingWorkOnTerminalCompletion: true);

        // The ancestor does not cancel here, so the relay still receives the late completion — but only after the
        // ancestor has evaluated the notification.
        Assert.Equal(
            ["ancestor:node-ancestor:notified:exit:node-notifier", "relay:node-relay:child-completed:node-notifier"],
            log.Entries.Take(2));
    }

    [Fact]
    public async Task WhenTheAncestorIsTheDirectParentTheLateCompletionIsDropped()
    {
        var log = new AncestorNotificationLog();
        await using var harness = NewHarness(log, new AncestorDirective(), "actexec-ancestor", "actexec-notifier");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(AncestorNode("node-ancestor", LeafNode("node-notifier"))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-ancestor");
        run.AssertCompleted("node-notifier");
        Assert.Equal(["ancestor:node-ancestor:notified:exit:node-notifier"], log.Entries);
    }

    [Fact]
    public async Task OnlyTheNearestMatchingAncestorIsNotified()
    {
        var log = new AncestorNotificationLog();
        await using var harness = NewHarness(log, new AncestorDirective { CancelLiveChildrenOnNotify = true }, "actexec-outer", "actexec-relay", "actexec-inner", "actexec-notifier");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                AncestorNode("node-outer",
                    StructuralExecutionTestSupport.NewStructuralNode("node-relay", typeof(RelayActivity),
                        AncestorNode("node-inner", LeafNode("node-notifier"))))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        Assert.Equal(
            ["ancestor:node-inner:notified:exit:node-notifier", "relay:node-relay:child-completed:node-inner", "ancestor:node-outer:child-completed:node-relay"],
            log.Entries);
    }

    [Fact]
    public async Task NoMatchingAncestorFaultsTheRequesterWithAStableCode()
    {
        var log = new AncestorNotificationLog();
        await using var harness = NewHarness(log, new AncestorDirective(), "actexec-relay", "actexec-notifier");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-relay", typeof(RelayActivity), LeafNode("node-notifier"))),
            allowPendingWorkOnTerminalCompletion: true);

        var notifier = run.State("node-notifier");
        Assert.Equal(ActivityExecutionStatus.Faulted, notifier.Status);
        Assert.Equal("runtime.ancestor-notification.target-not-found", notifier.Fault!.Code);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task ANotificationToAnAncestorThatStoppedRunningIsDropped()
    {
        var log = new AncestorNotificationLog();
        await using var harness = NewHarness(log, new AncestorDirective(), "actexec-ancestor", "actexec-fork", "actexec-first", "actexec-second");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                AncestorNode("node-ancestor",
                    StructuralExecutionTestSupport.NewStructuralNode("node-fork", typeof(RelayForkActivity),
                        LeafNode("node-first"),
                        LeafNode("node-second")))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-ancestor");
        Assert.Single(log.Entries, entry => entry.StartsWith("ancestor:node-ancestor:notified:", StringComparison.Ordinal));
        Assert.Empty(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
    }

    [Fact]
    public async Task AnAncestorNotificationWithADeferContinuationFaultsTheEvaluation()
    {
        var log = new AncestorNotificationLog();
        await using var harness = NewHarness(log, new AncestorDirective(), "actexec-ancestor", "actexec-notifier", "actexec-leaf");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                AncestorNode("node-ancestor",
                    StructuralExecutionTestSupport.NewStructuralNode("node-notifier", typeof(DeferringAncestorNotifierActivity),
                        WorkflowExecutionHarness.NewProbeNode("node-leaf")))),
            allowPendingWorkOnTerminalCompletion: true);

        var notifier = run.State("node-notifier");
        Assert.Equal(ActivityExecutionStatus.Faulted, notifier.Status);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task AChildCompletionEvaluationThatCompletesMayNotifyAnAncestor_AheadOfItsOwnCompletion()
    {
        var log = new AncestorNotificationLog();
        await using var harness = NewHarness(log, new AncestorDirective(), "actexec-ancestor", "actexec-relay", "actexec-notifier", "actexec-leaf");

        var run = await harness.RunAsync(CompletionNotifierGraph(typeof(CompletingAncestorNotifierActivity)), allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-notifier");
        // Spec 258 R-2: the ancestor evaluates the notification before the relay sees the notifier complete.
        Assert.Equal(
            ["ancestor:node-ancestor:notified:exit:node-notifier", "relay:node-relay:child-completed:node-notifier"],
            log.Entries.Take(2));
    }

    [Fact]
    public async Task AChildCompletionEvaluationThatDefersMayNotNotifyAnAncestor()
    {
        var log = new AncestorNotificationLog();
        await using var harness = NewHarness(log, new AncestorDirective(), "actexec-ancestor", "actexec-relay", "actexec-notifier", "actexec-leaf");

        var run = await harness.RunAsync(CompletionNotifierGraph(typeof(DeferringCompletionNotifierActivity)), allowPendingWorkOnTerminalCompletion: true);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-notifier").Status);
        Assert.DoesNotContain(log.Entries, entry => entry.Contains(":notified:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AChildFaultEvaluationMayNotNotifyAnAncestor()
    {
        var log = new AncestorNotificationLog();
        await using var harness = NewHarness(log, new AncestorDirective(), "actexec-ancestor", "actexec-relay", "actexec-notifier", "actexec-leaf");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                AncestorNode("node-ancestor",
                    StructuralExecutionTestSupport.NewStructuralNode("node-relay", typeof(RelayActivity),
                        StructuralExecutionTestSupport.NewStructuralNode("node-notifier", typeof(FaultEvaluationNotifierActivity),
                            WorkflowExecutionHarness.NewFaultingNode("node-leaf"))))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-notifier").Status);
        Assert.DoesNotContain(log.Entries, entry => entry.Contains(":notified:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AChildCompletionEvaluationWithNoMatchingAncestorFaultsTheRequesterWithTheStableCode()
    {
        var log = new AncestorNotificationLog();
        await using var harness = NewHarness(log, new AncestorDirective(), "actexec-relay", "actexec-notifier", "actexec-leaf");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-relay", typeof(RelayActivity),
                    StructuralExecutionTestSupport.NewStructuralNode("node-notifier", typeof(CompletingAncestorNotifierActivity),
                        WorkflowExecutionHarness.NewProbeNode("node-leaf")))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        var notifier = run.State("node-notifier");
        Assert.Equal(ActivityExecutionStatus.Faulted, notifier.Status);
        Assert.Equal("runtime.ancestor-notification.target-not-found", notifier.Fault!.Code);
    }

    [Fact]
    public async Task AHostStopBetweenTheNotificationAndTheCompletionDeliversEachOnceAfterRestart()
    {
        var log = new AncestorNotificationLog();
        await using var harness = WorkflowExecutionHarness.Create()
            .ConfigureServices(services =>
            {
                services.AddSingleton(log);
                services.AddSingleton(new AncestorDirective());
                services.AddScoped<IActivityActivationStrategy, AncestorActivationStrategy>();
                services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                StopHostOnFirstNotification(services);
            })
            .Build("actexec-ancestor", "actexec-relay", "actexec-notifier", "actexec-leaf");

        var executable = CompletionNotifierGraph(typeof(CompletingAncestorNotifierActivity));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.RunAsync(executable, allowPendingWorkOnTerminalCompletion: true));
        Assert.DoesNotContain(log.Entries, entry => entry.Contains(":notified:", StringComparison.Ordinal));
        // The notifier's completion, the notification and the relay's completion form one commit unit, so the stop
        // discards all three: the notifier is still running and the leaf's completion is still queued, unclaimed. No
        // claim was taken on the durable queue, so the restart has no visibility timeout to wait out; it replays the
        // leaf's completion and delivers the notification and the completion afresh.
        var stopped = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.Equal(ActivityExecutionStatus.Running, stopped.State("node-notifier").Status);
        Assert.Equal(ActivityExecutionStatus.Completed, stopped.State("node-leaf").Status);

        await harness.SweepUntilQuietAsync();

        var run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        // The ancestor completes on the notification, so the relay's later completion reaches a finished ancestor.
        Assert.Equal(
            ["ancestor:node-ancestor:notified:exit:node-notifier", "relay:node-relay:child-completed:node-notifier"],
            log.Entries);
    }

    private static WorkflowExecutable CompletionNotifierGraph(Type notifierType) =>
        StructuralExecutionTestSupport.NewExecutable(
            AncestorNode("node-ancestor",
                StructuralExecutionTestSupport.NewStructuralNode("node-relay", typeof(RelayActivity),
                    StructuralExecutionTestSupport.NewStructuralNode("node-notifier", notifierType,
                        WorkflowExecutionHarness.NewProbeNode("node-leaf")))));

    private static void StopHostOnFirstNotification(IServiceCollection services)
    {
        var descriptor = services.Single(candidate =>
            candidate.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            candidate.ImplementationType == typeof(WorkflowNotifyParentActivitySchedulerWorkHandler));
        services.Remove(descriptor);
        var stopped = 0;
        services.Add(ServiceDescriptor.Describe(
            typeof(IWorkflowSchedulerWorkHandler),
            serviceProvider => new HostStoppingNotificationHandler(
                ActivatorUtilities.CreateInstance<WorkflowNotifyParentActivitySchedulerWorkHandler>(serviceProvider),
                () => Interlocked.Exchange(ref stopped, 1) == 0),
            descriptor.Lifetime));
    }

    private sealed class HostStoppingNotificationHandler(IWorkflowSchedulerWorkHandler inner, Func<bool> stopNow) : IWorkflowSchedulerWorkHandler
    {
        public string Name => inner.Name;

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => inner.CanHandle(workItem);

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            stopNow()
                ? throw new OperationCanceledException("The host stopped during the pass.")
                : inner.HandleAsync(workItem, cancellationToken);
    }

    private static WorkflowExecutionHarness NewHarness(AncestorNotificationLog log, AncestorDirective directive, params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .ConfigureServices(services =>
            {
                services.AddSingleton(log);
                services.AddSingleton(directive);
                services.AddScoped<IActivityActivationStrategy, AncestorActivationStrategy>();
            })
            .Build(activityExecutionIds);

    // The ancestor is activated from its own consumer key, as a graph boundary is from elsa.graph-activity; a CLR
    // node would be pinned to elsa.clr-activity by the harness and could never be addressed by key.
    private static ExecutableNode AncestorNode(string nodeId, params ExecutableNode[] children)
    {
        var payload = JsonSerializer.SerializeToElement(new { type = "ancestor" });
        var contract = new ActivityContract(
            "test-ancestor",
            "1.0.0",
            AncestorConsumerKey,
            payload,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Object"),
                true,
                ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result },
                []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(AncestorConsumerKey, AncestorConsumerKey));

        return new ExecutableNode(
            nodeId,
            $"authored-{nodeId}",
            "test-ancestor",
            "1.0.0",
            new RuntimeActivityDescriptor(AncestorConsumerKey, RuntimeActivityDescriptor.InitialSchemaVersion, payload),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            [new ExecutableChildSlot(StructuralExecutionTestSupport.ChildSlotName, children)],
            structure: null,
            activityContract: contract);
    }

    private sealed class AncestorActivationStrategy(IServiceScopeFactory scopeFactory) : IActivityActivationStrategy
    {
        public string ConsumerKey => AncestorConsumerKey;
        public IReadOnlyCollection<string> SupportedSchemaVersions => [RuntimeActivityDescriptor.InitialSchemaVersion];
        public bool RequiresInputHydration => false;

        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationStrategyRequest request, CancellationToken cancellationToken = default)
        {
            var scope = scopeFactory.CreateAsyncScope();
            var activity = ActivatorUtilities.CreateInstance<AncestorTargetActivity>(scope.ServiceProvider);
            return ValueTask.FromResult(new ActivityActivationLease(activity, scope));
        }
    }

    private static ExecutableNode LeafNode(string nodeId) =>
        StructuralExecutionTestSupport.NewStructuralNode(nodeId, typeof(AncestorNotifierActivity));

    public sealed class AncestorNotificationLog
    {
        private readonly List<string> _entries = [];
        public IReadOnlyList<string> Entries { get { lock (_entries) return _entries.ToArray(); } }
        public void Add(string entry) { lock (_entries) _entries.Add(entry); }
    }

    public sealed record AncestorDirective
    {
        /// <summary>Cancel every live direct child when notified, as a graph boundary does on exit.</summary>
        public bool CancelLiveChildrenOnNotify { get; init; }
    }

    /// <summary>The addressed ancestor: schedules its child, completes on the first notification.</summary>
    public sealed class AncestorTargetActivity(AncestorNotificationLog log, AncestorDirective directive) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityChildNotificationHandler,
        IRuntimeLiveChildActivityConsumer
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            log.Add($"ancestor:{((IRuntimeActivityExecutionContext)context.ParentContext).ExecutableNode.ExecutableNodeId}:child-completed:{context.CompletedChildExecutableNodeId}");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification)
        {
            log.Add($"ancestor:{context.ExecutableNode.ExecutableNodeId}:notified:{notification.Code}:{notification.NotifyingChildExecutableNodeId}");
            if (directive.CancelLiveChildrenOnNotify)
            {
                foreach (var child in context.GetLiveChildActivities())
                    context.RequestChildSubtreeCancellation(child.ActivityExecutionId, "exit");
            }

            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    /// <summary>An intermediate composite that would record any notification or completion it receives.</summary>
    public sealed class RelayActivity(AncestorNotificationLog log) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityChildNotificationHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            log.Add($"relay:{((IRuntimeActivityExecutionContext)context.ParentContext).ExecutableNode.ExecutableNodeId}:child-completed:{context.CompletedChildExecutableNodeId}");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification)
        {
            log.Add($"relay:{context.ExecutableNode.ExecutableNodeId}:notified:{notification.Code}");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }
    }

    /// <summary>Schedules every slot child at once and completes when all have completed.</summary>
    public sealed class RelayForkActivity : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeLiveChildActivityConsumer
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            foreach (var child in Assert.Single(context.ExecutableNode.ChildSlots).Activities)
                context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(((IRuntimeActivityExecutionContext)context.ParentContext).GetLiveChildActivities().Count == 0
                ? RuntimeStructuralContinuation.Complete()
                : RuntimeStructuralContinuation.Defer);
    }

    /// <summary>Stages one ancestor notification and completes itself, like the graph <c>Complete</c> activity.</summary>
    public sealed class AncestorNotifierActivity : StructuralActivity, IRuntimeStructuralActivity
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            context.RequestAncestorNotification(AncestorConsumerKey, "exit");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    /// <summary>Schedules its child; when the child completes, stages an ancestor notification and completes (R-2).</summary>
    public sealed class CompletingAncestorNotifierActivity : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            ((IRuntimeActivityExecutionContext)context.ParentContext).RequestAncestorNotification(AncestorConsumerKey, "exit");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    /// <summary>Stages an ancestor notification from a child-completion evaluation that defers: not allowed.</summary>
    public sealed class DeferringCompletionNotifierActivity : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            ((IRuntimeActivityExecutionContext)context.ParentContext).RequestAncestorNotification(AncestorConsumerKey, "exit");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }
    }

    /// <summary>Stages an ancestor notification from a child-fault evaluation that completes: not allowed.</summary>
    public sealed class FaultEvaluationNotifierActivity : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildFaultHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            ((IRuntimeActivityExecutionContext)context.ParentContext).RequestAncestorNotification(AncestorConsumerKey, "exit");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    /// <summary>Stages an ancestor notification but defers on a child: not allowed.</summary>
    public sealed class DeferringAncestorNotifierActivity : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            context.RequestAncestorNotification(AncestorConsumerKey, "exit");
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }
}
