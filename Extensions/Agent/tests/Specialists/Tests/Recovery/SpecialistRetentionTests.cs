using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Recovery;

public sealed partial class SpecialistEfJournalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Content_purge_is_conditional_preserves_operation_authority_and_never_reopens_acceptance(bool durable)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var candidate = Document with { DeliveryProtocol = 1, Deliveries = [new(Identity)] };
        await store.StageAsync(candidate, default);
        Assert.Empty((await store.QueryRetentionAsync(new("tenant", "tenant"), default)).Items);
        Assert.NotNull(await store.ClaimAsync(candidate.Handle, Identity, "child", "fence", Now, default));
        Assert.True(await store.DispatchAsync(candidate.Handle, Identity, new("fence", 1), Now, default));
        Assert.True(await store.AcceptAsync(candidate.Handle, Identity, new("fence", 1), "retained-result", Now, default));
        var original = Assert.Single((await store.QueryRetentionAsync(new("tenant", "tenant"), default)).Items);
        Assert.False(await store.PurgeContentAsync(original, Now, default));
        var successor = Identity with { ActivityExecutionId = "registered-during-retention-proof" };
        await store.StageAsync(candidate with { Identity = successor, Deliveries = [new(successor)] }, default);
        Assert.False(await store.PurgeContentAsync(original, Now.AddDays(31), default));
        var current = Assert.Single((await store.QueryRetentionAsync(new("tenant", "tenant"), default)).Items);
        Assert.False(await store.PurgeContentAsync(current with { Deliveries = [new(Identity)] }, Now.AddDays(31), default));
        var before = (await store.ReadAsync(candidate.Handle, Identity, default))!;
        Assert.True(await store.PurgeContentAsync(current, Now.AddDays(31), default));
        Assert.False(await store.PurgeContentAsync(current, Now.AddDays(32), default));
        var after = (await store.ReadAsync(candidate.Handle, Identity, default))!;
        Assert.Equal(before with { ProtectedRequest = null, ProtectedResult = null, ContentPurgedAt = after.ContentPurgedAt, Deliveries = after.Deliveries }, after);
        Assert.Equal(before.Deliveries, after.Deliveries);
        Assert.Equal(Now.AddDays(31), after.ContentPurgedAt);
        Assert.Null(after.ProtectedRequest);
        Assert.Null(after.ProtectedResult);
        Assert.Empty((await store.QueryRetentionAsync(new("tenant", "tenant"), default)).Items);
        var replay = await store.StageAsync(candidate with { Handle = new string('b', 64), Deadline = Now.AddDays(35) }, default);
        Assert.Equal(after.ContentPurgedAt, replay.ContentPurgedAt);
        Assert.Equal(after.Handle, replay.Handle);
        Assert.Null(replay.ProtectedRequest);
        Assert.Null(await store.ClaimAsync(candidate.Handle, Identity, "another-child", "another-fence", Now.AddDays(31), default));
        Assert.Equal(SpecialistCancellationOutcome.AlreadyAccepted,
            (await store.CancelAsync(new(Identity, "child", "late-source", new string('f', 64), Now.AddDays(31)), default)).Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retention_scan_has_finite_keyset_progress_and_explicit_scope_even_when_no_candidate_is_eligible(bool durable)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        foreach (var digit in new[] { 'a', 'b', 'c' })
        {
            var identity = Identity with { OperationKey = "scan-" + digit };
            await store.StageAsync(Document with { Handle = new string(digit, 64), Key = SpecialistTurnDocument.CalculateOperationKey(identity), Identity = identity }, default);
        }
        var first = await store.QueryRetentionAsync(new("tenant", "tenant", PageSize: 1), default);
        Assert.Empty(first.Items);
        Assert.Equal(new string('a', 64), first.NextHandle);
        var second = await store.QueryRetentionAsync(new("tenant", "tenant", first.NextHandle, 1), default);
        Assert.Equal(new string('b', 64), second.NextHandle);
        var last = await store.QueryRetentionAsync(new("tenant", "tenant", second.NextHandle, 1), default);
        Assert.Null(last.NextHandle);
        Assert.Empty((await store.QueryRetentionAsync(new(null, "tenant"), default)).Items);
        await Assert.ThrowsAnyAsync<Exception>(() => store.QueryRetentionAsync(new("foreign", "tenant"), default).AsTask());
        await Assert.ThrowsAnyAsync<Exception>(() => store.QueryRetentionAsync(new("tenant", "foreign"), default).AsTask());
        await Assert.ThrowsAnyAsync<Exception>(() => store.QueryRetentionAsync(new("tenant", "tenant", PageSize: 129), default).AsTask());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Claimed_and_unknown_conversation_work_pins_previous_history_until_explicit_settlement(bool durable)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var first = Document with { Conversation = new(new string('f', 64), 0) };
        await store.StageAsync(first, default);
        Assert.NotNull(await store.ClaimAsync(first.Handle, Identity, "first-child", "first-fence", Now, default));
        Assert.True(await store.DispatchAsync(first.Handle, Identity, new("first-fence", 1), Now, default));
        Assert.True(await store.AcceptAsync(first.Handle, Identity, new("first-fence", 1), "history", Now, default));
        var proof = Assert.Single((await store.QueryRetentionAsync(new("tenant", "tenant"), default)).Items);
        var nextIdentity = Identity with { OperationKey = "next", ActivityExecutionId = "next" };
        var next = first with { Identity = nextIdentity, Handle = new string('b', 64), Key = SpecialistTurnDocument.CalculateOperationKey(nextIdentity), Conversation = new(first.Conversation.ConversationId, 1) };
        await store.StageAsync(next, default);
        Assert.NotNull(await store.ClaimAsync(next.Handle, nextIdentity, "next-child", "next-fence", Now, default));
        Assert.False(await store.PurgeContentAsync(proof, Now.AddDays(31), default));
        Assert.True(await store.DispatchAsync(next.Handle, nextIdentity, new("next-fence", 1), Now, default));
        await store.FailAsync(next.Handle, nextIdentity, new("next-fence", 1), "lost-response", true, default);
        Assert.False(await store.PurgeContentAsync(proof, Now.AddDays(31), default));
        Assert.NotNull((await store.ReadAsync(first.Handle, Identity, default))!.ProtectedResult);
        Assert.Equal(SpecialistCancellationOutcome.Cancelled,
            (await store.CancelAsync(new(nextIdentity, "next-child", "cancel-unknown", new string('d', 64), Now.AddDays(31)), default)).Outcome);
        Assert.True(await store.PurgeContentAsync(proof, Now.AddDays(31), default));
        var conversation = (await store.ReadConversationAsync(first.Conversation.ConversationId, Identity, default))!;
        Assert.False(conversation.OutcomeUnknown);
        Assert.Null(conversation.ActiveTurnHandle);
        Assert.Equal(1, conversation.Revision);
        Assert.Equal(first.Handle, conversation.AcceptedTurn!.Handle);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Rejected_and_cancelled_content_can_expire_without_rewriting_the_original_terminal_decision(bool durable, bool cancelled)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        await store.StageAsync(Document, default);
        var cancellation = new SpecialistTurnCancellation(Identity, null, "cancel-source", new string('f', 64), Now);
        if (cancelled) Assert.Equal(SpecialistCancellationOutcome.Cancelled, (await store.CancelAsync(cancellation, default)).Outcome);
        else
        {
            Assert.NotNull(await store.ClaimAsync(Document.Handle, Identity, "child", "fence", Now, default));
            await store.FailAsync(Document.Handle, Identity, new("fence", 1), "rejected", false, default);
        }
        var before = (await store.ReadAsync(Document.Handle, Identity, default))!;
        var proof = Assert.Single((await store.QueryRetentionAsync(new("tenant", "tenant"), default)).Items);
        Assert.True(await store.PurgeContentAsync(proof, Now.AddDays(31), default));
        var after = (await store.ReadAsync(Document.Handle, Identity, default))!;
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.FailureCode, after.FailureCode);
        Assert.Null(after.ProtectedRequest);
        Assert.Null(await store.ClaimAsync(Document.Handle, Identity, "retry", "retry", Now.AddDays(31), default));
        if (cancelled) Assert.Equal(SpecialistCancellationOutcome.Cancelled, (await store.CancelAsync(cancellation, default)).Outcome);
        else Assert.NotNull((await store.StageAsync(Document, default)).ContentPurgedAt);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public async Task Missing_referenced_conversation_is_not_mistaken_for_an_uncreated_fresh_conversation(bool durable, long revision)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), new());
        var first = Document with { Conversation = new(revision == 0 ? new string('f', 64) : Document.Handle, revision) };
        await store.StageAsync(first, default);
        await store.CancelAsync(new(Identity, null, "cancel", new string('e', 64), Now), default);
        var candidate = Assert.Single((await store.QueryRetentionAsync(new("tenant", "tenant"), default)).Items);
        Assert.False(await store.PurgeContentAsync(candidate, Now.AddDays(31), default));
        Assert.NotNull((await store.ReadAsync(first.Handle, Identity, default))!.ProtectedRequest);
    }

}
