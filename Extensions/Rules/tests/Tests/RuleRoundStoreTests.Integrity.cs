using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Tests;

public sealed partial class RuleRoundStoreTests
{
    [Theory]
    [InlineData("omitted-firing")]
    [InlineData("duplicate-firing")]
    [InlineData("additional-fact")]
    [InlineData("foreign-firing")]
    [InlineData("noncanonical-order")]
    public async Task Rehashed_but_incomplete_or_noncanonical_receipts_are_rejected_without_mutation(string corruption)
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var before = await SnapshotAsync(invocation.Identity);
        var valid = evaluator.Evaluate(input);
        var candidate = corruption switch
        {
            "omitted-firing" => RehashReceipt(valid,
                firings: valid.FiringEvidence.Where(firing => firing.RuleId != "right").ToArray()),
            "duplicate-firing" => RehashReceipt(valid,
                firings: valid.FiringEvidence.Append(valid.FiringEvidence[0]).ToArray()),
            "additional-fact" => RehashReceipt(valid,
                additions: valid.AddedFacts.Append(new RuleFact("outside-vocabulary")).ToArray()),
            "foreign-firing" => RehashReceipt(valid,
                firings: valid.FiringEvidence.Append(new RuleFiringEvidence("foreign", new("outside-vocabulary"))).ToArray()),
            "noncanonical-order" => RehashReceipt(valid, valid.AddedFacts.Reverse().ToArray(),
                valid.FiringEvidence.Reverse().ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };

        var result = await store.AcceptRoundAsync(token, Submission(candidate, token, "invalid-attempt"));

        Assert.Equal(RuleReceiptDisposition.InvalidReceipt, result.Disposition);
        AssertNoAcceptancePayload(result);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Altered_receipt_digest_is_rejected_before_any_store_mutation()
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var before = await SnapshotAsync(invocation.Identity);
        var valid = evaluator.Evaluate(input);
        var alteredDigest = (valid.SemanticReceiptSha256[0] == '0' ? '1' : '0') + valid.SemanticReceiptSha256[1..];
        var altered = new RuleRoundReceipt(valid.Identity, valid.ImplementationPinHash, valid.AddedFacts,
            valid.FiringEvidence, alteredDigest);

        var result = await store.AcceptRoundAsync(token, Submission(altered, token, "altered-digest"));

        Assert.Equal(RuleReceiptDisposition.InvalidReceipt, result.Disposition);
        AssertNoAcceptancePayload(result);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Different_semantic_receipt_for_an_accepted_round_conflicts_without_replacing_the_winner()
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var acceptedReceipt = evaluator.Evaluate(input);
        var accepted = await store.AcceptRoundAsync(token, Submission(acceptedReceipt, token, "winning-attempt"));
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        var beforeConflict = await SnapshotAsync(invocation.Identity);
        var differentReceipt = RehashReceipt(acceptedReceipt,
            firings: acceptedReceipt.FiringEvidence.Where(firing => firing.RuleId != "right").ToArray());

        var conflict = await store.AcceptRoundAsync(token, Submission(differentReceipt, token, "losing-attempt"));

        Assert.Equal(RuleReceiptDisposition.Conflict, conflict.Disposition);
        AssertNoAcceptancePayload(conflict);
        Assert.Equal(beforeConflict, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Corrupt_running_state_at_the_round_limit_cannot_commit_another_receipt()
    {
        var invocation = NewRun(1);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        Assert.Equal(RuleReceiptDisposition.Accepted,
            (await store.AcceptRoundAsync(token, Submission(receipt, token, "accepted"))).Disposition);
        token = (await store.ReadWorkAsync(token)).CurrentToken!;
        var changed = RehashReceipt(new(new(receipt.Identity.Run, receipt.Identity.Fingerprint, 1, receipt.Identity.InputStateSha256),
            receipt.ImplementationPinHash, receipt.AddedFacts, receipt.FiringEvidence, receipt.SemanticReceiptSha256));
        var receiptId = RuleRoundRecords.ReceiptId(changed);
        await using (var db = fixture.Contexts.CreateDbContext())
        {
            var run = await db.Runs.SingleAsync();
            var old = await db.Receipts.SingleAsync();
            db.Receipts.Remove(old);
            db.Receipts.Add(new RuleRoundReceiptRecord
            {
                RunKey = run.RunKey, Ordinal = 1, ReceiptId64 = receiptId, InputFactsSha25664 = run.FactsSha25664,
                ReceiptJson = Encoding.UTF8.GetString(RuleRecordCodec.ReceiptRecordBytes(changed)),
                AcceptedAttemptId = old.AcceptedAttemptId, AcceptedWorkerId = old.AcceptedWorkerId,
                AcceptedFence = old.AcceptedFence, AcceptedModelRevision = old.AcceptedModelRevision,
                SchemaVersion = old.SchemaVersion
            });
            run.PendingReceiptId64 = receiptId;
            run.CommittedRounds = 1;
            await db.SaveChangesAsync();
        }
        var before = await SnapshotAsync(invocation.Identity);
        var result = await store.ConsumeRoundAsync(token, new(receiptId, changed.SemanticReceiptSha256));
        Assert.Equal(RuleTransitionDisposition.Unavailable, result.Disposition);
        AssertNoTransitionPayload(result);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Accepted_receipt_can_be_consumed_without_the_old_interpreter()
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        var accepted = await store.AcceptRoundAsync(token, Submission(receipt, token, "accepted"));
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        var unavailable = new FixedRoundValidator(RuleValidationDisposition.UnsupportedFormat);
        var recovery = CreateStore(unavailable);
        var pending = await recovery.ReadWorkAsync(token);
        Assert.Equal(RuleWorkReadDisposition.ConsumeReceipt, pending.Disposition);
        var consumed = await recovery.ConsumeRoundAsync(pending.CurrentToken!, new(accepted.ReceiptId!, receipt.SemanticReceiptSha256));
        Assert.Equal(RuleTransitionDisposition.Consumed, consumed.Disposition);
        Assert.Equal(0, unavailable.Calls);
        Assert.Equal(new[] { "left", "right", "seed" }, (await ReadCurrentFactsAsync(invocation.Identity)).Select(fact => fact.Atom));
    }

    [Fact]
    public async Task Read_work_with_released_owner_returns_lease_lost_without_work_payload()
    {
        var invocation = NewRun(4);
        var (store, token, _) = await StartAsync(invocation);
        Assert.Equal(RuleLeaseDisposition.Released, (await store.ReleaseAsync(token.Lease)).Disposition);
        var afterRelease = await SnapshotAsync(invocation.Identity);

        var read = await store.ReadWorkAsync(token);

        Assert.Equal(RuleWorkReadDisposition.LeaseLost, read.Disposition);
        Assert.Null(read.CurrentToken);
        Assert.Null(read.RoundInput);
        Assert.Null(read.PendingReceipt);
        Assert.Null(read.TerminalId);
        Assert.Equal(afterRelease, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Consume_after_cancellation_of_pending_receipt_is_terminal_unchanged_and_keeps_committed_facts_only()
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        var accepted = await store.AcceptRoundAsync(token, Submission(receipt, token, "pending-attempt"));
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        var receiptIdentity = new RuleReceiptIdentity(accepted.ReceiptId!, receipt.SemanticReceiptSha256);
        var cancelled = await store.CancelAsync(new(invocation));
        Assert.Equal(RuleCancellationDisposition.Cancelled, cancelled.Disposition);
        var terminal = await store.ReadTerminalAsync(invocation.Identity);
        Assert.Equal(RuleTerminalKind.Cancelled, terminal.Result?.Kind);
        Assert.Equal(new[] { "seed" }, terminal.Result?.ResultFacts.Select(fact => fact.Atom));
        var beforeConsume = await SnapshotAsync(invocation.Identity);

        var result = await store.ConsumeRoundAsync(token, receiptIdentity);

        Assert.Equal(RuleTransitionDisposition.TerminalUnchanged, result.Disposition);
        Assert.Null(result.TransitionId);
        Assert.Null(result.ReceiptId);
        Assert.Equal(cancelled.TerminalId, result.TerminalId);
        Assert.Null(result.NextRoundId);
        Assert.Equal(cancelled.ModelRevision, result.ModelRevision);
        Assert.Equal(beforeConsume, await SnapshotAsync(invocation.Identity));
        Assert.Equal(new[] { "seed" }, (await store.ReadTerminalAsync(invocation.Identity)).Result?.ResultFacts.Select(fact => fact.Atom));
    }

    [Theory]
    [InlineData("advanced")]
    [InlineData("cancelled")]
    public async Task Consume_replay_after_later_round_progress_returns_the_original_historical_confirmation(string outcome)
    {
        var invocation = NewRun(4);
        var (store, firstToken, firstInput) = await StartAsync(invocation);
        var firstReceipt = evaluator.Evaluate(firstInput);
        var firstAccepted = await store.AcceptRoundAsync(firstToken, Submission(firstReceipt, firstToken, "first-attempt"));
        var firstPending = await store.ReadWorkAsync(firstToken);
        var firstReceiptIdentity = new RuleReceiptIdentity(firstPending.PendingReceipt!.ReceiptId,
            firstPending.PendingReceipt.Receipt.SemanticReceiptSha256);
        var firstConsumed = await store.ConsumeRoundAsync(firstPending.CurrentToken!, firstReceiptIdentity);
        Assert.Equal(RuleTransitionDisposition.Consumed, firstConsumed.Disposition);

        var next = await store.ReadWorkAsync(firstPending.CurrentToken!);
        var secondToken = next.CurrentToken!;
        var secondReceipt = evaluator.Evaluate(next.RoundInput!);
        Assert.Equal(RuleReceiptDisposition.Accepted,
            (await store.AcceptRoundAsync(secondToken, Submission(secondReceipt, secondToken, "second-attempt"))).Disposition);
        var secondPending = await store.ReadWorkAsync(secondToken);
        Assert.Equal(RuleTransitionDisposition.Consumed,
            (await store.ConsumeRoundAsync(secondPending.CurrentToken!, new(secondPending.PendingReceipt!.ReceiptId,
                secondPending.PendingReceipt.Receipt.SemanticReceiptSha256))).Disposition);

        if (outcome == "cancelled")
            Assert.Equal(RuleCancellationDisposition.Cancelled, (await store.CancelAsync(new(invocation))).Disposition);
        else if (outcome != "advanced")
            throw new ArgumentOutOfRangeException(nameof(outcome));

        var beforeReplay = await SnapshotAsync(invocation.Identity);
        var replay = await CreateStore().ConsumeRoundAsync(firstToken, firstReceiptIdentity);

        Assert.Equal(RuleTransitionDisposition.Replay, replay.Disposition);
        Assert.Equal(firstConsumed.TransitionId, replay.TransitionId);
        Assert.Equal(firstAccepted.ReceiptId, replay.ReceiptId);
        Assert.Equal(firstConsumed.TerminalId, replay.TerminalId);
        Assert.Equal(firstConsumed.NextRoundId, replay.NextRoundId);
        Assert.Equal(firstConsumed.ModelRevision, replay.ModelRevision);
        Assert.Equal(beforeReplay, await SnapshotAsync(invocation.Identity));
    }
}
