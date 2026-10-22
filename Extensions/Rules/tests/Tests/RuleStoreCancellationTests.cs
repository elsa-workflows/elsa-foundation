using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Elsa.Rules.Runtime;
using Microsoft.EntityFrameworkCore;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed class RuleStoreCancellationTests(RuleStoreTestFixture fixture) : IClassFixture<RuleStoreTestFixture>
{
    [Fact]
    public async Task Pre_admission_cancel_wins_identity_without_calling_validator()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var validator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable);
        var store = fixture.CreateStore(validator);

        var cancelled = await store.CancelAsync(new(invocation));
        var replay = await store.AdmitAsync(new(invocation));
        var changed = await store.AdmitAsync(new(RuleTestData.Copy(invocation, maxRounds: invocation.MaxRounds - 1)));
        var terminal = await store.ReadTerminalAsync(invocation.Identity);

        Assert.Equal(RuleCancellationDisposition.Cancelled, cancelled.Disposition);
        Assert.NotNull(cancelled.TerminalId);
        Assert.Equal(RuleAdmissionDisposition.AlreadyCancelled, replay.Disposition);
        Assert.Equal(cancelled.TerminalId, replay.TerminalId);
        Assert.Equal(RuleAdmissionDisposition.Conflict, changed.Disposition);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
        Assert.Equal(RuleTerminalKind.Cancelled, terminal.Result?.Kind);
        Assert.Empty(terminal.Result?.ResultFacts ?? []);
        Assert.Equal(0, validator.Calls);
    }

    [Fact]
    public async Task Active_cancel_keeps_committed_facts_and_never_applies_pending_receipt()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var validator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready);
        var store = fixture.CreateStore(validator);
        var admission = await store.AdmitAsync(new(invocation));
        Assert.Equal(RuleAdmissionDisposition.Admitted, admission.Disposition);
        var committed = RuleRecordCodec.CanonicalFacts(Facts("seed", "left", "right"));
        var pending = await SeedActiveRunWithPendingReceiptAsync(invocation, committed);

        var cancellation = await store.CancelAsync(new(invocation));
        var terminal = await store.ReadTerminalAsync(invocation.Identity);
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.SingleAsync(candidate => candidate.RunKey == Key(invocation.Identity));
        var retainedReceipt = await db.Receipts.SingleAsync(candidate => candidate.RunKey == row.RunKey && candidate.Ordinal == 1);

        Assert.Equal(RuleCancellationDisposition.Cancelled, cancellation.Disposition);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
        Assert.Equal(RuleTerminalKind.Cancelled, terminal.Result?.Kind);
        Assert.Equal(1, terminal.Result?.CommittedRounds);
        Assert.Equal(committed.Select(fact => fact.Atom), terminal.Result?.ResultFacts.Select(fact => fact.Atom));
        Assert.DoesNotContain("joined", terminal.Result?.ResultFacts.Select(fact => fact.Atom) ?? []);
        Assert.Null(retainedReceipt.ConsumedModelRevision);
        Assert.Equal(pending.ReceiptId, retainedReceipt.ReceiptId64);
        Assert.Equal(5, row.ModelRevision);
        Assert.Equal(RuleRunStatus.Cancelled, (RuleRunStatus)row.Status);
        Assert.Equal(RuleRecordCodec.FactsHash(committed), row.FactsSha25664);
    }

    [Fact]
    public async Task Pre_admission_native_cancel_persists_terminal_and_delivery_together()
    {
        var baseInvocation = RuleStoreTestFixture.NewInvocation();
        var invocation = new RuleInvocationEnvelope(baseInvocation.Identity, baseInvocation.Artifact, baseInvocation.InitialFacts,
            baseInvocation.MaxRounds, baseInvocation.ExpectedImplementation, baseInvocation.InvocationSchemaVersion, Destination());
        var validator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable);
        var store = fixture.CreateStore(validator);

        var cancellation = await store.CancelAsync(new(invocation));
        var terminal = await store.ReadTerminalAsync(invocation.Identity);
        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.SingleAsync(candidate => candidate.RunKey == Key(invocation.Identity));

        Assert.Equal(RuleCancellationDisposition.Cancelled, cancellation.Disposition);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
        Assert.NotNull(row.TerminalJson);
        Assert.NotNull(row.DeliveryId64);
        Assert.NotNull(row.DeliveryPayloadJson);
        Assert.NotNull(row.DeliveryPayloadSha25664);
        Assert.False(row.DeliveryAcknowledged);
        var delivery = RuleRecordCodec.ReadDeliveryRecord(Encoding.UTF8.GetBytes(row.DeliveryPayloadJson!));
        Assert.Equal(row.DeliveryId64, delivery.DeliveryId);
        Assert.Equal(row.DeliveryPayloadSha25664, delivery.CanonicalPayloadSha256);
        Assert.Equal(cancellation.TerminalId, delivery.Result.TerminalId);
        Assert.Equal(Destination(), delivery.Destination);
        Assert.Equal(0, validator.Calls);
    }

    [Theory]
    [InlineData(RuleAdmissionValidationDisposition.Invalid)]
    [InlineData(RuleAdmissionValidationDisposition.Unavailable)]
    [InlineData(RuleAdmissionValidationDisposition.Refused)]
    public Task Failed_validation_rereads_concurrent_cancel_winner(RuleAdmissionValidationDisposition disposition) =>
        AssertCancellationWinsAfterValidationAsync(new RuleStoreTestFixture.BarrierAdmissionValidator(disposition,
            disposition == RuleAdmissionValidationDisposition.Refused ? new RuleRunDiagnostic("rules.invalid-finite-model") : null));

    [Fact]
    public Task Unsupported_validator_rereads_concurrent_cancel_winner() =>
        AssertCancellationWinsAfterValidationAsync(new RuleStoreTestFixture.BarrierAdmissionValidator(
            RuleAdmissionValidationDisposition.Unavailable, failure: new NotSupportedException("rules.validator-unavailable")));

    private async Task AssertCancellationWinsAfterValidationAsync(RuleStoreTestFixture.BarrierAdmissionValidator barrierValidator)
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var racingStore = fixture.CreateStore(barrierValidator);
        var admissionAttempt = racingStore.AdmitAsync(new(invocation)).AsTask();
        try
        {
            await barrierValidator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var cancelValidator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready);
            var cancellation = await fixture.CreateStore(cancelValidator).CancelAsync(new(invocation));
            barrierValidator.Release.TrySetResult(true);
            var admission = await admissionAttempt.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(RuleCancellationDisposition.Cancelled, cancellation.Disposition);
            Assert.Equal(RuleAdmissionDisposition.AlreadyCancelled, admission.Disposition);
            Assert.Equal(1, barrierValidator.Calls);
            Assert.Equal(0, cancelValidator.Calls);
            Assert.Equal(cancellation.TerminalId, admission.TerminalId);
        }
        finally
        {
            barrierValidator.Release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task Retained_identity_mismatch_conflicts_without_exposing_row_payload()
    {
        var requested = RuleStoreTestFixture.NewInvocation();
        var collidingIdentity = new RuleRunIdentity(new RuleScope("other-tenant", "other-partition"), "other-invocation");
        var colliding = RuleTestData.Copy(requested, identity: collidingIdentity);
        await SeedHashCollisionAsync(requested, colliding);
        var validator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable);
        var store = fixture.CreateStore(validator);

        var admission = await store.AdmitAsync(new(requested));
        var refusal = await store.RefuseAdmissionAsync(new(requested, new RuleRunDiagnostic("rules.invalid-finite-model")));
        var cancellation = await store.CancelAsync(new(requested));
        var run = await store.FindAsync(requested.Identity);
        var terminal = await store.ReadTerminalAsync(requested.Identity);

        Assert.Equal(RuleAdmissionDisposition.Conflict, admission.Disposition);
        Assert.Null(admission.ModelRevision);
        Assert.Null(admission.TerminalId);
        Assert.Equal(RuleRefusalDisposition.Conflict, refusal.Disposition);
        Assert.Null(refusal.TerminalId);
        Assert.Equal(RuleCancellationDisposition.Conflict, cancellation.Disposition);
        Assert.Null(cancellation.ModelRevision);
        Assert.Null(cancellation.TerminalId);
        Assert.Equal(RuleRunReadDisposition.NotFound, run.Disposition);
        Assert.Null(run.View);
        Assert.Equal(RuleTerminalReadDisposition.NotFound, terminal.Disposition);
        Assert.Null(terminal.Result);
        Assert.Equal(0, validator.Calls);
    }

    private async Task<(string ReceiptId, RuleRoundReceipt Receipt)> SeedActiveRunWithPendingReceiptAsync(
        RuleInvocationEnvelope invocation, IReadOnlyList<RuleFact> committedFacts)
    {
        var inputFacts = committedFacts.ToArray();
        var roundIdentity = new RuleRoundIdentity(invocation.Identity, RuleRecordCodec.Fingerprint(invocation), 1,
            RuleRecordCodec.FactsHash(inputFacts));
        var roundInput = new RuleRoundInput(roundIdentity, invocation.Artifact, invocation.ExpectedImplementation,
            inputFacts, invocation.MaxRounds, modelRevision: 3);
        var receipt = new RuleRoundEvaluator().Evaluate(roundInput);
        var receiptId = new string('c', 64);
        var runKey = Key(invocation.Identity);
        var factsJson = Encoding.UTF8.GetString(RuleRecordCodec.FactsBytes(inputFacts));

        await using var db = fixture.Contexts.CreateDbContext();
        var row = await db.Runs.SingleAsync(candidate => candidate.RunKey == runKey);
        row.ModelRevision = 4;
        row.FactsJson = factsJson;
        row.FactsSha25664 = RuleRecordCodec.FactsHash(inputFacts);
        row.CommittedRounds = 1;
        row.PendingReceiptId64 = receiptId;
        row.WorkOwner = "worker";
        row.WorkFence = 1;
        row.WorkExpiresAtUtcTicks = DateTimeOffset.UtcNow.AddMinutes(5).UtcDateTime.Ticks;
        var nextFactIndex = await db.Facts.Where(candidate => candidate.RunKey == runKey)
            .Select(candidate => (int?)candidate.FactIndex).MaxAsync() ?? -1;
        foreach (var fact in inputFacts.Where(fact => fact.Atom != "seed"))
        {
            db.Facts.Add(new RuleFactHistoryRecord
            {
                RunKey = runKey,
                FactIndex = ++nextFactIndex,
                Atom = fact.Atom,
                FirstVisibleModelRevision = 4,
                SchemaVersion = RulesEfModule.SchemaVersion
            });
        }
        db.Receipts.Add(new RuleRoundReceiptRecord
        {
            RunKey = runKey,
            Ordinal = 1,
            ReceiptId64 = receiptId,
            InputFactsSha25664 = roundIdentity.InputStateSha256,
            ReceiptJson = Encoding.UTF8.GetString(RuleRecordCodec.ReceiptRecordBytes(receipt)),
            AcceptedAttemptId = "attempt",
            AcceptedWorkerId = "worker",
            AcceptedFence = 1,
            AcceptedModelRevision = 4,
            SchemaVersion = RulesEfModule.SchemaVersion
        });
        await db.SaveChangesAsync();
        return (receiptId, receipt);
    }

    private async Task SeedHashCollisionAsync(RuleInvocationEnvelope requested, RuleInvocationEnvelope colliding)
    {
        var terminal = new RuleTerminalResult(new string('d', 64), RuleTerminalKind.Cancelled, 0, 1, [],
            RuleRecordCodec.FactsHash([]), new RuleRunDiagnostic("private-row-diagnostic"));
        await using var db = fixture.Contexts.CreateDbContext();
        db.Runs.Add(new RuleRunRecord
        {
            RunKey = Key(requested.Identity),
            IdentityBase64 = Convert.ToBase64String(RuleRecordCodec.ScopeBytes(colliding.Identity)),
            InvocationJson = Encoding.UTF8.GetString(RuleRecordCodec.InvocationBytes(colliding)),
            Fingerprint64 = RuleRecordCodec.Fingerprint(colliding).Sha256,
            PinHash64 = RuleRecordCodec.PinHash(colliding.ExpectedImplementation),
            Admitted = false,
            ModelRevision = 1,
            ConcurrencyToken = Guid.NewGuid().ToByteArray(),
            FactsJson = Encoding.UTF8.GetString(RuleRecordCodec.FactsBytes([])),
            FactsSha25664 = RuleRecordCodec.FactsHash([]),
            CommittedRounds = 0,
            Status = (int)RuleRunStatus.Cancelled,
            TerminalId64 = terminal.TerminalId,
            TerminalJson = Encoding.UTF8.GetString(RuleRecordCodec.TerminalBytes(terminal)),
            TerminalModelRevision = 1,
            WorkFence = 0,
            DeliveryFence = 0,
            DeliveryAcknowledged = false,
            SchemaVersion = RulesEfModule.SchemaVersion
        });
        await db.SaveChangesAsync();
    }

    private static string Key(RuleRunIdentity identity) => RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(identity));
}
