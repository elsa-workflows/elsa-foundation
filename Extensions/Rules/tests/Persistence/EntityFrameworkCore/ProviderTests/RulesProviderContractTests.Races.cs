using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore.ProviderTests;

internal static partial class RulesProviderContractTests
{
    private static async Task VerifyAdmissionRaceAsync(IDbContextFactory<RulesDbContext> contexts)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var barrier = new AdmissionBarrier();
        var store = new EfRuleRunStore(contexts, barrier);
        var invocation = MakeInvocation("admission-race");
        var first = store.AdmitAsync(new(invocation), deadline.Token).AsTask();
        var second = store.AdmitAsync(new(invocation), deadline.Token).AsTask();
        try
        {
            await barrier.BothEntered.Task.WaitAsync(deadline.Token);
            barrier.Release.TrySetResult();
            var results = await Task.WhenAll(first, second);
            Assert.Single(results, result => result.Disposition == RuleAdmissionDisposition.Admitted);
            Assert.Single(results, result => result.Disposition == RuleAdmissionDisposition.Replay);
            Assert.All(results, result => Assert.Equal(1, result.ModelRevision));
            await using var db = await contexts.CreateDbContextAsync();
            Assert.Equal(1, await db.Runs.CountAsync(row => row.RunKey == Key(invocation.Identity)));
            Assert.Equal(1, await db.Facts.CountAsync(row => row.RunKey == Key(invocation.Identity)));
            Assert.Equal(RuleCancellationDisposition.Cancelled, (await store.CancelAsync(new(invocation))).Disposition);
        }
        finally
        {
            barrier.Release.TrySetResult();
            await deadline.CancelAsync();
            // Finish both operations before their database/fixture can be disposed on a failed assertion.
            try { await Task.WhenAll(first, second); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
    }

    private static async Task VerifyConsumptionRaceAndSnapshotAsync(IRuleRunStore store, IRuleRunReader reader,
        IDbContextFactory<RulesDbContext> contexts)
    {
        var invocation = MakeInvocation("consume-race");
        var (token, receipt) = await StartRoundAsync(store, reader, invocation);
        var submission = new RuleRoundSubmission(receipt, new("first-winner", token.Lease.Owner, token.Lease.Fence));
        var accepted = await store.AcceptRoundAsync(token, submission);
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        var retry = await store.AcceptRoundAsync(token,
            new(receipt, new("response-loss-retry", token.Lease.Owner, token.Lease.Fence)));
        Assert.Equal(RuleReceiptDisposition.Replay, retry.Disposition);
        Assert.Equal(accepted.ReceiptId, retry.ReceiptId);
        var snapshot = (await reader.ReadProgressAsync(invocation.Identity, null, 1)).Page!;
        Assert.Equal("seed", Assert.Single(snapshot.Items).Key);
        Assert.NotNull(snapshot.Next);
        token = (await reader.ReadWorkAsync(token)).CurrentToken!;
        var identity = new RuleReceiptIdentity(accepted.ReceiptId!, receipt.SemanticReceiptSha256);
        using var reads = new CountdownEvent(2);
        var observed = 0;
        var concurrentContexts = new ObservedContextFactory(contexts, db => db.ChangeTracker.Tracked += (_, args) =>
        {
            if (!args.FromQuery || args.Entry.Entity is not RuleRunRecord row || Interlocked.Increment(ref observed) > 2) return;
            Assert.Equal(token.ExpectedModelRevision, row.ModelRevision);
            reads.Signal();
            Assert.True(reads.Wait(TimeSpan.FromSeconds(60)), "Both consumers must observe the same unconsumed revision.");
        });
        var concurrentStore = new EfRuleRunStore(concurrentContexts, ReadyAdmissionValidator.Instance);
        var results = await Task.WhenAll(
            Task.Run(async () => await concurrentStore.ConsumeRoundAsync(token, identity)),
            Task.Run(async () => await concurrentStore.ConsumeRoundAsync(token, identity)));
        Assert.Single(results, result => result.Disposition == RuleTransitionDisposition.Consumed);
        Assert.Single(results, result => result.Disposition == RuleTransitionDisposition.Replay);
        var transitionId = Assert.Single(results.Select(result => result.TransitionId).Distinct());
        Assert.False(string.IsNullOrWhiteSpace(transitionId));
        Assert.All(results, result => Assert.Equal(3, result.ModelRevision));
        var historical = (await reader.ReadProgressAsync(invocation.Identity, snapshot.Next, RuleLimits.MaxPageEntries)).Page!;
        var round = Assert.Single(historical.Items).Round!;
        Assert.Null(round.ConsumedModelRevision);
        Assert.Equal("first-winner", round.AcceptedAttemptId);
        var fresh = (await reader.ReadProgressAsync(invocation.Identity, null, RuleLimits.MaxPageEntries)).Page!;
        Assert.Equal(new[] { "seed", "0000000000", "left", "right" }, fresh.Items.Select(item => item.Key));
        Assert.Equal(3, fresh.Items[1].Round!.ConsumedModelRevision);
        await using var db = await contexts.CreateDbContextAsync();
        Assert.Equal(1, await db.Receipts.CountAsync(row => row.RunKey == Key(invocation.Identity)));
        Assert.Equal(3, await db.Facts.CountAsync(row => row.RunKey == Key(invocation.Identity)));
        Assert.Equal(RuleCancellationDisposition.Cancelled, (await store.CancelAsync(new(invocation))).Disposition);
    }

    private static async Task VerifyPendingCancellationAsync(IRuleRunStore store, IRuleRunReader reader)
    {
        var invocation = MakeInvocation("pending-cancel");
        var (token, receipt) = await StartRoundAsync(store, reader, invocation);
        var accepted = await store.AcceptRoundAsync(token,
            new(receipt, new("pending", token.Lease.Owner, token.Lease.Fence)));
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        token = (await reader.ReadWorkAsync(token)).CurrentToken!;
        Assert.Equal(RuleCancellationDisposition.Cancelled, (await store.CancelAsync(new(invocation))).Disposition);
        var late = await store.ConsumeRoundAsync(token, new(accepted.ReceiptId!, receipt.SemanticReceiptSha256));
        Assert.Equal(RuleTransitionDisposition.TerminalUnchanged, late.Disposition);
        var terminal = (await reader.ReadTerminalAsync(invocation.Identity)).Result!;
        Assert.Equal(RuleTerminalKind.Cancelled, terminal.Kind);
        Assert.Equal(new[] { "seed" }, terminal.ResultFacts.Select(fact => fact.Atom));
        Assert.Equal(0, terminal.CommittedRounds);
    }

    private static async Task VerifyExpiryRollbackAndReclaimAsync(IDbContextFactory<RulesDbContext> contexts)
    {
        var delayNextSave = false;
        var saveObserved = false;
        var delayed = new ObservedContextFactory(contexts, db => db.SavedChanges += (_, _) =>
        {
            if (!delayNextSave) return;
            delayNextSave = false;
            saveObserved = true;
            // Let the real database-time lease expire after writes and before commit validation.
            Thread.Sleep(TimeSpan.FromMilliseconds(1_100));
        });
        var store = new EfRuleRunStore(delayed, ReadyAdmissionValidator.Instance,
            TimeSpan.FromSeconds(1), new RuleRoundEvaluator());
        var invocation = MakeInvocation("expiry-rollback");
        var (token, receipt) = await StartRoundAsync(store, store, invocation);
        var before = await SnapshotRunAsync(contexts, invocation.Identity);
        delayNextSave = true;
        var result = await store.AcceptRoundAsync(token,
            new(receipt, new("expires-after-save", token.Lease.Owner, token.Lease.Fence)));
        Assert.True(saveObserved, "The expiry control must actually cross the post-save boundary.");
        Assert.Equal(RuleReceiptDisposition.LeaseLost, result.Disposition);
        Assert.Equal(before, await SnapshotRunAsync(contexts, invocation.Identity));
        var replacement = await store.ClaimWorkAsync(ClaimRequest(invocation, "replacement"));
        Assert.Equal(RuleWorkClaimDisposition.Claimed, replacement.Disposition);
        Assert.Equal(token.Lease.Fence + 1, replacement.Token!.Lease.Fence);
        Assert.Equal(RuleWorkReadDisposition.LeaseLost, (await store.ReadWorkAsync(token)).Disposition);
        Assert.Equal(RuleCancellationDisposition.Cancelled, (await store.CancelAsync(new(invocation))).Disposition);
    }

    private static async Task VerifyTerminalDeliveryAtomicityAsync(IDbContextFactory<RulesDbContext> contexts)
    {
        var failNextSave = false;
        var saveObserved = false;
        var observed = new ObservedContextFactory(contexts, db => db.SavedChanges += (_, _) =>
        {
            if (!failNextSave) return;
            failNextSave = false;
            saveObserved = true;
            throw new InjectedSaveFailure();
        });
        var store = new EfRuleRunStore(observed, ReadyAdmissionValidator.Instance,
            roundValidator: new RuleRoundEvaluator(), deliveryProofValidator: new ExactDeliveryProofValidator());
        // One adding round settles the native invocation with a budget fault and a delivery obligation.
        var invocation = MakeInvocation("terminal-atomicity", maxRounds: 1, native: true);
        var (token, receipt) = await StartRoundAsync(store, store, invocation);
        var accepted = await store.AcceptRoundAsync(token,
            new(receipt, new("terminal-attempt", token.Lease.Owner, token.Lease.Fence)));
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        token = (await store.ReadWorkAsync(token)).CurrentToken!;
        var identity = new RuleReceiptIdentity(accepted.ReceiptId!, receipt.SemanticReceiptSha256);
        var before = await SnapshotRunAsync(contexts, invocation.Identity);
        failNextSave = true;
        await Assert.ThrowsAsync<InjectedSaveFailure>(() => store.ConsumeRoundAsync(token, identity).AsTask());
        Assert.True(saveObserved);
        Assert.Equal(before, await SnapshotRunAsync(contexts, invocation.Identity));

        var consumed = await store.ConsumeRoundAsync(token, identity);
        Assert.Equal(RuleTransitionDisposition.Consumed, consumed.Disposition);
        Assert.False(string.IsNullOrWhiteSpace(consumed.TransitionId));
        var replay = await store.ConsumeRoundAsync(token, identity);
        Assert.Equal(RuleTransitionDisposition.Replay, replay.Disposition);
        Assert.Equal(consumed.TransitionId, replay.TransitionId);
        Assert.Equal(consumed.TerminalId, replay.TerminalId);
        await using (var db = await contexts.CreateDbContextAsync())
        {
            var row = await db.Runs.AsNoTracking().SingleAsync(row => row.RunKey == Key(invocation.Identity));
            var record = await db.Receipts.AsNoTracking().SingleAsync(row => row.RunKey == Key(invocation.Identity));
            Assert.Equal(3, row.ModelRevision);
            Assert.Equal(row.ModelRevision, record.ConsumedModelRevision);
            Assert.Equal(consumed.TerminalId, row.TerminalId64);
            Assert.Equal(row.TerminalId64, record.TerminalIdAtConsumption64);
            Assert.False(string.IsNullOrWhiteSpace(row.DeliveryId64));
            Assert.False(string.IsNullOrWhiteSpace(row.DeliveryPayloadJson));
            Assert.Equal(3, await db.Facts.CountAsync(row => row.RunKey == Key(invocation.Identity)));
        }
        var claim = await store.ClaimDeliveryAsync(new("atomic-delivery", [1]));
        Assert.Equal(RuleDeliveryClaimDisposition.Claimed, claim.Disposition);
        Assert.Equal(invocation.Identity, claim.Lease!.Identity);
        var envelope = (await store.ReadDeliveryAsync(claim.Lease)).Envelope!;
        Assert.Equal(RuleTerminalKind.Faulted, envelope.Result.Kind);
        Assert.Equal("rules.round-budget-exhausted", envelope.Result.Diagnostic?.Code);
        Assert.Equal(new[] { "left", "right", "seed" }, envelope.Result.ResultFacts.Select(fact => fact.Atom));
        Assert.Equal(RuleTransitionDisposition.Acknowledged,
            (await store.AcknowledgeDeliveryAsync(claim.Lease, new(DeliveryProof(envelope)))).Disposition);
    }

    private static async Task VerifyDeliveryExpiryRollbackAsync(IDbContextFactory<RulesDbContext> contexts)
    {
        var delayNextSave = false;
        var saveObserved = false;
        var delayed = new ObservedContextFactory(contexts, db => db.SavedChanges += (_, _) =>
        {
            if (!delayNextSave) return;
            delayNextSave = false;
            saveObserved = true;
            Thread.Sleep(TimeSpan.FromMilliseconds(1_100));
        });
        var store = new EfRuleRunStore(delayed, ReadyAdmissionValidator.Instance,
            deliveryProofValidator: new ExactDeliveryProofValidator(), deliveryLeaseDuration: TimeSpan.FromSeconds(1));
        var invocation = MakeInvocation("delivery-expiry", native: true);
        Assert.Equal(RuleCancellationDisposition.Cancelled, (await store.CancelAsync(new(invocation))).Disposition);
        var claim = await store.ClaimDeliveryAsync(new("expiring-delivery", [1]));
        Assert.Equal(RuleDeliveryClaimDisposition.Claimed, claim.Disposition);
        var lease = claim.Lease!;
        Assert.Equal(invocation.Identity, lease.Identity);
        var envelope = (await store.ReadDeliveryAsync(lease)).Envelope!;
        var proof = DeliveryProof(envelope);
        var before = await SnapshotRunAsync(contexts, invocation.Identity);
        delayNextSave = true;
        var expired = await store.AcknowledgeDeliveryAsync(lease, new(proof));
        Assert.True(saveObserved, "Acknowledgement must reach the post-save expiry boundary.");
        Assert.Equal(RuleTransitionDisposition.LeaseLost, expired.Disposition);
        Assert.Equal(before, await SnapshotRunAsync(contexts, invocation.Identity));
        // Use the normal duration for recovery; the short lease exists only for the expiry case.
        var recovery = new EfRuleRunStore(contexts, ReadyAdmissionValidator.Instance,
            deliveryProofValidator: new ExactDeliveryProofValidator());
        var replacement = await recovery.ClaimDeliveryAsync(new("replacement-delivery", [1]));
        Assert.Equal(RuleDeliveryClaimDisposition.Claimed, replacement.Disposition);
        Assert.Equal(lease.Identity, replacement.Lease!.Identity);
        Assert.Equal(lease.DeliveryId, replacement.Lease.DeliveryId);
        Assert.Equal(lease.Fence + 1, replacement.Lease.Fence);
        Assert.Equal(RuleDeliveryReadDisposition.LeaseLost, (await recovery.ReadDeliveryAsync(lease)).Disposition);
        Assert.Equal(RuleTransitionDisposition.LeaseLost,
            (await recovery.AcknowledgeDeliveryAsync(lease, new(proof))).Disposition);
        Assert.Equal(RuleTransitionDisposition.Acknowledged,
            (await recovery.AcknowledgeDeliveryAsync(replacement.Lease, new(proof))).Disposition);
    }

    private static RuleDeliveryProof DeliveryProof(RuleTerminalEnvelope envelope) => new(
        RuleDeliveryProofKind.Consumed, RuleRecordCodec.DestinationIdentity(envelope.Destination),
        envelope.Result.TerminalId, envelope.CanonicalPayloadSha256, "synthetic-parent-commit");

    private sealed class InjectedSaveFailure : Exception;

    private static async Task<(RuleWorkToken Token, RuleRoundReceipt Receipt)> StartRoundAsync(
        IRuleRunStore store, IRuleRunReader reader, RuleInvocationEnvelope invocation)
    {
        Assert.Equal(RuleAdmissionDisposition.Admitted, (await store.AdmitAsync(new(invocation))).Disposition);
        var claim = await store.ClaimWorkAsync(ClaimRequest(invocation, "invariant-worker"));
        Assert.Equal(RuleWorkClaimDisposition.Claimed, claim.Disposition);
        var work = await reader.ReadWorkAsync(claim.Token!);
        Assert.Equal(RuleWorkReadDisposition.EvaluateRound, work.Disposition);
        Assert.Equal(invocation.Identity, work.CurrentToken!.Lease.Identity);
        return (work.CurrentToken, new RuleRoundEvaluator().Evaluate(work.RoundInput!));
    }

    private static async Task<string> SnapshotRunAsync(IDbContextFactory<RulesDbContext> contexts, RuleRunIdentity identity)
    {
        await using var db = await contexts.CreateDbContextAsync();
        var key = Key(identity);
        return JsonSerializer.Serialize(new
        {
            Run = await db.Runs.AsNoTracking().SingleAsync(row => row.RunKey == key),
            Receipts = await db.Receipts.AsNoTracking().Where(row => row.RunKey == key).OrderBy(row => row.Ordinal).ToArrayAsync(),
            Facts = await db.Facts.AsNoTracking().Where(row => row.RunKey == key).OrderBy(row => row.FactIndex).ToArrayAsync()
        });
    }

    private sealed class AdmissionBarrier : IRuleAdmissionValidator
    {
        private int _entered;
        internal TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<RuleAdmissionValidationResult> ValidateAsync(RuleInvocationEnvelope invocation,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _entered) == 2) BothEntered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(RuleAdmissionValidationDisposition.Ready, null);
        }
    }

    private sealed class ObservedContextFactory(IDbContextFactory<RulesDbContext> inner, Action<RulesDbContext> observe)
        : IDbContextFactory<RulesDbContext>
    {
        public RulesDbContext CreateDbContext()
        {
            var db = inner.CreateDbContext();
            observe(db);
            return db;
        }
    }
}
