using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 258 R-1: an ancestor notification addressed by activity type (optionally one authored node of it) reaches the
/// nearest matching committed ancestor, can stop at a reusable-activity boundary, and can be optional. The receiver
/// learns which of its direct children the notification came through.
/// </summary>
public sealed class AncestorSelectorExecutionTests
{
    private static readonly string TargetType = typeof(TypedAncestorActivity).FullName!;

    [Fact]
    public async Task ATypeSelectorReachesTheNearestAncestorOfThatType_ThroughAnIntermediateComposite()
    {
        var log = new SelectorLog();
        await using var harness = NewHarness(log, new RuntimeAncestorSelector(ActivityTypeKey: TargetType));

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-target", typeof(TypedAncestorActivity),
                    StructuralExecutionTestSupport.NewStructuralNode("node-relay", typeof(QuietRelayActivity),
                        Notifier("node-notifier")))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        var notification = Assert.Single(log.Notifications);
        Assert.Equal("node-target", notification.Receiver);
        Assert.Equal("node-notifier", notification.NotifyingNodeId);
        Assert.Equal("node-relay", notification.ViaNodeId);
        Assert.Equal(run.State("node-relay").Execution.ActivityExecutionId, notification.ViaActivityExecutionId);
    }

    [Fact]
    public async Task ADirectChildIsItsOwnViaChild()
    {
        var log = new SelectorLog();
        await using var harness = NewHarness(log, new RuntimeAncestorSelector(ActivityTypeKey: TargetType));

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-target", typeof(TypedAncestorActivity), Notifier("node-notifier"))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        var notification = Assert.Single(log.Notifications);
        Assert.Equal("node-notifier", notification.ViaNodeId);
        Assert.Equal(run.State("node-notifier").Execution.ActivityExecutionId, notification.ViaActivityExecutionId);
    }

    [Fact]
    public async Task AnAuthoredIdSelectsOneAncestorOfTheType_SkippingANearerOne()
    {
        var log = new SelectorLog();
        await using var harness = NewHarness(log, new RuntimeAncestorSelector(ActivityTypeKey: TargetType, AuthoredActivityId: "authored-node-outer"));

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-outer", typeof(TypedAncestorActivity),
                    StructuralExecutionTestSupport.NewStructuralNode("node-inner", typeof(TypedAncestorActivity),
                        Notifier("node-notifier")))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        var notification = Assert.Single(log.Notifications);
        Assert.Equal("node-outer", notification.Receiver);
        Assert.Equal("node-inner", notification.ViaNodeId);
    }

    [Fact]
    public async Task AGraphBoundaryEndsTheWalkWhenAsked_AndAnOptionalRequestThenSendsNothing()
    {
        var log = new SelectorLog();
        await using var harness = NewHarness(log, new RuntimeAncestorSelector(ActivityTypeKey: TargetType, StopAtGraphBoundary: true, Optional: true));

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-target", typeof(TypedAncestorActivity),
                    GraphBoundaryNode("node-boundary", Notifier("node-notifier")))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-notifier");
        Assert.Empty(log.Notifications);
    }

    [Fact]
    public async Task WithoutTheBoundaryStopTheWalkCrossesAGraphBoundary()
    {
        var log = new SelectorLog();
        await using var harness = NewHarness(log, new RuntimeAncestorSelector(ActivityTypeKey: TargetType));

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-target", typeof(TypedAncestorActivity),
                    GraphBoundaryNode("node-boundary", Notifier("node-notifier")))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        Assert.Equal("node-boundary", Assert.Single(log.Notifications).ViaNodeId);
    }

    [Fact]
    public async Task AnOptionalRequestWithNoMatchCompletesTheRequesterAndSendsNothing()
    {
        var log = new SelectorLog();
        await using var harness = NewHarness(log, new RuntimeAncestorSelector(ActivityTypeKey: "no.such.Type", Optional: true));

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-target", typeof(TypedAncestorActivity), Notifier("node-notifier"))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-notifier");
        Assert.Empty(log.Notifications);
    }

    [Fact]
    public async Task ARequiredRequestWithNoMatchFaultsTheRequesterWithTheStableCode()
    {
        var log = new SelectorLog();
        await using var harness = NewHarness(log, new RuntimeAncestorSelector(ActivityTypeKey: "no.such.Type"));

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-target", typeof(TypedAncestorActivity), Notifier("node-notifier"))),
            allowPendingWorkOnTerminalCompletion: true);

        var notifier = run.State("node-notifier");
        Assert.Equal(ActivityExecutionStatus.Faulted, notifier.Status);
        Assert.Equal("runtime.ancestor-notification.target-not-found", notifier.Fault!.Code);
        Assert.Contains("no.such.Type", notifier.Fault.Message, StringComparison.Ordinal);
        Assert.Empty(log.Notifications);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("elsa.clr-activity", "Some.Type")]
    public void ASelectorNamesExactlyOneOfConsumerKeyOrActivityType(string? consumerKey, string? activityTypeKey)
    {
        Assert.Throws<ArgumentException>(() => new RuntimeAncestorNotificationRequest(
            new RuntimeAncestorSelector(consumerKey, activityTypeKey), "code"));
    }

    [Fact]
    public void AnAuthoredIdNeedsAnActivityType()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeAncestorNotificationRequest(
            new RuntimeAncestorSelector(ConsumerKey: "elsa.clr-activity", AuthoredActivityId: "authored-x"), "code"));
    }

    [Fact]
    public void TheConsumerKeyOverloadMapsToAConsumerKeySelector()
    {
        var request = new RuntimeAncestorNotificationRequest("elsa.graph-activity", "exit");

        Assert.Equal(new RuntimeAncestorSelector(ConsumerKey: "elsa.graph-activity"), request.Selector);
        Assert.Equal("elsa.graph-activity", request.AncestorConsumerKey);
    }

    private static WorkflowExecutionHarness NewHarness(SelectorLog log, RuntimeAncestorSelector selector) =>
        WorkflowExecutionHarness.Create()
            .ConfigureServices(services =>
            {
                services.AddSingleton(log);
                services.AddSingleton(new SelectorDirective(selector));
                services.AddScoped<IActivityActivationStrategy, GraphBoundaryActivationStrategy>();
            })
            .Build("actexec-1", "actexec-2", "actexec-3", "actexec-4", "actexec-5", "actexec-6");

    private static ExecutableNode Notifier(string nodeId) =>
        StructuralExecutionTestSupport.NewStructuralNode(nodeId, typeof(SelectorNotifierActivity));

    // A reusable-activity boundary is any node activated from the graph consumer key.
    private static ExecutableNode GraphBoundaryNode(string nodeId, params ExecutableNode[] children)
    {
        var consumerKey = WellKnownRuntimeActivityConsumers.GraphActivity;
        var payload = JsonSerializer.SerializeToElement(new { type = "boundary" });
        var contract = new ActivityContract(
            "test-boundary",
            "1.0.0",
            consumerKey,
            payload,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Object"),
                true,
                ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result },
                []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(consumerKey, consumerKey));

        return new ExecutableNode(
            nodeId,
            $"authored-{nodeId}",
            "test-boundary",
            "1.0.0",
            new RuntimeActivityDescriptor(consumerKey, RuntimeActivityDescriptor.InitialSchemaVersion, payload),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            [new ExecutableChildSlot(StructuralExecutionTestSupport.ChildSlotName, children)],
            structure: null,
            activityContract: contract);
    }

    private sealed class GraphBoundaryActivationStrategy(IServiceScopeFactory scopeFactory) : IActivityActivationStrategy
    {
        public string ConsumerKey => WellKnownRuntimeActivityConsumers.GraphActivity;
        public IReadOnlyCollection<string> SupportedSchemaVersions => [RuntimeActivityDescriptor.InitialSchemaVersion];
        public bool RequiresInputHydration => false;

        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationStrategyRequest request, CancellationToken cancellationToken = default)
        {
            var scope = scopeFactory.CreateAsyncScope();
            var activity = ActivatorUtilities.CreateInstance<QuietRelayActivity>(scope.ServiceProvider);
            return ValueTask.FromResult(new ActivityActivationLease(activity, scope));
        }
    }

    public sealed record SelectorDirective(RuntimeAncestorSelector Selector);

    public sealed record SelectorNotification(
        string Receiver,
        string NotifyingNodeId,
        string ViaActivityExecutionId,
        string ViaNodeId);

    public sealed class SelectorLog
    {
        private readonly List<SelectorNotification> _notifications = [];
        public IReadOnlyList<SelectorNotification> Notifications { get { lock (_notifications) return _notifications.ToArray(); } }
        public void Add(SelectorNotification notification) { lock (_notifications) _notifications.Add(notification); }
    }

    /// <summary>The addressed ancestor type: records each notification with its via child and keeps running.</summary>
    public sealed class TypedAncestorActivity(SelectorLog log) : StructuralActivity,
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

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

        public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification)
        {
            log.Add(new SelectorNotification(
                context.ExecutableNode.ExecutableNodeId,
                notification.NotifyingChildExecutableNodeId,
                notification.ViaChildActivityExecutionId,
                notification.ViaChildExecutableNodeId));
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }
    }

    /// <summary>A composite between the notifier and its target that completes with its child and hears nothing.</summary>
    public sealed class QuietRelayActivity : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }

    /// <summary>Stages one ancestor notification with the configured selector and completes itself.</summary>
    public sealed class SelectorNotifierActivity(SelectorDirective directive) : StructuralActivity, IRuntimeStructuralActivity
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            context.RequestAncestorNotification(directive.Selector, "signal");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }
}
