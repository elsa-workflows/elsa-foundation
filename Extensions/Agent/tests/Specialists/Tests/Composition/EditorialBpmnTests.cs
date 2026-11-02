using System.Text.Json;
using Elsa.Agent.Specialists.Review.Contracts;
using Elsa.Agent.Specialists.Review.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Composition;

/// <summary>The editorial sample as a native BPMN process: the same published specialists, gate and human review.</summary>
public sealed class EditorialBpmnTests
{
    private const string Reviewer = "reviewer-ada";
    private static readonly string First = EditorialCompositionTests.Article("Moss gardens grow slowly.");
    private static readonly string Second = EditorialCompositionTests.Article("Moss gardens reward patience.");
    private static readonly string Rejected = EditorialCompositionTests.Review("changes_requested", "Lead with the reward.");
    private static readonly string Approved = EditorialCompositionTests.Review("approved", "Ready.");

    [Theory]
    [InlineData(ReviewDecision.Approve, "Approved")]
    [InlineData(ReviewDecision.Deny, "Rejected")]
    public async Task Bound_children_loop_once_then_a_restarted_host_admits_the_human_decision_exactly_once(ReviewDecision decision, string outcome)
    {
        await using var host = await EditorialHost.StartAsync($"bpmn-{outcome.ToLowerInvariant()}", new([First, Second], [Rejected, Approved]));
        host.Subjects.Grant(Reviewer);
        var (executable, _, _) = await EditorialExecutable.BuildAsync(DateTimeOffset.UtcNow.AddHours(1), bpmn: true);
        await host.StartRunAsync(executable);
        await host.PumpAsync();

        var calls = host.Model.Calls.ToArray();
        Assert.True(calls.Select(x => x.Role).SequenceEqual(["copywriter", "proofreader", "copywriter", "proofreader"]),
            string.Join(",", calls.Select(x => x.Role)) + await host.DiagnoseAsync());
        Assert.Contains("Lead with the reward.", calls[2].User, StringComparison.Ordinal);
        var pending = await host.PendingReviewAsync();
        var view = Assert.IsType<HumanReviewView>(await host.ReadReviewAsync(pending.InvocationId));
        Assert.Equal("Moss gardens reward patience.", view.Content.GetString());

        await host.RestartAsync();
        var command = new ReviewDecisionCommand(host.WorkflowExecutionId, pending.InvocationId, view.ContentHash, decision, null, $"request-bpmn-{outcome}");
        await host.DecideAsync(command, Reviewer);
        await host.PumpAsync();

        Assert.Equal(ReviewDecisionReceiptStatus.Accepted, (await host.DecideAsync(command, Reviewer)).Status);
        Assert.Equal(ReviewDecisionReceiptStatus.AlreadyResolved, (await host.DecideAsync(command with { RequestId = "request-bpmn-late" }, Reviewer)).Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await host.WorkflowAsync()).Status);
        var review = Assert.Single(await host.ActivitiesAsync(), x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.Equal(outcome, review.Completion!.OutcomeKey);
        Assert.Single(review.TriggerDeliveries!, x => x.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Single(await host.ActivitiesAsync(), x => x.Execution.ExecutableNodeId == "editorial-flowchart" && x.Status == ActivityExecutionStatus.Completed);
        Assert.Equal(4, host.Model.Calls.Count);
    }

    [Fact]
    public async Task Third_rejection_routes_to_the_exhausted_end_without_a_review()
    {
        await using var host = await EditorialHost.StartAsync("bpmn-exhausted",
            new([First, Second, EditorialCompositionTests.Article("Third draft.")], [Rejected, Rejected, Rejected]));
        var (executable, _, _) = await EditorialExecutable.BuildAsync(DateTimeOffset.UtcNow.AddHours(1), bpmn: true);
        await host.StartRunAsync(executable);
        await host.PumpAsync();

        Assert.Equal(WorkflowExecutionStatus.Completed, (await host.WorkflowAsync()).Status);
        Assert.Equal(6, host.Model.Calls.Count);
        Assert.DoesNotContain(await host.ActivitiesAsync(), x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
    }
}
