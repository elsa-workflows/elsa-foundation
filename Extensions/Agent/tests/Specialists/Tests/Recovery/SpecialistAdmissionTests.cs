using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Services;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Agent.Specialists.Tests.Runtime;
using Elsa.Secrets.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Recovery;

public sealed partial class SpecialistEfJournalTests
{
    [Theory]
    [InlineData(false, "prepared")]
    [InlineData(true, "prepared")]
    [InlineData(false, "accepted")]
    [InlineData(true, "accepted")]
    [InlineData(false, "unknown")]
    [InlineData(true, "unknown")]
    public async Task Disabled_creation_is_atomic_and_does_not_rewrite_matching_retained_operations(bool durable, string phase)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var memory = new InMemorySpecialistTurnState();
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(new Access(PersistenceAccessContext.Scoped(new("tenant"))), memory);
        var candidate = Document with { DeliveryProtocol = 1, Deliveries = [new(Identity)] };
        var disabled = await Assert.ThrowsAsync<SpecialistJournalException>(() => store.StageAsync(candidate, false, default).AsTask());
        Assert.Equal("specialist.journal.admission-disabled", disabled.Code);
        Assert.NotNull(disabled.InnerException);
        if (durable)
        {
            Assert.Equal(0, await context.Set<SpecialistTurnRecord>().CountAsync());
            Assert.Equal(0, await context.Set<SpecialistOperationRecord>().CountAsync());
        }
        else { Assert.Empty(memory.Rows); Assert.Empty(memory.Operations); }
        await store.StageAsync(candidate, true, default);
        if (phase != "prepared")
        {
            Assert.NotNull(await store.ClaimAsync(candidate.Handle, Identity, "child", "fence", Now, default));
            Assert.True(await store.DispatchAsync(candidate.Handle, Identity, new("fence", 1), Now, default));
            if (phase == "accepted") Assert.True(await store.AcceptAsync(candidate.Handle, Identity, new("fence", 1), "retained-result", Now, default));
            else await store.FailAsync(candidate.Handle, Identity, new("fence", 1), "lost-response", true, default);
        }
        var before = (await store.ReadAsync(candidate.Handle, Identity, default))!;
        var replay = await store.StageAsync(candidate with { Handle = new string('d', 64), Deadline = Now.AddDays(1) }, false, default);
        Assert.Equal(before.Deliveries, replay.Deliveries);
        Assert.Equal(before with { Deliveries = null }, replay with { Deliveries = null });
        var successor = Identity with { ActivityExecutionId = "retained-successor" };
        Assert.Equal(before.Handle, (await store.StageAsync(candidate with { Identity = successor, Deliveries = [new(successor)] }, false, default)).Handle);
        var changed = await Assert.ThrowsAsync<SpecialistJournalException>(() => store.StageAsync(candidate with { RequestHash = new string('e', 64) }, false, default).AsTask());
        Assert.Equal("specialist.journal.conflict", changed.Code);
        var other = Identity with { OperationKey = "new-operation", ActivityExecutionId = "new-parent" };
        var denied = await Assert.ThrowsAsync<SpecialistJournalException>(() => store.StageAsync(candidate with { Identity = other,
            Key = SpecialistTurnDocument.CalculateOperationKey(other), Handle = new string('b', 64), Deliveries = [new(other)] }, false, default).AsTask());
        Assert.Equal("specialist.journal.admission-disabled", denied.Code);
        Assert.Equal(before.Status, (await store.ReadAsync(candidate.Handle, Identity, default))!.Status);
        if (phase == "unknown") Assert.Null(await store.ClaimAsync(candidate.Handle, successor, "new-child", "new-fence", Now.AddMinutes(1), default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_runtime_journal_still_authenticates_and_replays_accepted_deliveries(bool durable)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        await using var crypto = SpecialistJournalTests.Services();
        var access = new Access(PersistenceAccessContext.Scoped(new("tenant")));
        ISpecialistTurnStore store = durable ? Store(context) : new InMemorySpecialistTurnStore(access, new());
        var protector = crypto.GetRequiredService<ISecretValueProtector>();
        var enabled = new SpecialistTurnJournal(store, protector, access, new(true));
        var disabled = new SpecialistTurnJournal(store, protector, access, new(false));
        var request = SpecialistJournalTests.Request();
        var now = DateTimeOffset.UtcNow;
        var handle = await enabled.StageAsync(request, now, default);
        var claim = await enabled.ClaimAsync(handle, request.Identity, "first-child", now, default);
        Assert.True(await enabled.DispatchAsync(claim, now, default));
        Assert.True(await enabled.AcceptAsync(claim, new(Json("{\"Article\":\"retained\"}"), null, null, null, 1, 1), now, default));
        var before = (await enabled.ReadAsync(handle, request.Identity, default)).Document;
        var successor = request with { Identity = request.Identity with { ActivityExecutionId = "successor" } };
        Assert.Equal(handle, await disabled.StageAsync(successor, now.AddDays(1), default));
        var result = await disabled.ReplayAsync(handle, successor.Identity, "successor-child", default);
        Assert.Equal("retained", result.Result.GetProperty("Article").GetString());
        Assert.Equal(before.ProtectedRequest, (await disabled.ReadAsync(handle, successor.Identity, default)).Document.ProtectedRequest);
        Assert.Equal(before.ProtectedResult, (await disabled.ReadAsync(handle, successor.Identity, default)).Document.ProtectedResult);
        var refusal = await Assert.ThrowsAsync<SpecialistTurnException>(() => disabled.StageAsync(request with { Identity = request.Identity with { OperationKey = "new" } }, now, default).AsTask());
        Assert.Equal("specialist.turn.admission-disabled", refusal.Code);
        // The administrative creation policy is not an implicit cancellation command.
        var cancelled = await store.CancelAsync(new(request.Identity, "first-child", "cancel-source", new string('a', 64), now), default);
        Assert.Equal(SpecialistCancellationOutcome.AlreadyAccepted, cancelled.Outcome);
    }
}
