using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.ProviderTests;

public sealed class SpecialistJournalProviderTests
{
    [SkippableFact]
    public Task Conditional_ownership_and_accepted_replay_on_postgresql() => ProviderDatabase.RunAsync("PostgreSql", connection => RunAsync("PostgreSql", connection));
    [SkippableFact]
    public Task Conditional_ownership_and_accepted_replay_on_sql_server() => ProviderDatabase.RunAsync("SqlServer", connection => RunAsync("SqlServer", connection));
    [SkippableFact]
    public Task Conditional_ownership_and_accepted_replay_on_mysql() => ProviderDatabase.RunAsync("MySql", connection => RunAsync("MySql", connection));

    private static async Task RunAsync(string providerName, string connection)
    {
        var now = DateTimeOffset.UtcNow;
        var identity = new SpecialistInvocationIdentity("tenant", "tenant", "workflow", "attempt", "operation", "artifact", "hash", "version", "template");
        var candidate = new SpecialistTurnDocument(new string('a', 64), SpecialistTurnDocument.CalculateOperationKey(identity), new string('b', 64), identity,
            "opaque-protected-request", now.AddMinutes(5)) { MaximumModelCalls = 2, Conversation = new(new string('e', 64), 0) };
        await using (var first = Host())
        {
            await using (var scope = first.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<SpecialistsDbContext>();
                await context.Database.MigrateAsync();
                Assert.Single(await context.Database.GetAppliedMigrationsAsync());
            }
            var handles = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                await using var scope = first.CreateAsyncScope();
                var alternate = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(index))).ToLowerInvariant();
                return (await scope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>().StageAsync(candidate with { Handle = alternate }, CancellationToken.None)).Handle;
            }));
            var handle = Assert.Single(handles.Distinct());
            candidate = candidate with { Handle = handle };
            var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                await using var scope = first.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>().ClaimAsync(handle, identity, "child-" + index, "fence-" + index, now, CancellationToken.None);
            }));
            var winner = Assert.Single(claims, value => value is not null)!;
            await using var ownerScope = first.CreateAsyncScope();
            var store = ownerScope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>();
            Assert.True((await store.GetCapabilitiesAsync()).IsDurable);
            Assert.False(await store.DispatchAsync(handle, identity, "stale-fence", now, CancellationToken.None));
            Assert.False(await store.AcceptAsync(handle, identity, winner.Fence!, "premature", now, CancellationToken.None));
            Assert.True(await store.DispatchAsync(handle, identity, winner.Fence!, now, CancellationToken.None));
            var reservations = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                await using var scope = first.CreateAsyncScope();
                var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(index))).ToLowerInvariant();
                return await scope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>().ReserveModelRequestAsync(handle, identity, winner.Fence!,
                    new(id, 64, now, new("fixture", "model", "incarnation", 1, "fixture/1", "fixture/1")), now, CancellationToken.None);
            }));
            Assert.Equal(2, reservations.Count(value => value is not null));
            Assert.False(await store.AcceptAsync(handle, identity, "stale-fence", "wrong-owner", now, CancellationToken.None));
            Assert.False(await store.AcceptAsync(handle, identity, winner.Fence!, "expired", candidate.Deadline, CancellationToken.None));
            Assert.False(await store.AcceptAsync(handle, identity, winner.Fence!, "unobserved-response", now, CancellationToken.None));
            var reserved = (await store.ReadAsync(handle, identity, CancellationToken.None))!.ModelRequests!;
            Assert.True(await store.CompleteModelResponseAsync(handle, identity, winner.Fence!, reserved.Select(value => value.Id).ToArray(),
                new(now, true, 11, 7, 18, "reported-model"), now, CancellationToken.None));
            Assert.True(await store.AcceptAsync(handle, identity, winner.Fence!, "opaque-protected-result-and-session", now, CancellationToken.None));
            await store.FailAsync(handle, identity, winner.Fence!, "late-failure", true, CancellationToken.None);
        }
        await using var restarted = Host();
        await using var restartScope = restarted.CreateAsyncScope();
        var recovered = restartScope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>();
        var accepted = await recovered.ReadAsync(candidate.Handle, identity, CancellationToken.None);
        Assert.NotNull(accepted);
        Assert.Equal(SpecialistTurnStatus.Accepted, accepted.Status);
        Assert.Equal("opaque-protected-result-and-session", accepted.ProtectedResult);
        Assert.Equal(2, accepted.ModelRequests!.Count);
        Assert.Equal("reported-model", accepted.ResponseObservation!.ObservedModelId);
        Assert.Equal(7, accepted.ResponseObservation.OutputTokens);
        Assert.Equal(candidate.Deadline, accepted.Deadline);
        var replay = await recovered.StageAsync(candidate with { Handle = new string('c', 64), Deadline = now.AddHours(1) }, CancellationToken.None);
        Assert.Equal(accepted.ProtectedResult, replay.ProtectedResult);
        Assert.Equal(accepted.ModelRequests, replay.ModelRequests);
        await Assert.ThrowsAsync<SpecialistJournalException>(async () => await recovered.StageAsync(candidate with { RequestHash = new string('d', 64) }, CancellationToken.None));
        Assert.Null(await recovered.ReadAsync(candidate.Handle, identity with { ActivityExecutionId = "foreign-attempt" }, CancellationToken.None));
        Assert.Null(await recovered.ClaimAsync(candidate.Handle, identity, "replacement", "replacement", now, CancellationToken.None));
        var conversation = await recovered.ReadConversationAsync(candidate.Conversation.ConversationId, identity, CancellationToken.None);
        Assert.Equal(1, conversation!.Revision);
        Assert.Equal(candidate.Handle, conversation.AcceptedTurn!.Handle);
        Assert.Null(await recovered.ReadConversationAsync(conversation.Id, identity with { WorkflowExecutionId = "foreign" }, CancellationToken.None));
        var continuations = Enumerable.Range(0, 8).Select(index =>
        {
            var nextIdentity = identity with { OperationKey = "next-" + index, ActivityExecutionId = "next-" + index };
            return candidate with { Identity = nextIdentity, Handle = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("continuation-" + index))).ToLowerInvariant(),
                Key = SpecialistTurnDocument.CalculateOperationKey(nextIdentity), Conversation = new(conversation.Id, 1) };
        }).ToArray();
        foreach (var next in continuations) await recovered.StageAsync(next, CancellationToken.None);
        var conversationClaims = await Task.WhenAll(continuations.Select((next, index) => ClaimContinuation(next, index)));
        var admitted = Assert.Single(conversationClaims, value => value is not null)!;
        Assert.True(await recovered.DispatchAsync(admitted.Handle, admitted.Identity, admitted.Fence!, now, CancellationToken.None));
        Assert.True(await recovered.AcceptAsync(admitted.Handle, admitted.Identity, admitted.Fence!, "second-result-and-history", now, CancellationToken.None));
        Assert.Equal(2, (await recovered.ReadConversationAsync(conversation.Id, identity, CancellationToken.None))!.Revision);
        foreach (var stale in continuations.Where(value => value.Handle != admitted.Handle))
        {
            Assert.Null(await recovered.ClaimAsync(stale.Handle, stale.Identity, "stale-child", "stale-fence", now, CancellationToken.None));
            Assert.Equal(SpecialistTurnStatus.Prepared, (await recovered.ReadAsync(stale.Handle, stale.Identity, CancellationToken.None))!.Status);
        }
        Assert.Equal("opaque-protected-result-and-session", (await recovered.StageAsync(candidate, CancellationToken.None)).ProtectedResult);
        Assert.Equal(2, (await recovered.ReadConversationAsync(conversation.Id, identity, CancellationToken.None))!.Revision);
        var unknownIdentity = identity with { OperationKey = "unknown", ActivityExecutionId = "unknown" };
        var unknown = candidate with { Identity = unknownIdentity, Handle = new string('f', 64), Key = SpecialistTurnDocument.CalculateOperationKey(unknownIdentity), Conversation = new(conversation.Id, 2) };
        await recovered.StageAsync(unknown, CancellationToken.None);
        Assert.NotNull(await recovered.ClaimAsync(unknown.Handle, unknown.Identity, "unknown-child", "unknown-fence", now, CancellationToken.None));
        Assert.True(await recovered.DispatchAsync(unknown.Handle, unknown.Identity, "unknown-fence", now, CancellationToken.None));
        await recovered.FailAsync(unknown.Handle, unknown.Identity, "unknown-fence", "response-lost", true, CancellationToken.None);
        await using (var freshHost = Host())
        await using (var freshScope = freshHost.CreateAsyncScope())
        {
            var freshStore = freshScope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>();
            var blocked = await freshStore.ReadConversationAsync(conversation.Id, identity, CancellationToken.None);
            Assert.True(blocked!.OutcomeUnknown);
            Assert.Equal(2, blocked.Revision);
            var blockedIdentity = identity with { OperationKey = "blocked", ActivityExecutionId = "blocked" };
            var attempt = unknown with { Identity = blockedIdentity, Handle = new string('9', 64), Key = SpecialistTurnDocument.CalculateOperationKey(blockedIdentity) };
            await freshStore.StageAsync(attempt, CancellationToken.None);
            Assert.Null(await freshStore.ClaimAsync(attempt.Handle, attempt.Identity, "blocked", "blocked", now, CancellationToken.None));
        }

        async Task<SpecialistTurnDocument?> ClaimContinuation(SpecialistTurnDocument next, int index)
        {
            await using var scope = restarted.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>().ClaimAsync(next.Handle, next.Identity, "next-child-" + index, "next-fence-" + index, now, CancellationToken.None);
        }

        ServiceProvider Host()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
            services.AddSpecialistsEntityFrameworkCore(new() { Provider = providerName, ConnectionString = connection });
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant"));
    }
}
