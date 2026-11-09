using System.Data.Common;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Elsa.Rules.Tests.RuleStoreTestInterceptors;

namespace Elsa.Rules.Tests;

public sealed partial class RuleRoundStoreTests
{
    [Theory]
    [InlineData("cancelled")]
    [InlineData("reclaimed")]
    [InlineData("expired")]
    public async Task Acceptance_rechecks_authority_after_semantic_validation(string outcome)
    {
        var invocation = NewRun(4);
        var clock = new SqliteClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = CreateStore(evaluator, clock);
        var (_, token, input) = await StartAsync(invocation, store);
        var barrier = new PausingRoundValidator(RuleValidationDisposition.Equivalent);
        var validatingStore = CreateStore(barrier, clock);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var accepting = validatingStore.AcceptRoundAsync(token,
            Submission(evaluator.Evaluate(input), token, "late-attempt"), deadline.Token).AsTask();
        await barrier.Entered.Task.WaitAsync(deadline.Token);
        try
        {
            if (outcome == "cancelled")
                Assert.Equal(RuleCancellationDisposition.Cancelled, (await store.CancelAsync(new(invocation))).Disposition);
            else
            {
                clock.Set(token.Lease.ExpiresAt.UtcTicks);
                if (outcome == "reclaimed")
                {
                    var claim = await store.ClaimWorkAsync(new("replacement", [invocation.ExpectedImplementation],
                        [invocation.InvocationSchemaVersion], [invocation.ExpectedImplementation.ReceiptSchemaVersion],
                        [invocation.ExpectedImplementation.StateSchemaVersion]));
                    Assert.Equal(RuleWorkClaimDisposition.Claimed, claim.Disposition);
                    Assert.Equal(token.Lease.Fence + 1, claim.Token?.Lease.Fence);
                }
            }
            var before = await SnapshotAsync(invocation.Identity);
            barrier.Release.TrySetResult();
            var result = await accepting;
            Assert.Equal(RuleReceiptDisposition.LeaseLost, result.Disposition);
            AssertNoAcceptancePayload(result);
            Assert.Equal(before, await SnapshotAsync(invocation.Identity));
        }
        finally { barrier.Release.TrySetResult(); }
    }

    [Theory]
    [InlineData(RuleValidationDisposition.Invalid)]
    [InlineData(RuleValidationDisposition.UnsupportedFormat)]
    public async Task Acceptance_rereads_an_equivalent_winner_even_after_unsuccessful_validation(RuleValidationDisposition disposition)
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        var barrier = new PausingRoundValidator(disposition);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var losing = CreateStore(barrier).AcceptRoundAsync(token,
            Submission(receipt, token, "losing-attempt"), deadline.Token).AsTask();
        await barrier.Entered.Task.WaitAsync(deadline.Token);
        try
        {
            var winner = await store.AcceptRoundAsync(token, Submission(receipt, token, "winner-attempt"));
            Assert.Equal(RuleReceiptDisposition.Accepted, winner.Disposition);
            var before = await SnapshotAsync(invocation.Identity);
            barrier.Release.TrySetResult();
            var replay = await losing;
            Assert.Equal(RuleReceiptDisposition.Replay, replay.Disposition);
            Assert.Equal(winner.ReceiptId, replay.ReceiptId);
            Assert.Equal(winner.ModelRevision, replay.ModelRevision);
            Assert.Equal(before, await SnapshotAsync(invocation.Identity));
        }
        finally { barrier.Release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expiry_after_save_rolls_back_the_entire_acceptance_or_consumption(bool consuming)
    {
        // Budget one makes consumption write a terminal and native outbox as well as facts and history.
        var invocation = NewRun(1, native: true);
        var clock = new SqliteClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = CreateStore(evaluator, clock);
        var (_, token, input) = await StartAsync(invocation, store);
        var receipt = evaluator.Evaluate(input);
        RuleReceiptIdentity? identity = null;
        if (consuming)
        {
            var accepted = await store.AcceptRoundAsync(token, Submission(receipt, token, "accepted"));
            Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
            token = (await store.ReadWorkAsync(token)).CurrentToken!;
            identity = new(accepted.ReceiptId!, receipt.SemanticReceiptSha256);
        }
        var before = await SnapshotAsync(invocation.Identity);
        var expiring = CreateStore(evaluator, clock, new ExpireAfterSave(clock, token.Lease.ExpiresAt.UtcTicks));
        if (consuming)
        {
            var result = await expiring.ConsumeRoundAsync(token, identity!);
            Assert.Equal(RuleTransitionDisposition.LeaseLost, result.Disposition);
            AssertNoTransitionPayload(result);
        }
        else
        {
            var result = await expiring.AcceptRoundAsync(token, Submission(receipt, token, "expires"));
            Assert.Equal(RuleReceiptDisposition.LeaseLost, result.Disposition);
            AssertNoAcceptancePayload(result);
        }
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Competing_consumers_commit_once_and_replay_the_same_confirmation()
    {
        var invocation = NewRun(1, native: true);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        var accepted = await store.AcceptRoundAsync(token, Submission(receipt, token, "accepted"));
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        token = (await store.ReadWorkAsync(token)).CurrentToken!;
        var identity = new RuleReceiptIdentity(accepted.ReceiptId!, receipt.SemanticReceiptSha256);
        var barrier = new FirstTwoRunReads();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await Task.WhenAll(
            CreateStore(evaluator, barrier).ConsumeRoundAsync(token, identity, deadline.Token).AsTask(),
            CreateStore(evaluator, barrier).ConsumeRoundAsync(token, identity, deadline.Token).AsTask());

        var committed = Assert.Single(results, result => result.Disposition == RuleTransitionDisposition.Consumed);
        var replay = Assert.Single(results, result => result.Disposition == RuleTransitionDisposition.Replay);
        Assert.Equal(committed.TransitionId, replay.TransitionId);
        Assert.Equal(committed.TerminalId, replay.TerminalId);
        Assert.Equal(committed.ModelRevision, replay.ModelRevision);
        Assert.Equal(1, (await store.FindAsync(invocation.Identity)).View?.CommittedRounds);
        Assert.Equal(new[] { "left", "right", "seed" }, (await ReadFactHistoryAsync(invocation.Identity))
            .Select(fact => fact.Atom).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("read")]
    [InlineData("accept-replay")]
    [InlineData("consume")]
    public async Task A_related_receipt_read_straddling_consumption_retries_a_consistent_run_snapshot(string operation)
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        var submission = Submission(receipt, token, "accepted");
        var accepted = await store.AcceptRoundAsync(token, submission);
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        token = (await store.ReadWorkAsync(token)).CurrentToken!;
        var identity = new RuleReceiptIdentity(accepted.ReceiptId!, receipt.SemanticReceiptSha256);
        RuleTransitionConfirmation? winner = null;
        var interleaved = CreateStore(evaluator, new BeforeReceiptRead(async () =>
        {
            winner = await store.ConsumeRoundAsync(token, identity);
            Assert.Equal(RuleTransitionDisposition.Consumed, winner.Disposition);
        }));
        switch (operation)
        {
            case "read":
                var read = await interleaved.ReadWorkAsync(token);
                Assert.Equal(RuleWorkReadDisposition.EvaluateRound, read.Disposition);
                Assert.Equal(1, read.RoundInput?.Identity.Ordinal);
                Assert.Equal(3, read.CurrentToken?.ExpectedModelRevision);
                Assert.Null(read.PendingReceipt);
                break;
            case "accept-replay":
                var replay = await interleaved.AcceptRoundAsync(token, submission);
                Assert.Equal(RuleReceiptDisposition.Replay, replay.Disposition);
                Assert.Equal(accepted.ReceiptId, replay.ReceiptId);
                Assert.Equal(accepted.ModelRevision, replay.ModelRevision);
                Assert.Null(replay.NextRoundId);
                break;
            case "consume":
                var consumed = await interleaved.ConsumeRoundAsync(token, identity);
                Assert.Equal(RuleTransitionDisposition.Replay, consumed.Disposition);
                Assert.Equal(winner?.TransitionId, consumed.TransitionId);
                Assert.Equal(winner?.NextRoundId, consumed.NextRoundId);
                Assert.Equal(winner?.ModelRevision, consumed.ModelRevision);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
        Assert.NotNull(winner);
        Assert.Equal(1, (await store.FindAsync(invocation.Identity)).View?.CommittedRounds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Cancellation_racing_consumption_preserves_the_committed_winner(int maxRounds)
    {
        var invocation = NewRun(maxRounds, native: true);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        var accepted = await store.AcceptRoundAsync(token, Submission(receipt, token, "accepted"));
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        token = (await store.ReadWorkAsync(token)).CurrentToken!;
        var barrier = new FirstTwoRunReads();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var consuming = CreateStore(evaluator, barrier).ConsumeRoundAsync(token,
            new(accepted.ReceiptId!, receipt.SemanticReceiptSha256), deadline.Token).AsTask();
        var cancelling = CreateStore(evaluator, barrier).CancelAsync(new(invocation), deadline.Token).AsTask();
        await Task.WhenAll(consuming, cancelling);
        var consumed = await consuming;
        var cancelled = await cancelling;
        var terminal = (await store.ReadTerminalAsync(invocation.Identity)).Result;
        Assert.NotNull(terminal);
        if (consumed.Disposition == RuleTransitionDisposition.Consumed)
        {
            Assert.Equal(1, terminal.CommittedRounds);
            Assert.Equal(new[] { "left", "right", "seed" }, terminal.ResultFacts.Select(fact => fact.Atom));
            Assert.Equal(maxRounds == 1 ? RuleTerminalKind.Faulted : RuleTerminalKind.Cancelled, terminal.Kind);
            Assert.Equal(maxRounds == 1 ? RuleCancellationDisposition.TerminalUnchanged : RuleCancellationDisposition.Cancelled,
                cancelled.Disposition);
        }
        else
        {
            Assert.Equal(RuleTransitionDisposition.TerminalUnchanged, consumed.Disposition);
            Assert.Equal(RuleCancellationDisposition.Cancelled, cancelled.Disposition);
            Assert.Equal(RuleTerminalKind.Cancelled, terminal.Kind);
            Assert.Equal(0, terminal.CommittedRounds);
            Assert.Equal(new[] { "seed" }, terminal.ResultFacts.Select(fact => fact.Atom));
        }
        var before = await SnapshotAsync(invocation.Identity);
        Assert.Equal(terminal.Kind == RuleTerminalKind.Cancelled ? RuleCancellationDisposition.Replay : RuleCancellationDisposition.TerminalUnchanged,
            (await store.CancelAsync(new(invocation))).Disposition);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Read_work_refreshes_a_concurrent_renewal_across_the_old_expiry()
    {
        var invocation = NewRun(4);
        var clock = new SqliteClock(DateTimeOffset.UtcNow.UtcTicks);
        var store = CreateStore(evaluator, clock);
        var (_, token, input) = await StartAsync(invocation, store);
        Assert.Equal(RuleReceiptDisposition.Accepted,
            (await store.AcceptRoundAsync(token, Submission(evaluator.Evaluate(input), token, "accepted"))).Disposition);
        RuleWorkLease? renewed = null;
        var reading = CreateStore(evaluator, clock, new BeforeReceiptRead(async () =>
        {
            clock.Set(token.Lease.ExpiresAt.UtcTicks - 1);
            var renewal = await store.RenewAsync(token.Lease);
            Assert.Equal(RuleLeaseDisposition.Renewed, renewal.Disposition);
            renewed = renewal.Lease;
            clock.Set(token.Lease.ExpiresAt.UtcTicks + 1);
        }));
        var read = await reading.ReadWorkAsync(token);
        Assert.Equal(RuleWorkReadDisposition.ConsumeReceipt, read.Disposition);
        Assert.NotNull(renewed);
        Assert.Equal(renewed, read.CurrentToken?.Lease);
        Assert.Equal(2, read.CurrentToken?.ExpectedModelRevision);
    }

    private sealed class PausingRoundValidator(RuleValidationDisposition disposition) : IRuleRoundValidator
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RuleRoundValidationResult> ValidateAsync(RuleRoundValidationInput input,
            RuleRoundReceipt candidate, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(disposition, null);
        }
    }

    private sealed class BeforeReceiptRead(Func<Task> action) : DbCommandInterceptor
    {
        private int _armed = 1;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("elsa_rules_runtime_receipts", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _armed, 0) == 1)
                await action();
            return result;
        }
    }
}
