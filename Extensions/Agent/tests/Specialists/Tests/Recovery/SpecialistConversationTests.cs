using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Recovery;

public sealed class SpecialistConversationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly SpecialistInvocationIdentity Identity = new("tenant", "tenant", "workflow", "first", "first", "artifact", "hash", "version", "template");
    private static readonly string ConversationId = Token("conversation");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revision_admission_is_exclusive_scoped_and_survives_a_new_store(bool durable)
    {
        await using var fixture = await Fixture.CreateAsync(durable);
        var first = Document("first", 0);
        await fixture.Use(async store =>
        {
            await store.StageAsync(first, default);
            Assert.Null(await store.ReadConversationAsync(ConversationId, Identity, default)); // Staging is not admission.
            Assert.NotNull(await store.ClaimAsync(first.Handle, first.Identity, "child-first", "first-fence", Now, default));
            Assert.True(await store.DispatchAsync(first.Handle, first.Identity, "first-fence", Now, default));
            Assert.False(await store.AcceptAsync(first.Handle, first.Identity, "wrong-fence", "wrong", Now, default));
            Assert.True(await store.AcceptAsync(first.Handle, first.Identity, "first-fence", "first-accepted", Now, default));
        });
        var contenders = Enumerable.Range(0, 8).Select(index => Document("next-" + index, 1)).ToArray();
        foreach (var candidate in contenders) await fixture.Use(async store => { await store.StageAsync(candidate, default); });
        var claims = await Task.WhenAll(contenders.Select((candidate, index) => fixture.Use(store =>
            store.ClaimAsync(candidate.Handle, candidate.Identity, "child-" + index, "fence-" + index, Now, default).AsTask())));
        var winner = Assert.Single(claims, value => value is not null)!;
        await fixture.Use(async store =>
        {
            var conversation = (await store.ReadConversationAsync(ConversationId, Identity, default))!;
            Assert.Equal(1, conversation.Revision);
            Assert.Equal(winner.Handle, conversation.ActiveTurnHandle);
            Assert.Equal(first.Handle, conversation.AcceptedTurn!.Handle);
            Assert.True(await store.DispatchAsync(winner.Handle, winner.Identity, winner.Fence!, Now, default));
            Assert.False(await store.AcceptAsync(winner.Handle, winner.Identity, winner.Fence!, "expired", first.Deadline, default));
            Assert.True(await store.AcceptAsync(winner.Handle, winner.Identity, winner.Fence!, "second-accepted", Now, default));
            await store.FailAsync(first.Handle, first.Identity, "first-fence", "late", true, default);
            var replay = await store.StageAsync(first with { Handle = Token("replay"), Deadline = Now.AddHours(1) }, default);
            Assert.Equal("first-accepted", replay.ProtectedResult);
            Assert.Equal(2, (await store.ReadConversationAsync(ConversationId, Identity, default))!.Revision);
            foreach (var stale in contenders.Where(value => value.Handle != winner.Handle))
            {
                Assert.Null(await store.ClaimAsync(stale.Handle, stale.Identity, "stale", "stale", Now, default));
                Assert.Equal(SpecialistTurnStatus.Prepared, (await store.ReadAsync(stale.Handle, stale.Identity, default))!.Status);
            }
            foreach (var foreign in new[] { Identity with { WorkflowExecutionId = "foreign" }, Identity with { DefinitionVersionId = "other-version" },
                Identity with { TemplateHash = "other-template" } })
            {
                Assert.Null(await store.ReadConversationAsync(ConversationId, foreign, default));
            }
            await Assert.ThrowsAsync<SpecialistJournalException>(async () => await store.ReadConversationAsync(ConversationId, Identity with { TenantId = "foreign" }, default));
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Known_failure_releases_the_base_revision_but_uncertainty_blocks_continuation(bool durable, bool unknown)
    {
        await using var fixture = await Fixture.CreateAsync(durable);
        var first = Document("first", 0);
        var second = Document("second", 1);
        var third = Document("third", 1);
        await fixture.Use(async store =>
        {
            await store.StageAsync(first, default);
            Assert.NotNull(await store.ClaimAsync(first.Handle, first.Identity, "child", "first", Now, default));
            Assert.True(await store.DispatchAsync(first.Handle, first.Identity, "first", Now, default));
            Assert.True(await store.AcceptAsync(first.Handle, first.Identity, "first", "accepted", Now, default));
            await store.StageAsync(second, default);
            Assert.NotNull(await store.ClaimAsync(second.Handle, second.Identity, "child", "second", Now, default));
            Assert.True(await store.DispatchAsync(second.Handle, second.Identity, "second", Now, default));
            await store.FailAsync(second.Handle, second.Identity, "second", "fixture-rejection", unknown, default);
        });
        await fixture.Use(async store =>
        {
            var conversation = (await store.ReadConversationAsync(ConversationId, Identity, default))!;
            Assert.Equal(1, conversation.Revision);
            Assert.Equal(first.Handle, conversation.AcceptedTurn!.Handle);
            Assert.Equal(unknown, conversation.OutcomeUnknown);
            await store.StageAsync(third, default);
            var claim = await store.ClaimAsync(third.Handle, third.Identity, "child", "third", Now, default);
            if (unknown) Assert.Null(claim); else Assert.NotNull(claim);
            Assert.False(await store.AcceptAsync(second.Handle, second.Identity, "second", "late-candidate", Now, default));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Another_workflow_or_specialist_version_cannot_claim_a_known_conversation(bool durable)
    {
        await using var fixture = await Fixture.CreateAsync(durable);
        await fixture.Use(async store =>
        {
            var first = Document("first", 0);
            await store.StageAsync(first, default);
            Assert.NotNull(await store.ClaimAsync(first.Handle, first.Identity, "child", "first", Now, default));
            Assert.True(await store.DispatchAsync(first.Handle, first.Identity, "first", Now, default));
            Assert.True(await store.AcceptAsync(first.Handle, first.Identity, "first", "accepted", Now, default));
            foreach (var identity in new[] { Identity with { WorkflowExecutionId = "foreign" }, Identity with { DefinitionVersionId = "different" }, Identity with { TemplateHash = "different" } })
            {
                var foreign = Document("foreign-" + identity.WorkflowExecutionId + identity.DefinitionVersionId + identity.TemplateHash, 1, identity);
                await store.StageAsync(foreign, default);
                Assert.Null(await store.ClaimAsync(foreign.Handle, foreign.Identity, "child", "foreign", Now, default));
                Assert.Equal(SpecialistTurnStatus.Prepared, (await store.ReadAsync(foreign.Handle, foreign.Identity, default))!.Status);
            }
            Assert.Equal(1, (await store.ReadConversationAsync(ConversationId, Identity, default))!.Revision);
        });
    }

    [Fact]
    public async Task Conversation_write_failure_rolls_back_the_turn_acceptance()
    {
        await using var fixture = await Fixture.CreateAsync(true);
        var first = Document("first", 0);
        await fixture.Use(async store =>
        {
            await store.StageAsync(first, default);
            Assert.NotNull(await store.ClaimAsync(first.Handle, first.Identity, "child", "first", Now, default));
            Assert.True(await store.DispatchAsync(first.Handle, first.Identity, "first", Now, default));
        });
        var cause = new IOException("injected conversation write failure");
        await using (var context = fixture.Context(new RejectConversationWrite(cause)))
        {
            var error = await Assert.ThrowsAsync<SpecialistJournalException>(async () => await new EfSpecialistTurnStore(context, new Access()).AcceptAsync(first.Handle, first.Identity, "first", "candidate", Now, default));
            Assert.Same(cause, error.InnerException);
        }
        await fixture.Use(async store =>
        {
            var turn = (await store.ReadAsync(first.Handle, first.Identity, default))!;
            Assert.Equal(SpecialistTurnStatus.Dispatching, turn.Status);
            Assert.Null(turn.ProtectedResult);
            var conversation = (await store.ReadConversationAsync(ConversationId, Identity, default))!;
            Assert.Equal(0, conversation.Revision);
            Assert.Null(conversation.AcceptedTurn);
            Assert.Equal(first.Handle, conversation.ActiveTurnHandle);
        });
    }

    private static SpecialistTurnDocument Document(string operation, long revision, SpecialistInvocationIdentity? owner = null)
    {
        var identity = (owner ?? Identity) with { ActivityExecutionId = operation, OperationKey = operation };
        return new(Token(operation), SpecialistTurnDocument.CalculateOperationKey(identity), Token("request-" + operation), identity, "protected-request", Now.AddMinutes(5))
            { Conversation = new(ConversationId, revision) };
    }
    private static string Token(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant")); }
    private sealed class RejectConversationWrite(Exception cause) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"elsa_specialist_conversations\"", StringComparison.Ordinal)) throw cause;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Fixture(bool durable, string directory) : IAsyncDisposable
    {
        private readonly InMemorySpecialistTurnState state = new();
        public SpecialistsSqliteDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<SpecialistsSqliteDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(directory, "journal.db") + ";Pooling=False;Default Timeout=30").AddInterceptors(interceptors).Options);
        public async Task<T> Use<T>(Func<ISpecialistTurnStore, Task<T>> action)
        {
            await using var context = Context();
            return await action(durable ? new EfSpecialistTurnStore(context, new Access()) : new InMemorySpecialistTurnStore(new Access(), state));
        }
        public Task Use(Func<ISpecialistTurnStore, Task> action) => Use(async store => { await action(store); return true; });
        public static async Task<Fixture> CreateAsync(bool durable)
        {
            var directory = Path.Combine(Path.GetTempPath(), "elsa-specialist-conversation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var fixture = new Fixture(durable, directory);
            await using var context = fixture.Context();
            await context.Database.EnsureCreatedAsync();
            return fixture;
        }
        public ValueTask DisposeAsync() { Directory.Delete(directory, true); return ValueTask.CompletedTask; }
    }
}
