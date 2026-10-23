using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Elsa.Rules.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed partial class RuleRoundStoreTests : IAsyncLifetime
{
    private readonly RuleStoreTestFixture fixture = new();
    private readonly RuleRoundEvaluator evaluator = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Theory]
    [InlineData(3, RuleTerminalKind.Faulted)]
    [InlineData(4, RuleTerminalKind.Succeeded)]
    public async Task Witness_rounds_are_accepted_then_consumed_once_to_closure(int maxRounds, RuleTerminalKind expectedTerminal)
    {
        var invocation = NewRun(maxRounds);
        var (store, token, input) = await StartAsync(invocation);
        string[][] expectedAdded = [["left", "right"], ["joined"], ["final"], []];
        string[][] expectedFirings = [["left", "right"], ["join", "left", "right"],
            ["final", "join", "left", "right"], ["final", "join", "left", "right"]];
        string[][] expectedFacts = [["left", "right", "seed"], ["joined", "left", "right", "seed"],
            ["final", "joined", "left", "right", "seed"], ["final", "joined", "left", "right", "seed"]];
        RuleTransitionConfirmation? terminalTransition = null;

        for (var ordinal = 0; ordinal < maxRounds; ordinal++)
        {
            Assert.Equal(ordinal, input.Identity.Ordinal);
            Assert.Equal(token.ExpectedModelRevision, input.ModelRevision);
            var receipt = evaluator.Evaluate(input);
            Assert.Equal(expectedAdded[ordinal], receipt.AddedFacts.Select(fact => fact.Atom));
            Assert.Equal(expectedFirings[ordinal], receipt.FiringEvidence.Select(firing => firing.RuleId));

            var beforeAccept = await SnapshotAsync(invocation.Identity);
            var submission = Submission(receipt, token, $"attempt-{ordinal}");
            var accepted = await store.AcceptRoundAsync(token, submission);

            Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
            Assert.NotNull(accepted.ReceiptId);
            Assert.Equal(beforeAccept.ModelRevision + 1, accepted.ModelRevision);
            Assert.Null(accepted.TerminalId);
            Assert.Null(accepted.NextRoundId);
            var afterAccept = await SnapshotAsync(invocation.Identity);
            Assert.Equal(beforeAccept.ModelRevision + 1, afterAccept.ModelRevision);
            Assert.Equal(beforeAccept.CommittedRounds, afterAccept.CommittedRounds);
            Assert.Equal(beforeAccept.FactsJson, afterAccept.FactsJson);
            Assert.Equal(beforeAccept.FactsHash, afterAccept.FactsHash);
            Assert.Equal(beforeAccept.Status, afterAccept.Status);
            Assert.NotNull(afterAccept.PendingReceiptId);

            // The claim token carries the pre-acceptance revision. A read by the same live owner refreshes it
            // and exposes only the accepted receipt needed for consumption.
            var pending = await store.ReadWorkAsync(token);
            Assert.Equal(RuleWorkReadDisposition.ConsumeReceipt, pending.Disposition);
            Assert.NotNull(pending.CurrentToken);
            Assert.Equal(accepted.ModelRevision, pending.CurrentToken.ExpectedModelRevision);
            Assert.Equal(token.Lease, pending.CurrentToken.Lease);
            Assert.Null(pending.RoundInput);
            Assert.NotNull(pending.PendingReceipt);
            Assert.Equal(accepted.ReceiptId, pending.PendingReceipt.ReceiptId);
            Assert.Equal(receipt.SemanticReceiptSha256, pending.PendingReceipt.Receipt.SemanticReceiptSha256);
            Assert.Equal($"attempt-{ordinal}", pending.PendingReceipt.AcceptedAttempt.AttemptId);
            Assert.Equal(afterAccept, await SnapshotAsync(invocation.Identity));

            var consumed = await store.ConsumeRoundAsync(pending.CurrentToken,
                new(pending.PendingReceipt.ReceiptId, pending.PendingReceipt.Receipt.SemanticReceiptSha256));
            Assert.Equal(RuleTransitionDisposition.Consumed, consumed.Disposition);
            Assert.Equal(pending.PendingReceipt.ReceiptId, consumed.ReceiptId);
            Assert.NotNull(consumed.TransitionId);
            Assert.Equal(accepted.ModelRevision + 1, consumed.ModelRevision);
            Assert.Equal(ordinal + 1, (await store.FindAsync(invocation.Identity)).View?.CommittedRounds);
            var consumedFacts = await ReadCurrentFactsAsync(invocation.Identity);
            Assert.Equal(expectedFacts[ordinal], consumedFacts.Select(fact => fact.Atom));
            var history = await ReadFactHistoryAsync(invocation.Identity);
            Assert.Equal(expectedFacts[ordinal], history.Select(fact => fact.Atom).Order(StringComparer.Ordinal));
            Assert.Equal(1, history.Single(fact => fact.Atom == "seed").FirstVisibleModelRevision);
            foreach (var added in receipt.AddedFacts)
                Assert.Equal(consumed.ModelRevision, history.Single(fact => fact.Atom == added.Atom).FirstVisibleModelRevision);

            if (ordinal + 1 < maxRounds)
            {
                Assert.Null(consumed.TerminalId);
                Assert.NotNull(consumed.NextRoundId);
                var next = await store.ReadWorkAsync(pending.CurrentToken);
                Assert.Equal(RuleWorkReadDisposition.EvaluateRound, next.Disposition);
                Assert.NotNull(next.CurrentToken);
                Assert.Equal(consumed.ModelRevision, next.CurrentToken.ExpectedModelRevision);
                Assert.NotNull(next.RoundInput);
                Assert.Equal(ordinal + 1, next.RoundInput.Identity.Ordinal);
                Assert.Null(next.PendingReceipt);
                token = next.CurrentToken;
                input = next.RoundInput;
            }
            else
                terminalTransition = consumed;
        }

        Assert.NotNull(terminalTransition);
        Assert.Equal(expectedTerminal == RuleTerminalKind.Succeeded ? null : "rules.round-budget-exhausted",
            (await store.ReadTerminalAsync(invocation.Identity)).Result?.Diagnostic?.Code);
        var terminalRead = await store.ReadTerminalAsync(invocation.Identity);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminalRead.Disposition);
        Assert.Equal(expectedTerminal, terminalRead.Result?.Kind);
        Assert.Equal(maxRounds, terminalRead.Result?.CommittedRounds);
        Assert.Equal(expectedFacts[Math.Min(maxRounds - 1, 3)], terminalRead.Result?.ResultFacts.Select(fact => fact.Atom));
        Assert.Equal(terminalRead.Result?.TerminalId, terminalTransition.TerminalId);
        Assert.Null(terminalTransition.NextRoundId);
        var view = await store.FindAsync(invocation.Identity);
        Assert.Equal(expectedTerminal == RuleTerminalKind.Succeeded ? RuleRunStatus.Succeeded : RuleRunStatus.Faulted, view.View?.Status);
        Assert.Equal(maxRounds, view.View?.CommittedRounds);
    }

    [Fact]
    public async Task Accept_changes_only_pending_receipt_and_revision_then_read_refreshes_old_token_without_mutation()
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var before = await SnapshotAsync(invocation.Identity);
        var receipt = evaluator.Evaluate(input);

        var accepted = await store.AcceptRoundAsync(token, Submission(receipt, token, "first-attempt"));

        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        Assert.Equal(2, accepted.ModelRevision);
        Assert.Null(accepted.TerminalId);
        Assert.Null(accepted.NextRoundId);
        var after = await SnapshotAsync(invocation.Identity);
        Assert.Equal(2, after.ModelRevision);
        Assert.Equal(before.FactsJson, after.FactsJson);
        Assert.Equal(before.FactsHash, after.FactsHash);
        Assert.Equal(before.CommittedRounds, after.CommittedRounds);
        Assert.Equal(before.Status, after.Status);
        Assert.NotNull(after.PendingReceiptId);
        Assert.NotEqual(before.ConcurrencyToken, after.ConcurrencyToken);

        var read = await store.ReadWorkAsync(token);
        Assert.Equal(RuleWorkReadDisposition.ConsumeReceipt, read.Disposition);
        Assert.Equal(2, read.CurrentToken?.ExpectedModelRevision);
        Assert.Equal(token.Lease, read.CurrentToken?.Lease);
        Assert.Equal(2, read.PendingReceipt?.AcceptedModelRevision);
        Assert.Equal("first-attempt", read.PendingReceipt?.AcceptedAttempt.AttemptId);
        Assert.Equal(after, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task New_acceptance_requires_current_model_revision_and_fence()
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var before = await SnapshotAsync(invocation.Identity);
        var receipt = evaluator.Evaluate(input);
        var submission = Submission(receipt, token, "valid-attempt");

        var staleRevision = await store.AcceptRoundAsync(new(token.Lease, token.ExpectedModelRevision + 1), submission);
        Assert.Equal(RuleReceiptDisposition.RevisionConflict, staleRevision.Disposition);
        AssertNoAcceptancePayload(staleRevision);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));

        var replacedLease = new RuleWorkLease(token.Lease.Identity, token.Lease.Fingerprint, token.Lease.Owner,
            token.Lease.Fence + 1, token.Lease.ExpiresAt);
        var staleFence = await store.AcceptRoundAsync(new(replacedLease, token.ExpectedModelRevision), submission);
        Assert.Equal(RuleReceiptDisposition.LeaseLost, staleFence.Disposition);
        AssertNoAcceptancePayload(staleFence);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));

        Assert.Equal(RuleReceiptDisposition.Accepted,
            (await store.AcceptRoundAsync(token, submission)).Disposition);
    }

    [Theory]
    [InlineData("released")]
    [InlineData("cancelled")]
    [InlineData("consumed")]
    public async Task Accepted_round_replay_preserves_first_attempt_after_lease_loss_or_terminal_progress(string outcome)
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        var firstSubmission = Submission(receipt, token, "first-attempt");
        var accepted = await store.AcceptRoundAsync(token, firstSubmission);
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);

        switch (outcome)
        {
            case "released":
                Assert.Equal(RuleLeaseDisposition.Released, (await store.ReleaseAsync(token.Lease)).Disposition);
                break;
            case "cancelled":
                Assert.Equal(RuleCancellationDisposition.Cancelled,
                    (await store.CancelAsync(new(invocation))).Disposition);
                var cancelled = await store.ReadTerminalAsync(invocation.Identity);
                Assert.Equal(new[] { "seed" }, cancelled.Result?.ResultFacts.Select(fact => fact.Atom));
                break;
            case "consumed":
                var pending = await store.ReadWorkAsync(token);
                var consume = await store.ConsumeRoundAsync(pending.CurrentToken!,
                    new(pending.PendingReceipt!.ReceiptId, pending.PendingReceipt.Receipt.SemanticReceiptSha256));
                Assert.Equal(RuleTransitionDisposition.Consumed, consume.Disposition);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        var beforeReplay = await SnapshotAsync(invocation.Identity);
        var unavailableValidator = new FixedRoundValidator(RuleValidationDisposition.UnsupportedFormat);
        var replayStore = CreateStore(unavailableValidator);
        var replacementAttempt = new RuleRoundSubmission(receipt,
            new("replacement-attempt", "replacement-worker", token.Lease.Fence + 1));
        var replay = await replayStore.AcceptRoundAsync(token, replacementAttempt);

        Assert.Equal(RuleReceiptDisposition.Replay, replay.Disposition);
        Assert.Equal(accepted.ReceiptId, replay.ReceiptId);
        Assert.Equal(accepted.ModelRevision, replay.ModelRevision);
        Assert.Null(replay.TerminalId);
        Assert.Null(replay.NextRoundId);
        Assert.Equal(0, unavailableValidator.Calls);
        Assert.Equal(beforeReplay, await SnapshotAsync(invocation.Identity));

        await using var db = fixture.Contexts.CreateDbContext();
        var runKey = RunKey(invocation.Identity);
        var stored = await db.Receipts.SingleAsync(row => row.RunKey == runKey && row.Ordinal == 0);
        Assert.Equal("first-attempt", stored.AcceptedAttemptId);
        Assert.Equal(token.Lease.Owner, stored.AcceptedWorkerId);
        Assert.Equal(token.Lease.Fence, stored.AcceptedFence);
        Assert.Equal(outcome == "consumed" ? 3L : null, stored.ConsumedModelRevision);
    }

    [Fact]
    public async Task Consume_requires_current_revision_then_exact_retry_returns_only_the_original_confirmation()
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        var accepted = await store.AcceptRoundAsync(token, Submission(receipt, token, "consume-attempt"));
        var pending = await store.ReadWorkAsync(token);
        var receiptIdentity = new RuleReceiptIdentity(accepted.ReceiptId!, receipt.SemanticReceiptSha256);
        var before = await SnapshotAsync(invocation.Identity);

        var stale = await store.ConsumeRoundAsync(token, receiptIdentity);

        Assert.Equal(RuleTransitionDisposition.RevisionConflict, stale.Disposition);
        AssertNoTransitionPayload(stale);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));

        var consumed = await store.ConsumeRoundAsync(pending.CurrentToken!, receiptIdentity);
        Assert.Equal(RuleTransitionDisposition.Consumed, consumed.Disposition);
        Assert.NotNull(consumed.TransitionId);
        Assert.Equal(accepted.ReceiptId, consumed.ReceiptId);
        Assert.Null(consumed.TerminalId);
        Assert.NotNull(consumed.NextRoundId);
        Assert.Equal(3, consumed.ModelRevision);
        var afterConsume = await SnapshotAsync(invocation.Identity);

        var replay = await CreateStore().ConsumeRoundAsync(token, receiptIdentity);

        Assert.Equal(RuleTransitionDisposition.Replay, replay.Disposition);
        Assert.Equal(consumed.TransitionId, replay.TransitionId);
        Assert.Equal(consumed.ReceiptId, replay.ReceiptId);
        Assert.Equal(consumed.TerminalId, replay.TerminalId);
        Assert.Equal(consumed.NextRoundId, replay.NextRoundId);
        Assert.Equal(consumed.ModelRevision, replay.ModelRevision);
        Assert.Equal(afterConsume, await SnapshotAsync(invocation.Identity));

        var changed = await store.ConsumeRoundAsync(token,
            new(receiptIdentity.ReceiptId, new string('c', 64)));
        Assert.Equal(RuleTransitionDisposition.Conflict, changed.Disposition);
        AssertNoTransitionPayload(changed);
        Assert.Equal(afterConsume, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Final_consume_persists_terminal_and_native_delivery_together()
    {
        var invocation = NewRun(3, native: true);
        var (store, token, input) = await StartAsync(invocation);
        RuleTransitionConfirmation? finalTransition = null;
        for (var ordinal = 0; ordinal < 3; ordinal++)
        {
            var receipt = evaluator.Evaluate(input);
            var accepted = await store.AcceptRoundAsync(token, Submission(receipt, token, $"native-attempt-{ordinal}"));
            Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
            var pending = await store.ReadWorkAsync(token);
            var consume = await store.ConsumeRoundAsync(pending.CurrentToken!,
                new(pending.PendingReceipt!.ReceiptId, pending.PendingReceipt.Receipt.SemanticReceiptSha256));
            Assert.Equal(RuleTransitionDisposition.Consumed, consume.Disposition);
            if (ordinal < 2)
            {
                var next = await store.ReadWorkAsync(pending.CurrentToken!);
                token = next.CurrentToken!;
                input = next.RoundInput!;
            }
            else
                finalTransition = consume;
        }

        Assert.NotNull(finalTransition);
        Assert.Equal(RuleTerminalKind.Faulted, (await store.ReadTerminalAsync(invocation.Identity)).Result?.Kind);
        Assert.NotNull(finalTransition.TerminalId);
        Assert.Null(finalTransition.NextRoundId);
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.SingleAsync(candidate => candidate.RunKey == RunKey(invocation.Identity));
        var terminal = RuleRecordCodec.ReadTerminal(Encoding.UTF8.GetBytes(row.TerminalJson!));
        var delivery = RuleRecordCodec.ReadDeliveryRecord(Encoding.UTF8.GetBytes(row.DeliveryPayloadJson!));
        var finalReceipt = await db.Receipts.SingleAsync(candidate => candidate.RunKey == row.RunKey && candidate.Ordinal == 2);
        Assert.Equal((int)RuleRunStatus.Faulted, row.Status);
        Assert.Equal(finalTransition.TerminalId, row.TerminalId64);
        Assert.Equal(row.TerminalId64, terminal.TerminalId);
        Assert.Equal("rules.round-budget-exhausted", terminal.Diagnostic?.Code);
        Assert.Equal(["final", "joined", "left", "right", "seed"], terminal.ResultFacts.Select(fact => fact.Atom));
        Assert.Equal(row.DeliveryId64, delivery.DeliveryId);
        Assert.Equal(row.DeliveryPayloadSha25664, delivery.CanonicalPayloadSha256);
        Assert.Equal(terminal.TerminalId, delivery.Result.TerminalId);
        Assert.Equal(invocation.NativeDestination, delivery.Destination);
        Assert.False(row.DeliveryAcknowledged);
        Assert.Equal(finalTransition.ModelRevision, finalReceipt.ConsumedModelRevision);
    }

    private async Task<(EfRuleRunStore Store, RuleWorkToken Token, RuleRoundInput Input)> StartAsync(RuleInvocationEnvelope invocation, EfRuleRunStore? store = null)
    {
        store ??= CreateStore();
        var admission = await store.AdmitAsync(new(invocation));
        Assert.Equal(RuleAdmissionDisposition.Admitted, admission.Disposition);
        Assert.Equal(1, admission.ModelRevision);
        var claim = await store.ClaimWorkAsync(new("round-worker", [invocation.ExpectedImplementation],
            [invocation.InvocationSchemaVersion], [invocation.ExpectedImplementation.ReceiptSchemaVersion],
            [invocation.ExpectedImplementation.StateSchemaVersion]));
        Assert.Equal(RuleWorkClaimDisposition.Claimed, claim.Disposition);
        Assert.NotNull(claim.Token);
        var read = await store.ReadWorkAsync(claim.Token);
        Assert.Equal(RuleWorkReadDisposition.EvaluateRound, read.Disposition);
        Assert.NotNull(read.CurrentToken);
        Assert.NotNull(read.RoundInput);
        return (store, read.CurrentToken, read.RoundInput);
    }

    private EfRuleRunStore CreateStore(IRuleRoundValidator? roundValidator = null, params IInterceptor[] interceptors) =>
        new(interceptors.Length == 0 ? fixture.Contexts : new RuleStoreTestFixture.SqliteContextFactory(fixture.DatabasePath, interceptors),
            new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready),
            TimeSpan.FromDays(1), roundValidator ?? evaluator);

    private static RuleInvocationEnvelope NewRun(int maxRounds, bool native = false)
    {
        var template = RuleStoreTestFixture.NewInvocation();
        return Copy(template, maxRounds: maxRounds, destination: native ? Destination() : null);
    }

    private static RuleRoundSubmission Submission(RuleRoundReceipt receipt, RuleWorkToken token, string attemptId) =>
        new(receipt, new(attemptId, token.Lease.Owner, token.Lease.Fence));

    private static RuleRoundReceipt RehashReceipt(RuleRoundReceipt source,
        IReadOnlyList<RuleFact>? additions = null, IReadOnlyList<RuleFiringEvidence>? firings = null)
    {
        var unsigned = new RuleRoundReceipt(source.Identity, source.ImplementationPinHash,
            additions ?? source.AddedFacts, firings ?? source.FiringEvidence, new string('0', 64));
        var digest = RuleRecordCodec.Hash(RuleRecordCodec.ReceiptBytes(unsigned));
        return new(unsigned.Identity, unsigned.ImplementationPinHash, unsigned.AddedFacts, unsigned.FiringEvidence, digest);
    }

    private async Task<StoreSnapshot> SnapshotAsync(RuleRunIdentity identity)
    {
        await using var db = fixture.Contexts.CreateDbContext();
        var runKey = RunKey(identity);
        var row = await db.Runs.SingleAsync(candidate => candidate.RunKey == runKey);
        var receipts = await db.Receipts.AsNoTracking().Where(candidate => candidate.RunKey == runKey)
            .OrderBy(candidate => candidate.Ordinal).ToArrayAsync();
        var facts = await db.Facts.AsNoTracking().Where(candidate => candidate.RunKey == runKey)
            .OrderBy(candidate => candidate.FactIndex).ToArrayAsync();
        return new(row.ModelRevision, row.CommittedRounds, row.FactsJson, row.FactsSha25664,
            Convert.ToBase64String(row.ConcurrencyToken), row.Status, row.PendingReceiptId64,
            System.Text.Json.JsonSerializer.Serialize(receipts), System.Text.Json.JsonSerializer.Serialize(facts),
            System.Text.Json.JsonSerializer.Serialize(row));
    }

    private async Task<RuleFact[]> ReadCurrentFactsAsync(RuleRunIdentity identity)
    {
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.RunKey == RunKey(identity));
        return RuleRecordCodec.ReadFacts(Encoding.UTF8.GetBytes(row.FactsJson)).ToArray();
    }

    private async Task<RuleFactHistoryRecord[]> ReadFactHistoryAsync(RuleRunIdentity identity)
    {
        await using var db = fixture.Contexts.CreateDbContext();
        return await db.Facts.AsNoTracking().Where(candidate => candidate.RunKey == RunKey(identity))
            .OrderBy(candidate => candidate.FactIndex).ToArrayAsync();
    }

    private static string RunKey(RuleRunIdentity identity) => RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(identity));

    private static void AssertNoAcceptancePayload(RuleReceiptConfirmation confirmation)
    {
        Assert.Null(confirmation.ReceiptId);
        Assert.Null(confirmation.ModelRevision);
        Assert.Null(confirmation.TerminalId);
        Assert.Null(confirmation.NextRoundId);
    }

    private static void AssertNoTransitionPayload(RuleTransitionConfirmation confirmation)
    {
        Assert.Null(confirmation.TransitionId);
        Assert.Null(confirmation.ReceiptId);
        Assert.Null(confirmation.TerminalId);
        Assert.Null(confirmation.NextRoundId);
        Assert.Null(confirmation.ModelRevision);
    }

    private sealed record StoreSnapshot(long ModelRevision, int CommittedRounds, string FactsJson,
        string FactsHash, string ConcurrencyToken, int Status, string? PendingReceiptId,
        string Receipts, string FactsHistory, string FullRun);

    private sealed class FixedRoundValidator(RuleValidationDisposition disposition) : IRuleRoundValidator
    {
        internal int Calls { get; private set; }

        public ValueTask<RuleRoundValidationResult> ValidateAsync(RuleRoundValidationInput input,
            RuleRoundReceipt candidate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(new RuleRoundValidationResult(disposition, null));
        }
    }
}
