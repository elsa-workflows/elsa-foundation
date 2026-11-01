using System.Text.Json;
using Elsa.Agent.Specialists.Editorial.Contracts;
using Elsa.Agent.Specialists.Editorial.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Composition;

/// <summary>
/// The finite editorial example through real specialist publication/placement, native Sequence → Flowchart
/// control flow, an exact native review bookmark and the permission-gated decision service.
/// </summary>
public sealed class EditorialCompositionTests
{
    private const string Reviewer = "reviewer-ada";
    private static readonly string FirstArticle = Article("Moss gardens grow slowly.");
    private static readonly string SecondArticle = Article("Moss gardens reward patience.");
    private static readonly string ChangesRequested = Review("changes_requested", "Lead with the reward.");
    private static readonly string Approved = Review("approved", "Ready.");

    [Fact]
    public async Task Rejected_review_feeds_the_exact_article_and_comments_into_one_rewrite_and_a_restarted_host_admits_one_approval()
    {
        await using var host = await EditorialHost.StartAsync("editorial-approve", new([FirstArticle, SecondArticle], [ChangesRequested, Approved]));
        host.Subjects.Grant(Reviewer);
        var (executable, _, _) = await EditorialExecutable.BuildAsync(DateTimeOffset.UtcNow.AddHours(1));
        await host.StartRunAsync(executable);
        await host.PumpAsync();

        var calls = host.Model.Calls.ToArray();
        Assert.True(calls.Select(x => x.Role).SequenceEqual(["copywriter", "proofreader", "copywriter", "proofreader"]),
            string.Join(",", calls.Select(x => x.Role)) + await host.DiagnoseAsync());
        Assert.Contains("Moss gardens grow slowly.", calls[2].User, StringComparison.Ordinal);
        Assert.Contains("Lead with the reward.", calls[2].User, StringComparison.Ordinal);
        Assert.Contains("Moss gardens reward patience.", calls[3].User, StringComparison.Ordinal);
        Assert.DoesNotContain("Moss gardens grow slowly.", calls[3].User, StringComparison.Ordinal);

        var pending = await host.PendingReviewAsync();
        var view = Assert.IsType<EditorialReviewView>(await host.ReadReviewAsync(pending.InvocationId));
        Assert.Equal(EditorialReviewStatus.Pending, view.Status);
        Assert.Equal(2, view.Round);
        Assert.Equal("Moss gardens reward patience.", view.Article);
        Assert.Equal("Ready.", view.ReviewComments);

        await host.RestartAsync();
        var command = new EditorialDecisionCommand(host.WorkflowExecutionId, pending.InvocationId, view.Round, view.ArticleHash,
            EditorialDecision.Approve, "Ship it.", "request-approve-1");
        var receipt = await host.DecideAsync(command, Reviewer);
        await host.PumpAsync();
        Assert.Contains(receipt.Status, new[] { EditorialDecisionReceiptStatus.Accepted, EditorialDecisionReceiptStatus.Pending });

        var replay = await host.DecideAsync(command, Reviewer);
        var reviewed = (await host.ActivitiesAsync()).Single(x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.True(replay.Status == EditorialDecisionReceiptStatus.Accepted, replay.Status + ": " + replay.Reason + " first=" + receipt.Status + " " + receipt.Reason +
            JsonSerializer.Serialize(new { reviewed.Status, Private = reviewed.PrivateState is not null, Result = reviewed.Completion?.Result.InlineValue?.GetRawText(),
                Deliveries = reviewed.TriggerDeliveries?.Select(x => new { x.Status, x.ProviderId, Payload = x.Payload.InlineValue is not null, Kind = x.Payload.GetType().Name }) }));
        Assert.Equal(EditorialDecisionReceiptStatus.Conflict, (await host.DecideAsync(command with { Decision = EditorialDecision.Deny }, Reviewer)).Status);
        Assert.Equal(EditorialDecisionReceiptStatus.AlreadyResolved, (await host.DecideAsync(command with { RequestId = "request-late-deny" }, Reviewer)).Status);

        Assert.Equal(WorkflowExecutionStatus.Completed, (await host.WorkflowAsync()).Status);
        var completed = Assert.Single(await host.ActivitiesAsync(), x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.Equal("Approved", completed.Completion!.OutcomeKey);
        Assert.Single(completed.TriggerDeliveries!, x => x.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Equal(4, host.Model.Calls.Count);
    }

    internal static string Article(string text) => JsonSerializer.Serialize(new { Article = text });

    internal static string Review(string verdict, string comments) => JsonSerializer.Serialize(new { Verdict = verdict, Comments = comments });
}
