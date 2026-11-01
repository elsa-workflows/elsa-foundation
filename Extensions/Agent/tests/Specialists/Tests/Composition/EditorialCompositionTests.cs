using System.Text.Json;
using Elsa.Agent.Specialists.Editorial.Contracts;
using Elsa.Agent.Specialists.Editorial.Models;
using Elsa.Agent.Specialists.Editorial.Services;
using Elsa.Secrets.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
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
        var (executable, copywriterVersion, proofreaderVersion) = await EditorialExecutable.BuildAsync(DateTimeOffset.UtcNow.AddHours(1));
        await host.StartRunAsync(executable);
        await host.PumpAsync();

        // The native graph pins the exact published specialist versions that ran.
        var pinned = Pinned(executable.RootActivity).Select(x => x.DescriptorPayload).Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("definitionVersionId", out _))
            .Select(x => x.GetProperty("definitionVersionId").GetString()).ToArray();
        Assert.Contains(copywriterVersion, pinned);
        Assert.Contains(proofreaderVersion, pinned);
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
        var finished = await host.ActivitiesAsync();
        // One logical continuation: each container completes exactly once.
        Assert.Single(finished, x => x.Execution.ExecutableNodeId == "editorial-flowchart" && x.Status == ActivityExecutionStatus.Completed);
        Assert.Single(finished, x => x.Execution.ExecutableNodeId == "editorial-sequence" && x.Status == ActivityExecutionStatus.Completed);
        var completed = Assert.Single(finished, x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.Equal("Approved", completed.Completion!.OutcomeKey);
        Assert.Single(completed.TriggerDeliveries!, x => x.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Equal(4, host.Model.Calls.Count);
    }

    [Fact]
    public async Task Denial_after_restart_takes_the_rejected_branch_without_publishing_or_another_model_turn()
    {
        await using var host = await EditorialHost.StartAsync("editorial-deny", new([FirstArticle], [Approved]));
        host.Subjects.Grant(Reviewer);
        var (pending, view) = await RunToReviewAsync(host);
        Assert.Equal(1, view.Round);
        await host.RestartAsync();

        var receipt = await host.DecideAsync(Command(host, pending, view, EditorialDecision.Deny, "request-deny-1"), Reviewer);
        await host.PumpAsync();

        Assert.Contains(receipt.Status, new[] { EditorialDecisionReceiptStatus.Accepted, EditorialDecisionReceiptStatus.Pending });
        Assert.Equal(WorkflowExecutionStatus.Completed, (await host.WorkflowAsync()).Status);
        var review = Assert.Single(await host.ActivitiesAsync(), x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.Equal("Rejected", review.Completion!.OutcomeKey);
        Assert.Equal("Deny", review.Completion.Result.InlineValue!.Value.GetProperty("decision").GetString());
        Assert.Equal(EditorialReviewStatus.Rejected, (await host.ReadReviewAsync(pending.InvocationId))!.Status);
        Assert.Equal(EditorialDecisionReceiptStatus.AlreadyResolved,
            (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-late-approve"), Reviewer)).Status);
        Assert.Equal(2, host.Model.Calls.Count);
    }

    [Fact]
    public async Task Third_rejection_takes_the_explicit_exhausted_branch_after_six_specialist_calls()
    {
        await using var host = await EditorialHost.StartAsync("editorial-exhausted",
            new([FirstArticle, SecondArticle, Article("Moss gardens, third draft.")], [ChangesRequested, ChangesRequested, ChangesRequested]));
        var (executable, _, _) = await EditorialExecutable.BuildAsync(DateTimeOffset.UtcNow.AddHours(1));
        await host.StartRunAsync(executable);
        await host.PumpAsync();

        Assert.Equal(WorkflowExecutionStatus.Completed, (await host.WorkflowAsync()).Status);
        Assert.Equal(["copywriter", "proofreader", "copywriter", "proofreader", "copywriter", "proofreader"], host.Model.Calls.Select(x => x.Role));
        var activities = await host.ActivitiesAsync();
        Assert.DoesNotContain(activities, x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        var last = activities.Where(x => x.Execution.ExecutableNodeId == EditorialHost.GateNodeId).OrderBy(x => x.ExecutionSequence).Last();
        Assert.Equal("Exhausted", last.Completion!.OutcomeKey);
        var result = last.Completion.Result.InlineValue!.Value;
        Assert.Equal(3, result.GetProperty("round").GetInt32());
        Assert.Equal("Moss gardens, third draft.", result.GetProperty("article").GetString());
        Assert.Equal("Lead with the reward.", result.GetProperty("feedback").GetString());
    }

    [Fact]
    public async Task Changed_foreign_unpermitted_malformed_and_unavailable_decisions_leave_the_valid_wait_pending()
    {
        await using var host = await EditorialHost.StartAsync("editorial-refusals", new([FirstArticle], [Approved]));
        var (pending, view) = await RunToReviewAsync(host);

        Assert.Equal(EditorialDecisionReceiptStatus.Forbidden, (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-forbidden"), Reviewer)).Status);
        host.Subjects.Grant(Reviewer);
        Assert.Equal(EditorialDecisionReceiptStatus.Conflict,
            (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-changed") with { ArticleHash = new string('0', 64) }, Reviewer)).Status);
        Assert.Equal(EditorialDecisionReceiptStatus.Conflict,
            (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-round") with { Round = 2 }, Reviewer)).Status);
        Assert.Equal(EditorialDecisionReceiptStatus.Conflict, (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "bad id"), Reviewer)).Status);
        host.Subjects.Grant(Reviewer, "tenant-foreign");
        Assert.Equal(EditorialDecisionReceiptStatus.NotFound,
            (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-foreign"), Reviewer, "tenant-foreign")).Status);
        Assert.Null(await host.ReadReviewAsync(pending.InvocationId, "tenant-foreign"));
        host.Subjects.Unavailable = true;
        Assert.Equal(EditorialDecisionReceiptStatus.Unavailable, (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-unavailable"), Reviewer)).Status);
        host.Subjects.Unavailable = false;
        await host.PumpAsync();

        Assert.Equal(EditorialReviewStatus.Pending, (await host.ReadReviewAsync(pending.InvocationId))!.Status);
        Assert.DoesNotContain((await host.PendingReviewAsync()).TriggerDeliveries ?? [], x => x.Status == ActivityTriggerDeliveryStatus.Consumed);

        await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-valid"), Reviewer);
        await host.PumpAsync();
        Assert.Equal(EditorialReviewStatus.Approved, (await host.ReadReviewAsync(pending.InvocationId))!.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await host.WorkflowAsync()).Status);
    }

    [Fact]
    public async Task Revocation_before_admission_refuses_the_issued_decision_and_keeps_the_wait_for_a_current_reviewer()
    {
        await using var host = await EditorialHost.StartAsync("editorial-revoked", new([FirstArticle], [Approved]));
        host.Subjects.Grant(Reviewer);
        host.Subjects.Grant("reviewer-grace");
        var (pending, view) = await RunToReviewAsync(host);

        // The service's check passes; the subject is revoked before the pre-claim guard re-checks it.
        host.Subjects.RevokeAfterNextCheck(Reviewer);
        var issued = await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-revoked"), Reviewer);
        await host.PumpAsync();
        Assert.Equal(EditorialDecisionReceiptStatus.Refused, issued.Status);
        Assert.True(host.Subjects.Checks >= 2);

        Assert.Equal(EditorialReviewStatus.Pending, (await host.ReadReviewAsync(pending.InvocationId))!.Status);
        Assert.Contains(await host.IncidentsAsync(), x => x.Message.Contains("no longer holds the review permission", StringComparison.Ordinal));
        // While the runtime still remembers it, the refused request ID is refused again rather than reported Pending.
        host.Subjects.Grant(Reviewer);
        Assert.Equal(EditorialDecisionReceiptStatus.Refused, (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-revoked"), Reviewer)).Status);
        host.Subjects.Revoke(Reviewer);
        Assert.Equal(EditorialDecisionReceiptStatus.Forbidden, (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-revoked-again"), Reviewer)).Status);

        await host.DecideAsync(Command(host, pending, view, EditorialDecision.Deny, "request-grace"), "reviewer-grace");
        await host.PumpAsync();
        var review = Assert.IsType<EditorialReviewView>(await host.ReadReviewAsync(pending.InvocationId));
        Assert.Equal(EditorialReviewStatus.Rejected, review.Status);
        Assert.Equal("reviewer-grace", review.DecidedBy);
    }

    [Fact]
    public async Task Expired_and_foreign_purpose_envelopes_cannot_consume_the_wait()
    {
        await using var host = await EditorialHost.StartAsync("editorial-envelopes", new([FirstArticle], [Approved]), TimeSpan.FromMilliseconds(1));
        host.Subjects.Grant(Reviewer);
        var (pending, view) = await RunToReviewAsync(host);

        Assert.Equal(EditorialDecisionReceiptStatus.Refused, (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-expired"), Reviewer)).Status);
        await host.PumpAsync();
        Assert.Equal(EditorialReviewStatus.Pending, (await host.ReadReviewAsync(pending.InvocationId))!.Status);
        Assert.Contains(await host.IncidentsAsync(), x => x.Message.Contains("expired before it was admitted", StringComparison.Ordinal));

        var bookmarkId = Assert.Single(pending.BookmarkIds);
        var state = EditorialReviewRecords.ReadState(pending);
        await using (var scope = await host.ScopeAsync())
        {
            var protector = scope.ServiceProvider.GetRequiredService<ISecretValueProtector>();
            var codec = new EditorialDecisionCodec(protector);
            var genuine = new EditorialDecisionClaims(EditorialDecisionCodec.Purpose, EditorialDecisionCodec.Codec, state.TenantId, state.Partition,
                state.WorkflowExecutionId, state.ActivityExecutionId, state.ExecutableNodeId, state.WaitId, bookmarkId, state.Round, state.ArticleHash,
                Reviewer, EditorialDecision.Approve, null, "request-genuine", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
            var authentic = codec.Protect(genuine);
            var segments = authentic.Split(':');
            var bytes = Convert.FromBase64String(segments[^1]);
            bytes[0] ^= 0x01;
            segments[^1] = Convert.ToBase64String(bytes);
            var flipped = string.Join(':', segments);
            var foreignWait = codec.Protect(genuine with { WaitId = "editorial-review:" + new string('f', 40), RequestId = "request-foreign-wait" });
            var foreignRun = codec.Protect(genuine with { WorkflowExecutionId = "another-run", RequestId = "request-foreign-run" });
            foreach (var envelope in new[] { protector.Protect("{\"purpose\":\"elsa.connections.secret/1\"}"), "v2:forged:envelope", flipped, foreignWait, foreignRun })
            {
                var input = JsonSerializer.SerializeToElement(new EditorialDecisionDelivery(EditorialReviewIds.FormatVersion, envelope));
                await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new BookmarkResumeDispatchRequest(host.WorkflowExecutionId,
                    EditorialReviewIds.StimulusType, StimulusHash(pending),
                    input, "forged-" + envelope.Length, "test", null, EditorialReviewRecords.PayloadType, EditorialReviewIds.ProviderId, bookmarkId, null));
            }
        }
        await host.PumpAsync();

        Assert.Equal(EditorialReviewStatus.Pending, (await host.ReadReviewAsync(pending.InvocationId))!.Status);
        Assert.DoesNotContain((await host.PendingReviewAsync()).TriggerDeliveries ?? [], x => x.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Equal(WorkflowExecutionStatus.Running, (await host.WorkflowAsync()).Status);
    }

    [Fact]
    public async Task Cancellation_before_admission_is_never_resurrected_into_an_approval()
    {
        await using var host = await EditorialHost.StartAsync("editorial-cancel", new([FirstArticle], [Approved]));
        host.Subjects.Grant(Reviewer);
        var (pending, view) = await RunToReviewAsync(host);

        await host.CancelAsync();
        await host.PumpAsync();
        Assert.Contains((await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-cancel"), Reviewer)).Status,
            new[] { EditorialDecisionReceiptStatus.Closed, EditorialDecisionReceiptStatus.NotFound });
        await host.PumpAsync();

        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await host.WorkflowAsync()).Status);
        var review = Assert.Single(await host.ActivitiesAsync(), x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.NotEqual("Approved", review.Completion?.OutcomeKey);
        Assert.Contains((await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-after-cancel"), Reviewer)).Status,
            new[] { EditorialDecisionReceiptStatus.Closed, EditorialDecisionReceiptStatus.NotFound });
    }

    [Fact]
    public async Task An_admitted_approval_stays_historical_when_the_workflow_is_cancelled_or_the_grant_revoked_afterwards()
    {
        await using var host = await EditorialHost.StartAsync("editorial-historical", new([FirstArticle], [Approved]));
        host.Subjects.Grant(Reviewer);
        var (pending, view) = await RunToReviewAsync(host);
        var command = Command(host, pending, view, EditorialDecision.Approve, "request-historical");

        await host.DecideAsync(command, Reviewer);
        await host.PumpAsync();
        host.Subjects.Revoke(Reviewer);
        await host.CancelAsync();
        await host.PumpAsync();

        var review = Assert.Single(await host.ActivitiesAsync(), x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.Equal("Approved", review.Completion!.OutcomeKey);
        Assert.Equal(EditorialReviewStatus.Approved, (await host.ReadReviewAsync(pending.InvocationId))!.Status);
        Assert.Equal(EditorialDecisionReceiptStatus.Accepted, (await host.DecideAsync(command, Reviewer)).Status);
    }

    [Fact]
    public async Task A_crash_after_admission_claims_the_wait_finishes_the_historical_decision_once_despite_later_revocation()
    {
        await using var host = await EditorialHost.StartAsync("editorial-claimed", new([FirstArticle], [Approved]));
        host.Subjects.Grant(Reviewer);
        var (pending, view) = await RunToReviewAsync(host);
        var command = Command(host, pending, view, EditorialDecision.Approve, "request-claimed");

        host.Crash.Arm();
        var lost = await host.DecideAsync(command, Reviewer);
        Assert.True(host.Crash.Fired, lost.Status + " " + lost.Reason);
        var claimed = Assert.Single(await host.ActivitiesAsync(), x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.Null(claimed.Completion);
        Assert.Single(claimed.TriggerDeliveries!, x => x.Status == ActivityTriggerDeliveryStatus.Consumed);

        // Revocation after admission cannot reinterpret the claimed decision; recovery needs no new authorization.
        host.Subjects.Revoke(Reviewer);
        await host.RestartAsync();
        for (var attempt = 0; attempt < 40 && (await host.WorkflowAsync()).Status == WorkflowExecutionStatus.Running; attempt++)
        {
            await host.PumpAsync();
            await Task.Delay(250);
        }

        Assert.Equal(WorkflowExecutionStatus.Completed, (await host.WorkflowAsync()).Status);
        var review = Assert.Single(await host.ActivitiesAsync(), x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.Equal("Approved", review.Completion!.OutcomeKey);
        Assert.Single(review.TriggerDeliveries!, x => x.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Equal(EditorialDecisionReceiptStatus.Accepted, (await host.DecideAsync(command, Reviewer)).Status);
        Assert.Equal(2, host.Model.Calls.Count);
    }

    [Fact]
    public async Task A_guard_time_identity_outage_keeps_the_decision_owed_until_it_recovers()
    {
        await using var host = await EditorialHost.StartAsync("editorial-outage", new([FirstArticle], [Approved]));
        host.Subjects.Grant(Reviewer);
        var (pending, view) = await RunToReviewAsync(host);

        host.Subjects.UnavailableAfterNextCheck();
        var issued = await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-outage"), Reviewer);
        Assert.Equal(EditorialDecisionReceiptStatus.Pending, issued.Status);
        Assert.Empty(await host.IncidentsAsync());
        Assert.Equal(EditorialReviewStatus.Pending, (await host.ReadReviewAsync(pending.InvocationId))!.Status);

        host.Subjects.Unavailable = false;
        for (var attempt = 0; attempt < 20 && (await host.WorkflowAsync()).Status == WorkflowExecutionStatus.Running; attempt++)
        {
            await Task.Delay(250);
            await host.PumpAsync();
        }

        Assert.Equal(EditorialReviewStatus.Approved, (await host.ReadReviewAsync(pending.InvocationId))!.Status);
        Assert.Equal(EditorialDecisionReceiptStatus.Accepted, (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-outage"), Reviewer)).Status);
    }

    [Fact]
    public async Task A_cancellation_requested_behind_an_owed_decision_settles_in_native_order_exactly_once()
    {
        await using var host = await EditorialHost.StartAsync("editorial-cancel-race", new([FirstArticle], [Approved]));
        host.Subjects.Grant(Reviewer);
        var (pending, view) = await RunToReviewAsync(host);

        host.Subjects.UnavailableAfterNextCheck();
        Assert.Equal(EditorialDecisionReceiptStatus.Pending, (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-race"), Reviewer)).Status);
        // The cancel is queued behind the earlier, still-owed decision. Native order admits the decision first, then the
        // cancel lands before the parent's completion, so the review is historical and the workflow never completes.
        await host.CancelAsync();
        Assert.Equal(WorkflowExecutionStatus.Running, (await host.WorkflowAsync()).Status);
        host.Subjects.Unavailable = false;
        for (var attempt = 0; attempt < 20 && (await host.WorkflowAsync()).Status == WorkflowExecutionStatus.Running; attempt++)
        {
            await Task.Delay(250);
            await host.PumpAsync();
        }

        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await host.WorkflowAsync()).Status);
        var activities = await host.ActivitiesAsync();
        var review = Assert.Single(activities, x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.Single(review.TriggerDeliveries ?? [], x => x.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Equal("Approved", review.Completion?.OutcomeKey);
        Assert.DoesNotContain(activities, x => x.Execution.ExecutableNodeId == "editorial-sequence" && x.Status == ActivityExecutionStatus.Completed);
        Assert.Contains((await host.DecideAsync(Command(host, pending, view, EditorialDecision.Deny, "request-after-race"), Reviewer)).Status,
            new[] { EditorialDecisionReceiptStatus.AlreadyResolved, EditorialDecisionReceiptStatus.Closed });
    }

    [Fact]
    public async Task A_deterministic_guard_failure_poisons_the_work_and_leaves_the_wait_intact()
    {
        await using var host = await EditorialHost.StartAsync("editorial-poison", new([FirstArticle], [Approved]));
        host.Subjects.Grant(Reviewer);
        var (pending, view) = await RunToReviewAsync(host);

        host.Subjects.Broken = true;
        Assert.Equal(EditorialDecisionReceiptStatus.Refused, (await host.DecideAsync(Command(host, pending, view, EditorialDecision.Approve, "request-poison"), Reviewer)).Status);
        await host.PumpAsync();

        Assert.Contains(await host.IncidentsAsync(), x => x.Message.Contains("identity configuration is invalid", StringComparison.Ordinal));
        Assert.Equal(EditorialReviewStatus.Pending, (await host.ReadReviewAsync(pending.InvocationId))!.Status);
        Assert.DoesNotContain((await host.PendingReviewAsync()).TriggerDeliveries ?? [], x => x.Status == ActivityTriggerDeliveryStatus.Consumed);
        await using var scope = await host.ScopeAsync();
        Assert.Empty((await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new RuntimeSchedulerWorkQuery(host.WorkflowExecutionId))).Items);
    }

    private static IEnumerable<ExecutableNode> Pinned(ExecutableNode node) =>
        new[] { node }.Concat(node.ChildSlots.SelectMany(slot => slot.Activities).SelectMany(Pinned));

    private static async Task<(ActivityExecutionState Pending, EditorialReviewView View)> RunToReviewAsync(EditorialHost host)
    {
        var (executable, _, _) = await EditorialExecutable.BuildAsync(DateTimeOffset.UtcNow.AddHours(1));
        await host.StartRunAsync(executable);
        await host.PumpAsync();
        var pending = await host.PendingReviewAsync();
        return (pending, Assert.IsType<EditorialReviewView>(await host.ReadReviewAsync(pending.InvocationId)));
    }

    private static EditorialDecisionCommand Command(EditorialHost host, ActivityExecutionState pending, EditorialReviewView view, EditorialDecision decision, string requestId) =>
        new(host.WorkflowExecutionId, pending.InvocationId, view.Round, view.ArticleHash, decision, null, requestId);

    private static string StimulusHash(ActivityExecutionState pending) => EditorialReviewRecords.ReadState(pending).WaitId;

    internal static string Article(string text) => JsonSerializer.Serialize(new { Article = text });

    internal static string Review(string verdict, string comments) => JsonSerializer.Serialize(new { Verdict = verdict, Comments = comments });
}
