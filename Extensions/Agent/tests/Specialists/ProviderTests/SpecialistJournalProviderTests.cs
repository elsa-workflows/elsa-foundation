using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
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
            Assert.False(await store.DispatchAsync(handle, identity, new("stale-fence", 1), now, CancellationToken.None));
            Assert.False(await store.AcceptAsync(handle, identity, new(winner.Fence!, winner.AdmissionGeneration), "premature", now, CancellationToken.None));
            Assert.True(await store.DispatchAsync(handle, identity, new(winner.Fence!, winner.AdmissionGeneration), now, CancellationToken.None));
            var reservations = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                await using var scope = first.CreateAsyncScope();
                var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(index))).ToLowerInvariant();
                return await scope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>().ReserveModelRequestAsync(handle, identity, new(winner.Fence!, winner.AdmissionGeneration),
                    new(id, 64, now, new("fixture", "model", "incarnation", 1, "fixture/1", "fixture/1")), now, CancellationToken.None);
            }));
            Assert.Equal(2, reservations.Count(value => value is not null));
            Assert.False(await store.AcceptAsync(handle, identity, new("stale-fence", 1), "wrong-owner", now, CancellationToken.None));
            Assert.False(await store.AcceptAsync(handle, identity, new(winner.Fence!, winner.AdmissionGeneration), "expired", candidate.Deadline, CancellationToken.None));
            Assert.False(await store.AcceptAsync(handle, identity, new(winner.Fence!, winner.AdmissionGeneration), "unobserved-response", now, CancellationToken.None));
            var reserved = (await store.ReadAsync(handle, identity, CancellationToken.None))!.ModelRequests!;
            Assert.True(await store.CompleteModelResponseAsync(handle, identity, new(winner.Fence!, winner.AdmissionGeneration), reserved.Select(value => value.Id).ToArray(),
                new(now, true, 11, 7, 18, "reported-model"), now, CancellationToken.None));
            Assert.True(await store.AcceptAsync(handle, identity, new(winner.Fence!, winner.AdmissionGeneration), "opaque-protected-result-and-session", now, CancellationToken.None));
            await store.FailAsync(handle, identity, new(winner.Fence!, winner.AdmissionGeneration), "late-failure", true, CancellationToken.None);
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
        Assert.True(await recovered.DispatchAsync(admitted.Handle, admitted.Identity, new(admitted.Fence!, admitted.AdmissionGeneration), now, CancellationToken.None));
        Assert.True(await recovered.AcceptAsync(admitted.Handle, admitted.Identity, new(admitted.Fence!, admitted.AdmissionGeneration), "second-result-and-history", now, CancellationToken.None));
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
        Assert.True(await recovered.DispatchAsync(unknown.Handle, unknown.Identity, new("unknown-fence", 1), now, CancellationToken.None));
        await recovered.FailAsync(unknown.Handle, unknown.Identity, new("unknown-fence", 1), "response-lost", true, CancellationToken.None);
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

        // Force renewal to commit after the response/acceptance reads its row but before its CAS.
        // Both writes are metadata; a harmless version overlap must not discard the response.
        var raceIdentity = identity with { OperationKey = "renew-race", ActivityExecutionId = "renew-race" };
        var race = candidate with { Identity = raceIdentity, Key = SpecialistTurnDocument.CalculateOperationKey(raceIdentity), Handle = new string('7', 64), Conversation = new(new string('6', 64), 0) };
        await recovered.StageAsync(race, CancellationToken.None);
        var raceClaim = (await recovered.ClaimAsync(race.Handle, race.Identity, "race-child", "race-fence", now, CancellationToken.None))!;
        var raceToken = new SpecialistTurnLeaseToken(raceClaim.Fence!, raceClaim.AdmissionGeneration);
        Assert.True(await recovered.DispatchAsync(race.Handle, race.Identity, raceToken, now, CancellationToken.None));
        var raceReservation = new SpecialistModelRequestReservation(new string('5', 64), 64, now, new("fixture", "model", "incarnation", 1, "fixture/1", "fixture/1"));
        Assert.NotNull(await recovered.ReserveModelRequestAsync(race.Handle, race.Identity, raceToken, raceReservation, now, CancellationToken.None));
        var overlap = new RenewalBeforeUpdate(async cancellationToken =>
        {
            await using var scope = restarted.CreateAsyncScope();
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>().RenewAsync(race.Handle, race.Identity, raceToken, now.AddSeconds(10), cancellationToken));
        });
        var racingServices = new ServiceCollection();
        racingServices.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
        racingServices.AddDbContext<RaceContext>(options =>
        {
            _ = providerName switch
            {
                "PostgreSql" => options.UseNpgsql(connection),
                "SqlServer" => options.UseSqlServer(connection),
                "MySql" => options.UseMySQL(connection),
                _ => throw new InvalidOperationException()
            };
            options.AddInterceptors(overlap);
        });
        racingServices.AddSpecialistTurnStore<RaceContext>();
        await using (var raceHost = racingServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
        await using (var raceScope = raceHost.CreateAsyncScope())
        {
            var store = raceScope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>();
            overlap.Arm();
            Assert.True(await store.CompleteModelResponseAsync(race.Handle, race.Identity, raceToken, [raceReservation.Id],
                new(now.AddSeconds(10), true, 11, 7, 18, "reported-model"), now.AddSeconds(10), CancellationToken.None));
            overlap.Arm();
            Assert.True(await store.AcceptAsync(race.Handle, race.Identity, raceToken, "accepted-after-renewal-race", now.AddSeconds(10), CancellationToken.None));
            Assert.Equal(2, overlap.Count);
        }
        Assert.Equal(SpecialistTurnStatus.Accepted, (await recovered.ReadAsync(race.Handle, race.Identity, CancellationToken.None))!.Status);

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

    private sealed class RaceContext(DbContextOptions<RaceContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddSpecialistsModel(Database.ProviderName!);
    }

    private sealed class RenewalBeforeUpdate(Func<CancellationToken, Task> renew) : DbCommandInterceptor
    {
        private int armed;
        public int Count { get; private set; }
        public void Arm() => Volatile.Write(ref armed, 1);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("elsa_specialist_turns", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref armed, 0) == 1)
            {
                await renew(cancellationToken);
                Count++;
            }
            return result;
        }
    }
}
