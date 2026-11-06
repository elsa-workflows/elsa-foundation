using System.Text.Json;
using Elsa.Workflows.Runtime.Services.InvocationInputs;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class InvocationInputInboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_proposal_is_immutable_and_owns_its_content_after_the_callers_document_is_disposed()
    {
        var original = new InvocationInputInbox(Policy());
        InvocationAdmissionProposal proposal;
        using (var document = JsonDocument.Parse("{\"kind\":\"Event\"}"))
            proposal = original.ProposeAdmission("a", document.RootElement, Now);
        Assert.Equal(InvocationAdmissionKind.Proposed, proposal.Kind);
        Assert.Equal(0, original.PendingCount);
        Assert.Equal(0, original.LastOrdinal);
        Assert.Equal(1, proposal.Inbox.PendingCount);
        Assert.Equal("Event", proposal.Receipt!.Content!.Value.GetProperty("kind").GetString());
    }

    [Fact]
    public void A_selected_input_still_reserves_pending_capacity_and_deduplication_precedes_full_rejection()
    {
        var inbox = Admit(new(Policy(1, 2)), "a").BeginEvaluation("a", "evaluate").Select("a", "selection");
        var revision = inbox.Revision;
        Assert.Equal(InvocationAdmissionKind.PendingCapacity, inbox.ProposeAdmission("b", Content(), Now).Kind);
        Assert.Equal(InvocationAdmissionKind.Existing, inbox.ProposeAdmission("a", Content(), Now).Kind);
        Assert.Equal(InvocationAdmissionKind.Conflict, inbox.ProposeAdmission("a", Content(2), Now).Kind);
        Assert.Equal(revision, inbox.Revision);
        Assert.Equal(1, inbox.LastOrdinal);
        inbox = inbox.Finish("a", InvocationInputStatus.Handled, "done", Now.AddMinutes(1));
        var second = inbox.ProposeAdmission("b", Content(), Now.AddMinutes(2));
        Assert.Equal(InvocationAdmissionKind.Proposed, second.Kind);
        Assert.Equal(2, second.Receipt!.Ordinal);
    }

    [Fact]
    public void Settled_receipts_reserve_their_own_budget_until_both_time_and_runtime_obligations_allow_release()
    {
        var inbox = Admit(new(Policy(1, 1)), "a").BeginEvaluation("a", "evaluate")
            .Finish("a", InvocationInputStatus.Unhandled, "no-rule", Now);
        Assert.Equal(0, inbox.PendingCount);
        Assert.Null(inbox.Find("a")!.Content);
        Assert.Equal(InvocationAdmissionKind.ReceiptCapacity, inbox.ProposeAdmission("b", Content(), Now).Kind);
        Assert.Throws<InvalidOperationException>(() => inbox.ReleaseReceipts(["a"], Now.AddMinutes(59)));
        Assert.Same(inbox, inbox.ReleaseReceipts([], Now.AddHours(2)));
        Assert.Equal(InvocationAdmissionKind.ReceiptCapacity, inbox.ProposeAdmission("b", Content(), Now.AddHours(2)).Kind);
        inbox = inbox.ReleaseReceipts(["a"], Now.AddHours(2));
        Assert.Equal(InvocationAdmissionKind.Proposed, inbox.ProposeAdmission("b", Content(), Now.AddHours(2)).Kind);
    }

    [Fact]
    public void An_expired_released_identity_can_be_admitted_again_with_a_later_ordinal()
    {
        var inbox = Admit(new(Policy()), "a").BeginEvaluation("a", "evaluate")
            .Finish("a", InvocationInputStatus.Unhandled, "no-rule", Now);
        inbox = inbox.ReleaseReceipts(["a"], Now.AddHours(1));
        var next = inbox.ProposeAdmission("a", Content(2), Now.AddHours(1));
        Assert.Equal(InvocationAdmissionKind.Proposed, next.Kind);
        Assert.Equal(2, next.Receipt!.Ordinal);
    }

    [Fact]
    public void Evaluation_is_fifo_and_an_unhandled_input_never_reenters_the_queue()
    {
        var inbox = Admit(Admit(new(Policy()), "a"), "b");
        Assert.Throws<InvalidOperationException>(() => inbox.BeginEvaluation("b", "evaluate-b"));
        inbox = inbox.BeginEvaluation("a", "evaluate-a");
        Assert.Same(inbox, inbox.BeginEvaluation("a", "evaluate-a"));
        var busy = inbox;
        Assert.Throws<InvalidOperationException>(() => busy.BeginEvaluation("b", "evaluate-b"));
        inbox = inbox.Finish("a", InvocationInputStatus.Unhandled, "no-rule", Now);
        var finished = inbox;
        Assert.Throws<InvalidOperationException>(() => finished.BeginEvaluation("a", "later-values"));
        inbox = inbox.BeginEvaluation("b", "evaluate-b");
        Assert.Equal(InvocationInputStatus.Evaluating, inbox.Find("b")!.Status);
        Assert.Equal(InvocationInputStatus.Unhandled, inbox.Find("a")!.Status);
    }

    [Fact]
    public void A_guard_failure_keeps_evaluation_and_its_reservation_while_other_inputs_can_be_proposed()
    {
        var inbox = Admit(new(Policy()), "a").BeginEvaluation("a", "guard-attempt");
        // No successful evaluation disposition is proposed: recovery retains this exact boundary.
        inbox = Admit(inbox, "b");
        Assert.Equal(2, inbox.PendingCount);
        Assert.Equal("guard-attempt", inbox.Find("a")!.EvaluationId);
        Assert.Throws<InvalidOperationException>(() => inbox.BeginEvaluation("b", "next"));
        Assert.Throws<InvalidOperationException>(() => inbox.Finish("a", InvocationInputStatus.Handled, "false-success", Now));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("cancelled")]
    [InlineData("superseded")]
    public void Ending_distinguishes_selected_interruption_and_unselected_not_processed(string reason)
    {
        var inbox = Admit(Admit(new(Policy()), "a"), "b").BeginEvaluation("a", "evaluate")
            .Select("a", "selection").RecordProgress("a", "exit-done/action-pending");
        var ended = inbox.End(reason, Now.AddDays(10));
        var selected = ended.Find("a")!; var queued = ended.Find("b")!;
        Assert.Equal(InvocationInputStatus.Interrupted, selected.Status);
        Assert.Equal("selection", selected.SelectionId);
        Assert.Equal("exit-done/action-pending", selected.ProgressEvidence);
        Assert.Equal(InvocationInputStatus.NotProcessed, queued.Status);
        Assert.Equal(reason, selected.FinalEvidence);
        Assert.Equal(reason, queued.FinalEvidence);
        Assert.Equal(Now.AddDays(10).AddHours(1), selected.RetainUntil);
        Assert.Equal(0, ended.PendingCount);
        Assert.Equal(2, ended.ReceiptCount);
        Assert.Equal(2, inbox.PendingCount);
    }

    [Fact]
    public void Ending_an_evaluation_preserves_its_evidence_without_claiming_it_was_unhandled()
    {
        var ended = Admit(new(Policy()), "a").BeginEvaluation("a", "guard-interrupted").End("cancelled", Now);
        Assert.Equal(InvocationInputStatus.NotProcessed, ended.Find("a")!.Status);
        Assert.Equal("guard-interrupted", ended.Find("a")!.EvaluationId);
    }

    [Fact]
    public void Terminal_duplicates_and_conflicts_resolve_before_new_input_rejection()
    {
        var ended = Admit(new(Policy()), "a").End("completed", Now);
        Assert.Equal(InvocationAdmissionKind.Existing, ended.ProposeAdmission("a", Content(), Now).Kind);
        Assert.Equal(InvocationAdmissionKind.Conflict, ended.ProposeAdmission("a", Content(2), Now).Kind);
        Assert.Equal(InvocationAdmissionKind.Ended, ended.ProposeAdmission("b", Content(), Now).Kind);
        Assert.Equal(1, ended.LastOrdinal);
        Assert.Same(ended, ended.End("completed", Now));
        Assert.Throws<InvalidOperationException>(() => ended.End("cancelled", Now));
    }

    [Fact]
    public void A_later_policy_preserves_original_validation_bounds_and_the_retention_promise()
    {
        var content = JsonSerializer.SerializeToElement(new { text = new string('x', 50) });
        var old = new InvocationInputInbox(Policy());
        var first = old.ProposeAdmission("long-original-id", content, Now);
        var changed = first.Inbox.WithPolicy(new(2, 4, TimeSpan.FromMinutes(1), 4, 32, 1));
        Assert.Equal(InvocationAdmissionKind.Existing, changed.ProposeAdmission("long-original-id", content, Now).Kind);
        changed = Admit(changed, "b");
        var ended = changed.End("completed", Now.AddDays(10));
        Assert.Equal(Now.AddDays(10).AddHours(1), ended.Find("long-original-id")!.RetainUntil);
        Assert.Equal(Now.AddDays(10).AddMinutes(1), ended.Find("b")!.RetainUntil);
    }

    [Fact]
    public void Ordinal_exhaustion_never_wraps_or_consumes_a_rejected_identity()
    {
        var inbox = new InvocationInputInbox(Policy(), long.MaxValue - 1);
        var last = inbox.ProposeAdmission("a", Content(), Now);
        Assert.Equal(long.MaxValue, last.Receipt!.Ordinal);
        Assert.Equal(InvocationAdmissionKind.OrdinalExhausted, last.Inbox.ProposeAdmission("b", Content(), Now).Kind);
        Assert.Null(last.Inbox.Find("b"));
        Assert.Equal(long.MaxValue, last.Inbox.LastOrdinal);
    }

    [Fact]
    public void Sender_ids_are_ordinal_and_belong_only_to_this_inbox()
    {
        var inbox = Admit(Admit(Admit(new(Policy(4, 4)), "ID"), "id"), " ID ");
        Assert.Equal(3, inbox.PendingCount);
        Assert.Equal(InvocationAdmissionKind.Proposed, new InvocationInputInbox(Policy()).ProposeAdmission("ID", Content(), Now).Kind);
        Assert.Equal(" ID ", inbox.Find(" ID ")!.InputId);
    }

    [Fact]
    public void Receipt_release_is_atomic_and_cannot_remove_pending_or_promised_evidence()
    {
        var inbox = Admit(Admit(new(Policy()), "a"), "b").BeginEvaluation("a", "evaluate")
            .Finish("a", InvocationInputStatus.Unhandled, "no-rule", Now);
        Assert.Throws<InvalidOperationException>(() => inbox.ReleaseReceipts(["a", "b"], Now.AddHours(2)));
        Assert.Equal(2, inbox.ReceiptCount);
        Assert.NotNull(inbox.Find("a"));
    }

    [Fact]
    public void Completion_requires_selection_and_final_evidence_cannot_be_rewritten()
    {
        var queued = Admit(new(Policy()), "a");
        Assert.Throws<InvalidOperationException>(() => queued.Select("a", "selection"));
        var selected = queued.BeginEvaluation("a", "evaluate").Select("a", "selection");
        Assert.Same(selected, selected.Select("a", "selection"));
        Assert.Throws<InvalidOperationException>(() => selected.Select("a", "other-selection"));
        var finished = selected.Finish("a", InvocationInputStatus.Handled, "done", Now);
        Assert.Same(finished, finished.Finish("a", InvocationInputStatus.Handled, "done", Now.AddHours(2)));
        Assert.Throws<InvalidOperationException>(() => finished.Finish("a", InvocationInputStatus.Unhandled, "different", Now));
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xD801)]
    [InlineData(0xDC00)]
    public void Invalid_utf16_ids_do_not_collapse_into_another_identity(int codeUnit)
    {
        var inbox = new InvocationInputInbox(Policy());
        Assert.Throws<ArgumentException>(() => inbox.ProposeAdmission("id-" + (char)codeUnit, Content(), Now));
        Assert.Equal(0, inbox.ReceiptCount);
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"a\":{\"b\":1,\"b\":2}}")]
    public void Duplicate_json_properties_never_propose_a_reservation(string content)
    {
        var inbox = new InvocationInputInbox(Policy()); using var document = JsonDocument.Parse(content);
        Assert.Throws<InvalidDataException>(() => inbox.ProposeAdmission("a", document.RootElement, Now));
        Assert.Equal(0, inbox.PendingCount);
    }

    [Fact]
    public void Identity_size_content_size_and_depth_limits_refuse_before_reservation()
    {
        var inbox = new InvocationInputInbox(new(1, 1, TimeSpan.FromHours(1), 1, 32, 1));
        Assert.Throws<ArgumentException>(() => inbox.ProposeAdmission("too-long", Content(), Now));
        Assert.Throws<ArgumentException>(() => inbox.ProposeAdmission("a", JsonSerializer.SerializeToElement(new { text = new string('x', 50) }), Now));
        Assert.Throws<ArgumentException>(() => inbox.ProposeAdmission("a", JsonSerializer.SerializeToElement(new { nested = new { } }), Now));
        Assert.Equal(0, inbox.PendingCount);
    }

    [Fact]
    public void Invalid_or_reversed_time_cannot_settle_or_release_a_promise()
    {
        var inbox = Admit(new(Policy()), "a").BeginEvaluation("a", "evaluate");
        Assert.Throws<ArgumentOutOfRangeException>(() => inbox.ProposeAdmission("b", Content(), default));
        Assert.Throws<InvalidOperationException>(() => inbox.Finish("a", InvocationInputStatus.Unhandled, "no-rule", Now.AddSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => inbox.ReleaseReceipts(["a"], default));
        Assert.Equal(InvocationInputStatus.Evaluating, inbox.Find("a")!.Status);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void Depth_boundary_counts_semantic_containers_without_losing_a_level_to_the_fingerprint_envelope(int depth, bool allowed)
    {
        var json = string.Concat(Enumerable.Repeat("{\"child\":", depth - 1)) + "{}" + new string('}', depth - 1);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 65 });
        var inbox = new InvocationInputInbox(new(1, 1, TimeSpan.FromHours(1), 64, 1024, 64));
        if (allowed) Assert.Equal(InvocationAdmissionKind.Proposed, inbox.ProposeAdmission("a", document.RootElement, Now).Kind);
        else Assert.Throws<ArgumentException>(() => inbox.ProposeAdmission("a", document.RootElement, Now));
        Assert.Equal(0, inbox.PendingCount);
    }

    private static InvocationInputPolicy Policy(int pending = 2, int receipts = 4) => new(pending, receipts,
        TimeSpan.FromHours(1), 64, 1024, 8);
    private static JsonElement Content(int number = 1) => JsonSerializer.SerializeToElement(new { number });
    private static InvocationInputInbox Admit(InvocationInputInbox inbox, string id) =>
        inbox.ProposeAdmission(id, Content(), Now).Inbox;
}
