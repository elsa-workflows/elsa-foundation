using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Editorial.Activities;
using Elsa.Agent.Specialists.Editorial.Models;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Composition;

/// <summary>The gate's closed routing table: ordinary control flow, never model prose, selects the branch.</summary>
public sealed class EditorialRevisionGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("", 0, null, null, "Write", 1, "article")]
    [InlineData("article", 1, "draft", null, "Review", 1, "review")]
    [InlineData("review", 1, "draft", "changes_requested", "Write", 2, "article")]
    [InlineData("review", 2, "draft", "changes_requested", "Write", 3, "article")]
    [InlineData("review", 3, "draft", "changes_requested", "Exhausted", 3, "none")]
    [InlineData("review", 2, "draft", "approved", "Approved", 2, "none")]
    public async Task Routes_each_stage_to_exactly_one_outcome(string awaiting, int round, string? article, string? verdict, string outcome, int nextRound, string next)
    {
        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<EditorialRevisionResult>>(
            await RunAsync(new(new FakeTimeProvider(Now)) { Awaiting = awaiting, Round = round, Article = article, Verdict = verdict, ReviewComments = "comments", Deadline = Now.AddMinutes(5) }));

        Assert.Equal(outcome, completion.Outcome);
        Assert.Equal(nextRound, completion.Result.Round);
        Assert.Equal(next, completion.Result.Awaiting);
        if (outcome == "Write" && round > 0)
        {
            Assert.Equal(article, completion.Result.PreviousArticle);
            Assert.Equal("comments", completion.Result.Feedback);
        }
    }

    [Theory]
    [InlineData("review", 1, "draft", "APPROVED", "editorial.review.verdict-invalid")]
    [InlineData("review", 1, "draft", "approve it", "editorial.review.verdict-invalid")]
    [InlineData("review", 1, "draft", null, "editorial.review.verdict-invalid")]
    [InlineData("review", 0, "draft", "approved", "editorial.revision.state-invalid")]
    [InlineData("article", 1, null, null, "editorial.revision.state-invalid")]
    [InlineData("", 1, null, null, "editorial.revision.state-invalid")]
    [InlineData("review", 4, "draft", "changes_requested", "editorial.revision.state-invalid")]
    public async Task Invalid_reviewer_output_and_stage_state_fault_before_routing(string awaiting, int round, string? article, string? verdict, string code)
    {
        var fault = Assert.IsAssignableFrom<IActivityFaultTransition>(
            await RunAsync(new(new FakeTimeProvider(Now)) { Awaiting = awaiting, Round = round, Article = article, Verdict = verdict }));

        Assert.Equal(code, fault.Fault.Code);
    }

    [Theory]
    [InlineData("", 0, null)]
    [InlineData("article", 1, null)]
    [InlineData("review", 1, "changes_requested")]
    public async Task The_original_shared_deadline_is_checked_before_every_specialist_call(string awaiting, int round, string? verdict)
    {
        var fault = Assert.IsAssignableFrom<IActivityFaultTransition>(
            await RunAsync(new(new FakeTimeProvider(Now)) { Awaiting = awaiting, Round = round, Article = "draft", Verdict = verdict, Deadline = Now }));

        Assert.Equal("editorial.revision.deadline-exceeded", fault.Fault.Code);
    }

    [Fact]
    public async Task An_approval_or_exhaustion_needs_no_further_call_and_ignores_the_deadline()
    {
        var gate = new EditorialRevisionGate(new FakeTimeProvider(Now)) { Awaiting = "review", Round = 3, Article = "draft", Verdict = "changes_requested", Deadline = Now.AddDays(-1) };

        Assert.Equal("Exhausted", Assert.IsAssignableFrom<IActivityCompletionTransition>(await RunAsync(gate)).Outcome);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task More_than_three_rounds_cannot_be_configured(int maxRounds)
    {
        var fault = Assert.IsAssignableFrom<IActivityFaultTransition>(await RunAsync(new(new FakeTimeProvider(Now)) { MaxRounds = maxRounds }));

        Assert.Equal("editorial.revision.bounds-invalid", fault.Fault.Code);
        Assert.Equal(EditorialReviewIds.MaximumRounds, new EditorialRevisionGate(TimeProvider.System).MaxRounds);
    }

    private static ValueTask<ActivityTransition> RunAsync(EditorialRevisionGate gate) =>
        ((IActivity)gate).ExecuteAsync(new ActivityExecutionContext("run", "gate-1", "attempt-1", "gate", CancellationToken.None));
}
