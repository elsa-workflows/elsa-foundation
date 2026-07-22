using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 126 (seam C): a still-running structural child raises a coded, durable notification to its own
/// committed parent; the parent — iff it implements <see cref="IRuntimeActivityChildNotificationHandler"/> —
/// receives it and may respond with any continuation (including a seam-A subtree cancellation), while the child
/// keeps running.
/// </summary>
public sealed class ChildParentNotificationExecutionTests
{
    [Fact]
    public async Task InvokeStagedNotification_DeliveredWithCodePayloadAndChildIdentity()
    {
        var recorder = new ParentNotificationRecorder();
        await using var harness = NewHarness(
            new ParentNotificationDirective { InvokeCodes = ["escalate-1"], Payload = JsonSerializer.SerializeToElement(new { level = 3 }) },
            new ParentNotificationConsumerDirective(),
            recorder);

        var run = await harness.RunAsync(NewGraph());

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-consumer");
        var notification = Assert.Single(recorder.Notifications);
        Assert.Equal("escalate-1", notification.Code);
        Assert.Equal("actexec-child", notification.NotifyingChildActivityExecutionId);
        Assert.Equal("node-child", notification.NotifyingChildExecutableNodeId);
        Assert.Contains("\"level\":3", notification.Payload);
    }

    [Fact]
    public async Task MultipleNotifications_DeliveredInStagingOrderWithDistinctDerivedIds()
    {
        var recorder = new ParentNotificationRecorder();
        await using var harness = NewHarness(
            new ParentNotificationDirective { InvokeCodes = ["first", "second"] },
            new ParentNotificationConsumerDirective(),
            recorder);

        var run = await harness.RunAsync(NewGraph());

        run.AssertWorkflowCompleted();
        // Both delivered (no id collision — distinct ordinal-suffixed derived ids) and in staging order.
        Assert.Equal(["first", "second"], recorder.Notifications.Select(notification => notification.Code));
    }

    [Fact]
    public async Task CompletionStagedNotification_DeliveredFromChildCompletionEvaluation()
    {
        var recorder = new ParentNotificationRecorder();
        await using var harness = NewHarness(
            new ParentNotificationDirective { CompletionCode = "done-signal" },
            new ParentNotificationConsumerDirective(),
            recorder);

        var run = await harness.RunAsync(NewGraph());

        run.AssertWorkflowCompleted();
        var notification = Assert.Single(recorder.Notifications);
        Assert.Equal("done-signal", notification.Code);
        Assert.Equal("actexec-child", notification.NotifyingChildActivityExecutionId);
    }

    [Fact]
    public async Task NonImplementingParent_SilentlyAcksTheNotification()
    {
        var recorder = new ParentNotificationRecorder();
        await using var harness = WorkflowExecutionHarness.Create()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new ParentNotificationDirective { InvokeCodes = ["ignored"] });
                services.AddSingleton(recorder);
            })
            .Build("actexec-consumer", "actexec-child", "actexec-leaf");

        var run = await harness.RunAsync(StructuralExecutionTestSupport.NewExecutable(
            StructuralExecutionTestSupport.NewStructuralNode("node-consumer", typeof(NonNotificationConsumingParentActivity),
                StructuralExecutionTestSupport.NewStructuralNode("node-child", typeof(NotifyingStructuralChildActivity),
                    WorkflowExecutionHarness.NewProbeNode("node-leaf")))));

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-consumer");
        Assert.Empty(recorder.Notifications);
        Assert.Empty(await ListIncidentsAsync(harness));
    }

    [Fact]
    public async Task RootActivityStaging_FaultsTheEvaluation()
    {
        var recorder = new ParentNotificationRecorder();
        await using var harness = NewHarness(
            new ParentNotificationDirective { InvokeCodes = ["escalate"] },
            new ParentNotificationConsumerDirective(),
            recorder,
            "actexec-root", "actexec-child");

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-root", typeof(RootNotifyingStructuralActivity),
                    WorkflowExecutionHarness.NewProbeNode("node-child"))),
            allowPendingWorkOnTerminalCompletion: true);

        var root = run.State("node-root");
        Assert.Equal(ActivityExecutionStatus.Faulted, root.Status);
        Assert.Contains("root activity has no parent", root.Fault!.Message, StringComparison.Ordinal);
        Assert.Empty(recorder.Notifications);
    }

    [Fact]
    public async Task FaultContinuationWithStagedNotification_FaultsTheEvaluation()
    {
        var recorder = new ParentNotificationRecorder();
        await using var harness = NewHarness(
            new ParentNotificationDirective { InvokeCodes = ["escalate"], FaultInsteadOfDefer = true },
            new ParentNotificationConsumerDirective(),
            recorder);

        var run = await harness.RunAsync(NewGraph(), allowPendingWorkOnTerminalCompletion: true);

        var child = run.State("node-child");
        Assert.Equal(ActivityExecutionStatus.Faulted, child.Status);
        Assert.Contains("cannot also notify its parent", child.Fault!.Message, StringComparison.Ordinal);
        Assert.Empty(recorder.Notifications);
    }

    [Fact]
    public async Task ParentNotRunning_AcksLaterNotifications()
    {
        var recorder = new ParentNotificationRecorder();
        await using var harness = NewHarness(
            new ParentNotificationDirective { InvokeCodes = ["first", "second"] },
            new ParentNotificationConsumerDirective { CompleteOnNotify = true },
            recorder);

        var run = await harness.RunAsync(NewGraph(), allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        // The parent completes on the first notification; the second reaches a non-Running parent and is acked away.
        var notification = Assert.Single(recorder.Notifications);
        Assert.Equal("first", notification.Code);
    }

    [Fact]
    public async Task SeamACancellationFromNotificationEvaluation_TearsDownNotifyingChild()
    {
        var recorder = new ParentNotificationRecorder();
        await using var harness = NewHarness(
            new ParentNotificationDirective { InvokeCodes = ["interrupt"] },
            new ParentNotificationConsumerDirective { CancelNotifyingChildOnNotify = true, CompleteOnNotify = true },
            recorder);

        var run = await harness.RunAsync(NewGraph(), allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-consumer");
        Assert.Single(recorder.Notifications);
        var child = run.State("node-child");
        Assert.Equal(ActivityExecutionStatus.Cancelled, child.Status);
        Assert.Equal("ParentCancelled", child.SubStatus);
    }

    private static WorkflowExecutionHarness NewHarness(
        ParentNotificationDirective childDirective,
        ParentNotificationConsumerDirective consumerDirective,
        ParentNotificationRecorder recorder,
        params string[] activityExecutionIds)
    {
        var ids = activityExecutionIds.Length > 0
            ? activityExecutionIds
            : ["actexec-consumer", "actexec-child", "actexec-leaf"];
        return WorkflowExecutionHarness.Create()
            .ConfigureServices(services =>
            {
                services.AddSingleton(childDirective);
                services.AddSingleton(consumerDirective);
                services.AddSingleton(recorder);
            })
            .Build(ids);
    }

    private static WorkflowExecutable NewGraph() =>
        StructuralExecutionTestSupport.NewExecutable(
            StructuralExecutionTestSupport.NewStructuralNode("node-consumer", typeof(NotificationConsumingParentActivity),
                StructuralExecutionTestSupport.NewStructuralNode("node-child", typeof(NotifyingStructuralChildActivity),
                    WorkflowExecutionHarness.NewProbeNode("node-leaf"))));

    private static async Task<IReadOnlyCollection<IncidentState>> ListIncidentsAsync(WorkflowExecutionHarness harness) =>
        await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
}
