using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Recovery;

public sealed partial class SpecialistEfJournalTests
{
    private static SpecialistTurnCancellation Cancellation(SpecialistInvocationIdentity? identity = null, string? child = null) =>
        new(identity ?? Identity, child, "cancel-commit:cancel-intent", new string('a', 64), Now.AddSeconds(1));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_cancellation_of_uncertainty_preserves_previous_history_and_a_later_owner(bool durable)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var first = Document with { DeliveryProtocol = 1, Deliveries = [new(Identity)], Conversation = new(new string('e', 64), 0) };
        await store.StageAsync(first, default);
        Assert.NotNull(await store.ClaimAsync(first.Handle, Identity, "accepted-child", "accepted-fence", Now, default));
        Assert.True(await store.DispatchAsync(first.Handle, Identity, new("accepted-fence", 1), Now, default));
        Assert.True(await store.AcceptAsync(first.Handle, Identity, new("accepted-fence", 1), "prior-history-and-result", Now, default));
        var uncertainIdentity = Identity with { ActivityExecutionId = "uncertain-parent", OperationKey = "uncertain-operation" };
        var uncertain = first with { Handle = new string('b', 64), Identity = uncertainIdentity, Deliveries = [new(uncertainIdentity)],
            Key = SpecialistTurnDocument.CalculateOperationKey(uncertainIdentity), Conversation = first.Conversation with { ExpectedRevision = 1 } };
        await store.StageAsync(uncertain, default);
        Assert.NotNull(await store.ClaimAsync(uncertain.Handle, uncertainIdentity, "uncertain-child", "uncertain-fence", Now, default));
        Assert.True(await store.DispatchAsync(uncertain.Handle, uncertainIdentity, new("uncertain-fence", 1), Now, default));
        Assert.NotNull(await store.ReserveModelRequestAsync(uncertain.Handle, uncertainIdentity, new("uncertain-fence", 1), Reservation(0), Now, default));
        await store.FailAsync(uncertain.Handle, uncertainIdentity, new("uncertain-fence", 1), "lost-response", true, default);
        var cancellation = Cancellation(uncertainIdentity, "uncertain-child");
        Assert.Equal(SpecialistCancellationOutcome.Cancelled, (await store.CancelAsync(cancellation, default)).Outcome);
        var preserved = (await store.ReadConversationAsync(first.Conversation.ConversationId, Identity, default))!;
        Assert.Equal(1, preserved.Revision);
        Assert.Equal(first.Handle, preserved.AcceptedTurn!.Handle);
        Assert.False(preserved.OutcomeUnknown);
        Assert.Null(preserved.ActiveTurnHandle);
        Assert.Equal("prior-history-and-result", (await store.ReadAsync(first.Handle, Identity, default))!.ProtectedResult);
        Assert.Single((await store.ReadAsync(uncertain.Handle, uncertainIdentity, default))!.ModelRequests!);
        var laterIdentity = Identity with { ActivityExecutionId = "later-parent", OperationKey = "later-operation" };
        var later = uncertain with { Handle = new string('c', 64), Identity = laterIdentity, Deliveries = [new(laterIdentity)], Key = SpecialistTurnDocument.CalculateOperationKey(laterIdentity) };
        await store.StageAsync(later, default);
        Assert.NotNull(await store.ClaimAsync(later.Handle, laterIdentity, "later-child", "later-fence", Now, default));
        var laterOwner = (await store.ReadConversationAsync(first.Conversation.ConversationId, Identity, default))!;
        Assert.Equal(SpecialistCancellationOutcome.Cancelled, (await store.CancelAsync(cancellation, default)).Outcome);
        Assert.Equal(SpecialistCancellationOutcome.AlreadyAccepted, (await store.CancelAsync(Cancellation(Identity, "accepted-child"), default)).Outcome);
        Assert.Equal(laterOwner, await store.ReadConversationAsync(first.Conversation.ConversationId, Identity, default));
        Assert.Equal("later-fence", laterOwner.ActiveFence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preadmission_cancellation_blocks_the_exact_parent_but_preserves_a_new_native_delivery(bool durable)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var cancelled = Cancellation();
        var receipt = await store.CancelAsync(cancelled, default);
        Assert.Equal(SpecialistCancellationOutcome.CancelledBeforeAdmission, receipt.Outcome);
        Assert.Null(receipt.AdmissionGeneration);
        Assert.Equal(receipt, await store.CancelAsync(cancelled, default));
        Assert.Equal(receipt, await store.CancelAsync(cancelled with { SourceId = "another-commit:same-intent" }, default));
        var refusal = await Assert.ThrowsAsync<SpecialistJournalException>(() => store.StageAsync(Document, default).AsTask());
        Assert.Equal("specialist.journal.cancelled", refusal.Code);
        Assert.Null(await store.ReadAsync(Document.Handle, Identity, default));
        await Assert.ThrowsAsync<SpecialistJournalException>(() => store.CancelAsync(cancelled with { SourceFingerprint = new string('b', 64) }, default).AsTask());
        var nextIdentity = Identity with { ActivityExecutionId = "new-parent" };
        var next = Document with { Identity = nextIdentity, DeliveryProtocol = 1, Deliveries = [new(nextIdentity)] };
        await store.StageAsync(next, default);
        Assert.NotNull(await store.ClaimAsync(next.Handle, nextIdentity, "new-child", "new-fence", Now, default));
        Assert.Equal(receipt, await store.CancelAsync(cancelled, default));
        Assert.True(await store.DispatchAsync(next.Handle, nextIdentity, new("new-fence", 1), Now, default));
        Assert.True(await store.AcceptAsync(next.Handle, nextIdentity, new("new-fence", 1), "new-accepted-pair", Now, default));
        Assert.Equal("new-accepted-pair", (await store.ReadAsync(next.Handle, nextIdentity, default))!.ProtectedResult);
        if (durable)
        {
            await using var fresh = database.Context();
            Assert.Equal(receipt, await Store(fresh).CancelAsync(cancelled, default));
        }
    }

    public static IEnumerable<object[]> CancellationWinners() =>
        from durable in new[] { false, true }
        from phase in new[] { "prepared", "claimed", "dispatching", "unknown", "accepted", "failed" }
        select new object[] { durable, phase };

    [Theory]
    [MemberData(nameof(CancellationWinners))]
    public async Task Cancellation_winners_preserve_history_exposure_and_terminal_decisions(bool durable, string phase)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var document = Document with { DeliveryProtocol = 1, Deliveries = [new(Identity)], Conversation = new(new string('e', 64), 0) };
        await store.StageAsync(document, default);
        var token = new SpecialistTurnLeaseToken("fence", 1);
        if (phase != "prepared") Assert.NotNull(await store.ClaimAsync(document.Handle, Identity, "child", "fence", Now, default));
        if (phase is not ("prepared" or "claimed"))
        {
            Assert.True(await store.DispatchAsync(document.Handle, Identity, token, Now, default));
            Assert.NotNull(await store.ReserveModelRequestAsync(document.Handle, Identity, token, Reservation(0), Now, default));
        }
        if (phase == "accepted")
        {
            var request = Assert.Single((await store.ReadAsync(document.Handle, Identity, default))!.ModelRequests!);
            Assert.True(await store.CompleteModelResponseAsync(document.Handle, Identity, token, [request.Id], new(Now, true, 1, 2, 3, "observed"), Now, default));
            Assert.True(await store.AcceptAsync(document.Handle, Identity, token, "immutable-accepted-pair", Now, default));
        }
        if (phase is "unknown" or "failed") await store.FailAsync(document.Handle, Identity, token, "before-cancel", phase == "unknown", default);
        var before = (await store.ReadAsync(document.Handle, Identity, default))!;
        var cancelled = Cancellation(child: phase == "prepared" ? null : "child");
        var receipt = await store.CancelAsync(cancelled, default);
        Assert.Equal(phase == "accepted" ? SpecialistCancellationOutcome.AlreadyAccepted : phase == "failed" ? SpecialistCancellationOutcome.AlreadyTerminal : SpecialistCancellationOutcome.Cancelled, receipt.Outcome);
        Assert.Equal(phase == "prepared" ? 0 : 1, receipt.AdmissionGeneration);
        var after = (await store.ReadAsync(document.Handle, Identity, default))!;
        Assert.Equal(phase == "accepted" ? SpecialistTurnStatus.Accepted : phase == "failed" ? SpecialistTurnStatus.Failed : SpecialistTurnStatus.Cancelled, after.Status);
        Assert.Equal(before.ProtectedRequest, after.ProtectedRequest);
        Assert.Equal(before.ProtectedResult, after.ProtectedResult);
        Assert.Equal(before.ModelRequests, after.ModelRequests);
        Assert.Equal(before.ResponseObservation, after.ResponseObservation);
        Assert.False(await store.AcceptAsync(document.Handle, Identity, token, "late-worker", Now, default));
        await store.FailAsync(document.Handle, Identity, token, "late-worker", true, default);
        Assert.Equal(after.Status, (await store.ReadAsync(document.Handle, Identity, default))!.Status);
        Assert.Null(await store.ClaimAsync(document.Handle, Identity, "another-child", "another-fence", Now.AddMinutes(1), default));
        if (phase == "prepared") Assert.Null(await store.ReadConversationAsync(document.Conversation.ConversationId, Identity, default));
        else
        {
            var conversation = (await store.ReadConversationAsync(document.Conversation.ConversationId, Identity, default))!;
            Assert.Equal(phase == "accepted" ? 1 : 0, conversation.Revision);
            Assert.Null(conversation.ActiveTurnHandle);
            Assert.False(conversation.OutcomeUnknown);
        }
        Assert.Equal(receipt, await store.CancelAsync(cancelled, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_delayed_old_parent_cancellation_cannot_invalidate_a_new_claim_generation(bool durable)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var document = Document with { DeliveryProtocol = 1, Deliveries = [new(Identity)], Conversation = new(new string('e', 64), 0) };
        await store.StageAsync(document, default);
        Assert.NotNull(await store.ClaimAsync(document.Handle, Identity, "old-child", "old-fence", Now, default));
        var successor = Identity with { ActivityExecutionId = "successor" };
        await store.StageAsync(document with { Identity = successor, Deliveries = [new(successor)] }, default);
        var next = (await store.ClaimAsync(document.Handle, successor, "successor-child", "successor-fence", Now.AddSeconds(30), default))!;
        Assert.Equal(2, next.AdmissionGeneration);
        var receipt = await store.CancelAsync(Cancellation(child: "old-child"), default);
        Assert.Equal(SpecialistCancellationOutcome.OtherDeliveryUnchanged, receipt.Outcome);
        Assert.Null(receipt.AdmissionGeneration);
        Assert.Null(await store.ClaimAsync(document.Handle, Identity, "old-child", "late-old-fence", Now.AddMinutes(1), default));
        Assert.True(await store.DispatchAsync(document.Handle, successor, new("successor-fence", 2), Now.AddSeconds(31), default));
        Assert.True(await store.AcceptAsync(document.Handle, successor, new("successor-fence", 2), "successor-result", Now.AddSeconds(31), default));
        Assert.Equal(1, (await store.ReadConversationAsync(document.Conversation.ConversationId, Identity, default))!.Revision);
        Assert.Equal("successor-result", (await store.ReadAsync(document.Handle, successor, default))!.ProtectedResult);
    }
}
