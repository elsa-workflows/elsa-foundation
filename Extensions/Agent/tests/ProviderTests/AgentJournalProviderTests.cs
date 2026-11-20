using Elsa.Agent.Core.Exceptions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore;
using Elsa.Agent.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.ProviderTests;

public sealed class AgentJournalProviderTests
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
        var identity = new AgentInvocationIdentity("tenant", "tenant", "workflow", "attempt", "operation", "artifact", "hash", "version", "template");
        var candidate = new AgentTurnDocument(new string('a', 64), AgentTurnDocument.CalculateOperationKey(identity), new string('b', 64), identity,
            "opaque-protected-request", now.AddMinutes(5)) { MaximumModelCalls = 2, Conversation = new(new string('e', 64), 0), DeliveryProtocol = 1, Deliveries = [new(identity)] };
        await using (var first = Host())
        {
            await using (var scope = first.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
                await context.Database.MigrateAsync();
                var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
                // The stage 2 clean break (#4027) regenerated the module's history as one Initial migration per provider.
                Assert.EndsWith("_Initial", Assert.Single(appliedMigrations));
                var denied = await Assert.ThrowsAsync<AgentJournalException>(() => scope.ServiceProvider.GetRequiredService<IAgentTurnStore>()
                    .StageAsync(candidate, false, CancellationToken.None).AsTask());
                Assert.Equal("agent.journal.admission-disabled", denied.Code);
                await context.Database.OpenConnectionAsync();
                foreach (var table in new[] { "elsa_agent_turns", "elsa_agent_operations" })
                {
                    await using var count = context.Database.GetDbConnection().CreateCommand();
                    count.CommandText = "SELECT COUNT(*) FROM " + table;
                    Assert.Equal(0L, Convert.ToInt64(await count.ExecuteScalarAsync()));
                }
                await context.Database.CloseConnectionAsync();
            }
            var handles = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                await using var scope = first.CreateAsyncScope();
                var alternate = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(index))).ToLowerInvariant();
                return (await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().StageAsync(candidate with { Handle = alternate }, CancellationToken.None)).Handle;
            }));
            var handle = Assert.Single(handles.Distinct());
            candidate = candidate with { Handle = handle };
            var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                await using var scope = first.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().ClaimAsync(handle, identity, "child-" + index, "fence-" + index, now, CancellationToken.None);
            }));
            var winner = Assert.Single(claims, value => value is not null)!;
            await using var ownerScope = first.CreateAsyncScope();
            var store = ownerScope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
            Assert.True((await store.GetCapabilitiesAsync()).IsDurable);
            Assert.False(await store.DispatchAsync(handle, identity, new("stale-fence", 1), now, CancellationToken.None));
            Assert.False(await store.AcceptAsync(handle, identity, new(winner.Fence!, winner.AdmissionGeneration), "premature", now, CancellationToken.None));
            Assert.True(await store.DispatchAsync(handle, identity, new(winner.Fence!, winner.AdmissionGeneration), now, CancellationToken.None));
            var reservations = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                await using var scope = first.CreateAsyncScope();
                var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(index))).ToLowerInvariant();
                return await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().ReserveModelRequestAsync(handle, identity, new(winner.Fence!, winner.AdmissionGeneration),
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
        var recovered = restartScope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
        var accepted = await recovered.ReadAsync(candidate.Handle, identity, CancellationToken.None);
        Assert.NotNull(accepted);
        Assert.Equal(AgentTurnStatus.Accepted, accepted.Status);
        Assert.Equal("opaque-protected-result-and-session", accepted.ProtectedResult);
        Assert.Equal(2, accepted.ModelRequests!.Count);
        Assert.Equal("reported-model", accepted.ResponseObservation!.ObservedModelId);
        Assert.Equal(7, accepted.ResponseObservation.OutputTokens);
        Assert.Equal(candidate.Deadline, accepted.Deadline);
        var replay = await recovered.StageAsync(candidate with { Handle = new string('c', 64), Deadline = now.AddHours(1) }, false, CancellationToken.None);
        Assert.Equal(accepted.ProtectedResult, replay.ProtectedResult);
        Assert.Equal(accepted.ModelRequests, replay.ModelRequests);
        await Assert.ThrowsAsync<AgentJournalException>(async () => await recovered.StageAsync(candidate with { RequestHash = new string('d', 64) }, CancellationToken.None));
        Assert.Null(await recovered.ReadAsync(candidate.Handle, identity with { ActivityExecutionId = "foreign-attempt" }, CancellationToken.None));
        Assert.Null(await recovered.ClaimAsync(candidate.Handle, identity, "replacement", "replacement", now, CancellationToken.None));
        var conversation = await recovered.ReadConversationAsync(candidate.Conversation.ConversationId, identity, CancellationToken.None);
        Assert.Equal(1, conversation!.Revision);
        Assert.Equal(candidate.Handle, conversation.AcceptedTurn!.Handle);
        Assert.Null(await recovered.ReadConversationAsync(conversation.Id, identity with { WorkflowExecutionId = "foreign" }, CancellationToken.None));
        var continuations = Enumerable.Range(0, 8).Select(index =>
        {
            var nextIdentity = identity with { OperationKey = "next-" + index, ActivityExecutionId = "next-" + index };
            return candidate with { Identity = nextIdentity, Deliveries = [new(nextIdentity)], Handle = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("continuation-" + index))).ToLowerInvariant(),
                Key = AgentTurnDocument.CalculateOperationKey(nextIdentity), Conversation = new(conversation.Id, 1) };
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
            Assert.Equal(AgentTurnStatus.Prepared, (await recovered.ReadAsync(stale.Handle, stale.Identity, CancellationToken.None))!.Status);
        }
        Assert.Equal("opaque-protected-result-and-session", (await recovered.StageAsync(candidate, CancellationToken.None)).ProtectedResult);
        Assert.Equal(2, (await recovered.ReadConversationAsync(conversation.Id, identity, CancellationToken.None))!.Revision);
        var unknownIdentity = identity with { OperationKey = "unknown", ActivityExecutionId = "unknown" };
        var unknown = candidate with { Identity = unknownIdentity, Deliveries = [new(unknownIdentity)], Handle = new string('f', 64), Key = AgentTurnDocument.CalculateOperationKey(unknownIdentity), Conversation = new(conversation.Id, 2) };
        await recovered.StageAsync(unknown, CancellationToken.None);
        Assert.NotNull(await recovered.ClaimAsync(unknown.Handle, unknown.Identity, "unknown-child", "unknown-fence", now, CancellationToken.None));
        Assert.True(await recovered.DispatchAsync(unknown.Handle, unknown.Identity, new("unknown-fence", 1), now, CancellationToken.None));
        await recovered.FailAsync(unknown.Handle, unknown.Identity, new("unknown-fence", 1), "response-lost", true, CancellationToken.None);
        await using (var freshHost = Host())
        await using (var freshScope = freshHost.CreateAsyncScope())
        {
            var freshStore = freshScope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
            var blocked = await freshStore.ReadConversationAsync(conversation.Id, identity, CancellationToken.None);
            Assert.True(blocked!.OutcomeUnknown);
            Assert.Equal(2, blocked.Revision);
            var blockedIdentity = identity with { OperationKey = "blocked", ActivityExecutionId = "blocked" };
            var attempt = unknown with { Identity = blockedIdentity, Deliveries = [new(blockedIdentity)], Handle = new string('9', 64), Key = AgentTurnDocument.CalculateOperationKey(blockedIdentity) };
            await freshStore.StageAsync(attempt, CancellationToken.None);
            Assert.Null(await freshStore.ClaimAsync(attempt.Handle, attempt.Identity, "blocked", "blocked", now, CancellationToken.None));
        }

        // Concurrent native delivery registration/replay changes only this old accepted turn,
        // even while the conversation has newer history and an unrelated unknown active operation.
        var beforeDeliveryReplay = (await recovered.ReadConversationAsync(conversation.Id, identity, CancellationToken.None))!;
        var deliveryReplays = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            await using var scope = restarted.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
            var deliveryIdentity = identity with { ActivityExecutionId = "replay-parent-" + index };
            Assert.Null(await store.ReadAsync(candidate.Handle, deliveryIdentity, CancellationToken.None));
            var associated = await store.StageAsync(candidate with { Identity = deliveryIdentity, Deliveries = [new(deliveryIdentity)], Deadline = now.AddDays(1) }, false, CancellationToken.None);
            Assert.Equal(candidate.Handle, associated.Handle);
            return await store.ReplayAcceptedAsync(candidate.Handle, deliveryIdentity, "replay-child-" + index, CancellationToken.None);
        }));
        Assert.All(deliveryReplays, document => { Assert.NotNull(document); Assert.Equal(accepted.ProtectedResult, document.ProtectedResult); Assert.Equal(candidate.Deadline, document.Deadline); });
        Assert.Equal(beforeDeliveryReplay, await recovered.ReadConversationAsync(conversation.Id, identity, CancellationToken.None));
        var applications = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            await using var scope = restarted.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
            var application = new AgentTurnApplication(identity with { ActivityExecutionId = "replay-parent-" + index },
                "replay-child-" + index, "commit:application-" + index, new string('a', 64), now);
            var applied = await store.ApplyAsync(candidate.Handle, application, CancellationToken.None);
            Assert.NotNull(applied);
            Assert.NotNull(await store.ApplyAsync(candidate.Handle, application, CancellationToken.None));
            Assert.Null(await store.ApplyAsync(candidate.Handle, application with { SourceId = "foreign-source" }, CancellationToken.None));
            return applied;
        }));
        Assert.All(applications, document => Assert.Equal(accepted.ProtectedResult, document.ProtectedResult));
        Assert.Equal(8, (await recovered.ReadAsync(candidate.Handle, identity, CancellationToken.None))!.Applications!.Count);
        Assert.Equal(beforeDeliveryReplay, await recovered.ReadConversationAsync(conversation.Id, identity, CancellationToken.None));
        await using (var deliveryHost = Host())
        await using (var deliveryScope = deliveryHost.CreateAsyncScope())
        {
            var store = deliveryScope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
            var deliveryIdentity = identity with { ActivityExecutionId = "replay-parent-7" };
            var read = (await store.ReadAsync(candidate.Handle, deliveryIdentity, CancellationToken.None))!;
            Assert.Equal(9, read.Deliveries!.Count);
            Assert.Equal(8, read.Applications!.Count);
            Assert.Equal("commit:application-7", read.Applications.Single(x => x.Identity == deliveryIdentity).SourceId);
            Assert.Equal("replay-child-7", read.Deliveries.Single(x => x.Identity == deliveryIdentity).ChildInvocationId);
            Assert.Null(await store.ReplayAcceptedAsync(candidate.Handle, deliveryIdentity, "foreign-child", CancellationToken.None));
            Assert.Equal(accepted.ChildInvocationId, read.ChildInvocationId);
        }

        // Force renewal to commit after the response/acceptance reads its row but before its CAS.
        // Both writes are metadata; a harmless version overlap must not discard the response.
        var raceIdentity = identity with { OperationKey = "renew-race", ActivityExecutionId = "renew-race" };
        var race = candidate with { Identity = raceIdentity, Deliveries = [new(raceIdentity)], Key = AgentTurnDocument.CalculateOperationKey(raceIdentity), Handle = new string('7', 64), Conversation = new(new string('6', 64), 0) };
        await recovered.StageAsync(race, CancellationToken.None);
        var raceClaim = (await recovered.ClaimAsync(race.Handle, race.Identity, "race-child", "race-fence", now, CancellationToken.None))!;
        var raceToken = new AgentTurnLeaseToken(raceClaim.Fence!, raceClaim.AdmissionGeneration);
        Assert.True(await recovered.DispatchAsync(race.Handle, race.Identity, raceToken, now, CancellationToken.None));
        var raceReservation = new AgentModelRequestReservation(new string('5', 64), 64, now, new("fixture", "model", "incarnation", 1, "fixture/1", "fixture/1"));
        Assert.NotNull(await recovered.ReserveModelRequestAsync(race.Handle, race.Identity, raceToken, raceReservation, now, CancellationToken.None));
        Func<CancellationToken, Task> mutation = async cancellationToken =>
        {
            await using var scope = restarted.CreateAsyncScope();
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().RenewAsync(race.Handle, race.Identity, raceToken, now.AddSeconds(10), cancellationToken));
        };
        var overlap = new RenewalBeforeUpdate(cancellationToken => mutation(cancellationToken));
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
        racingServices.AddAgentTurnStore<RaceContext>();
        await using (var raceHost = racingServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
        await using (var raceScope = raceHost.CreateAsyncScope())
        {
            var store = raceScope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
            overlap.Arm();
            Assert.True(await store.CompleteModelResponseAsync(race.Handle, race.Identity, raceToken, [raceReservation.Id],
                new(now.AddSeconds(10), true, 11, 7, 18, "reported-model"), now.AddSeconds(10), CancellationToken.None));
            overlap.Arm();
            Assert.True(await store.AcceptAsync(race.Handle, race.Identity, raceToken, "accepted-after-renewal-race", now.AddSeconds(10), CancellationToken.None));
            Assert.Equal(2, overlap.Count);
            var innerAlias = race.Identity with { ActivityExecutionId = "forced-stage-inner" };
            var outerAlias = race.Identity with { ActivityExecutionId = "forced-stage-outer" };
            mutation = async cancellationToken =>
            {
                await using var scope = restarted.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().StageAsync(
                    race with { Identity = innerAlias, Deliveries = [new(innerAlias)] }, cancellationToken);
            };
            // Stage now serializes on the operation gate. Force the competing registration
            // before that gate CAS, while the outer reader still holds only a version snapshot.
            overlap.Arm("elsa_agent_operations");
            var registered = await store.StageAsync(race with { Identity = outerAlias, Deliveries = [new(outerAlias)] }, CancellationToken.None);
            Assert.Equal(3, overlap.Count);
            Assert.Contains(registered.Deliveries!, delivery => delivery.Identity == innerAlias);
            Assert.Contains(registered.Deliveries!, delivery => delivery.Identity == outerAlias);
            Assert.Equal("accepted-after-renewal-race", registered.ProtectedResult);
        }
        Assert.Equal(AgentTurnStatus.Accepted, (await recovered.ReadAsync(race.Handle, race.Identity, CancellationToken.None))!.Status);

        // New operation-gate boundaries: force each competing commit before the outer CAS.
        // These callbacks run before lock acquisition, never while awaiting the same held gate.
        var pre = RaceDocument("cancel-before-stage");
        var preCancel = CancelRequest(pre.Identity);
        var insert = new BeforeOperationInsert(async cancellationToken =>
        {
            await using var scope = restarted.CreateAsyncScope();
            var receipt = await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().CancelAsync(preCancel, cancellationToken);
            Assert.Equal(AgentCancellationOutcome.CancelledBeforeAdmission, receipt.Outcome);
        });
        await using (var host = RaceHost(insert))
        await using (var scope = host.CreateAsyncScope())
        {
            var refusal = await Assert.ThrowsAsync<AgentJournalException>(() => scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().StageAsync(pre, CancellationToken.None).AsTask());
            Assert.Equal("agent.journal.cancelled", refusal.Code);
            Assert.Equal(1, insert.Count);
            Assert.Null(await recovered.ReadAsync(pre.Handle, pre.Identity, CancellationToken.None));
        }

        var preClaim = RaceDocument("cancel-before-claim");
        await recovered.StageAsync(preClaim, CancellationToken.None);
        var claimOverlap = new RenewalBeforeUpdate(async cancellationToken =>
        {
            await using var scope = restarted.CreateAsyncScope();
            var receipt = await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().CancelAsync(CancelRequest(preClaim.Identity), cancellationToken);
            Assert.Equal(AgentCancellationOutcome.Cancelled, receipt.Outcome);
        });
        claimOverlap.Arm("elsa_agent_operations");
        await using (var host = RaceHost(claimOverlap))
        await using (var scope = host.CreateAsyncScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().ClaimAsync(preClaim.Handle, preClaim.Identity, "pre-claim-child", "pre-claim-fence", now, CancellationToken.None));
            Assert.Equal(1, claimOverlap.Count);
            Assert.Equal(AgentTurnStatus.Cancelled, (await recovered.ReadAsync(preClaim.Handle, preClaim.Identity, CancellationToken.None))!.Status);
        }

        var fanout = RaceDocument("two-cancelled-parents");
        await recovered.StageAsync(fanout, CancellationToken.None);
        var parentA = fanout.Identity with { ActivityExecutionId = "cancel-parent-a" };
        var parentB = fanout.Identity with { ActivityExecutionId = "cancel-parent-b" };
        await recovered.StageAsync(fanout with { Identity = parentA, Deliveries = [new(parentA)] }, CancellationToken.None);
        await recovered.StageAsync(fanout with { Identity = parentB, Deliveries = [new(parentB)] }, CancellationToken.None);
        var cancelledB = CancelRequest(parentB) with { SourceId = "cancel-source:parent-b", SourceFingerprint = Hash("cancel-parent-b") };
        var cancelledA = CancelRequest(parentA) with { SourceId = "cancel-source:parent-a", SourceFingerprint = Hash("cancel-parent-a") };
        var independent = new RenewalBeforeUpdate(async cancellationToken =>
        {
            await using var scope = restarted.CreateAsyncScope();
            Assert.Equal(AgentCancellationOutcome.OtherDeliveryUnchanged,
                (await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().CancelAsync(cancelledB, cancellationToken)).Outcome);
        });
        independent.Arm("elsa_agent_operations");
        await using (var host = RaceHost(independent))
        await using (var scope = host.CreateAsyncScope())
        {
            Assert.Equal(AgentCancellationOutcome.OtherDeliveryUnchanged,
                (await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().CancelAsync(cancelledA, CancellationToken.None)).Outcome);
            Assert.Equal(1, independent.Count);
        }
        Assert.Null(await recovered.ClaimAsync(fanout.Handle, parentA, "cancelled-a-child", "cancelled-a-fence", now.AddSeconds(2), CancellationToken.None));
        Assert.Null(await recovered.ClaimAsync(fanout.Handle, parentB, "cancelled-b-child", "cancelled-b-fence", now.AddSeconds(2), CancellationToken.None));
        Assert.NotNull(await recovered.ClaimAsync(fanout.Handle, fanout.Identity, "uncancelled-child", "uncancelled-fence", now.AddSeconds(2), CancellationToken.None));

        foreach (var acceptsFirst in new[] { false, true })
        foreach (var continuity in new[] { false, true })
        {
            var contested = RaceDocument((acceptsFirst ? "accept-before-cancel" : "cancel-before-accept") + (continuity ? "-continuity" : "-fresh"));
            if (!continuity) contested = contested with { Conversation = null };
            await recovered.StageAsync(contested, CancellationToken.None);
            Assert.NotNull(await recovered.ClaimAsync(contested.Handle, contested.Identity, "contested-child", "contested-fence", now, CancellationToken.None));
            var token = new AgentTurnLeaseToken("contested-fence", 1);
            Assert.True(await recovered.DispatchAsync(contested.Handle, contested.Identity, token, now, CancellationToken.None));
            var cancel = CancelRequest(contested.Identity, "contested-child");
            var winner = new RenewalBeforeUpdate(async cancellationToken =>
            {
                await using var scope = restarted.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
                if (acceptsFirst) Assert.True(await store.AcceptAsync(contested.Handle, contested.Identity, token, "accepted-winner", now, cancellationToken));
                else Assert.Equal(AgentCancellationOutcome.Cancelled, (await store.CancelAsync(cancel, cancellationToken)).Outcome);
            });
            winner.Arm();
            await using var host = RaceHost(winner);
            await using var scope = host.CreateAsyncScope();
            var outer = scope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
            if (acceptsFirst) Assert.Equal(AgentCancellationOutcome.AlreadyAccepted, (await outer.CancelAsync(cancel, CancellationToken.None)).Outcome);
            else Assert.False(await outer.AcceptAsync(contested.Handle, contested.Identity, token, "losing-result", now, CancellationToken.None));
            Assert.Equal(1, winner.Count);
            var retained = (await recovered.ReadAsync(contested.Handle, contested.Identity, CancellationToken.None))!;
            Assert.Equal(acceptsFirst ? AgentTurnStatus.Accepted : AgentTurnStatus.Cancelled, retained.Status);
            Assert.Equal(acceptsFirst ? "accepted-winner" : null, retained.ProtectedResult);
            if (continuity)
            {
                var retainedConversation = (await recovered.ReadConversationAsync(contested.Conversation!.ConversationId, contested.Identity, CancellationToken.None))!;
                Assert.Equal(acceptsFirst ? 1 : 0, retainedConversation.Revision);
                Assert.Null(retainedConversation.ActiveTurnHandle);
            }
        }

        var transferred = RaceDocument("old-cancel-after-new-claim");
        await recovered.StageAsync(transferred, CancellationToken.None);
        Assert.NotNull(await recovered.ClaimAsync(transferred.Handle, transferred.Identity, "old-child", "old-fence", now, CancellationToken.None));
        var successorIdentity = transferred.Identity with { ActivityExecutionId = "new-delivery-parent" };
        await recovered.StageAsync(transferred with { Identity = successorIdentity, Deliveries = [new(successorIdentity)] }, CancellationToken.None);
        var transfer = new RenewalBeforeUpdate(async cancellationToken =>
        {
            await using var scope = restarted.CreateAsyncScope();
            var claim = await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().ClaimAsync(transferred.Handle, successorIdentity, "new-delivery-child", "new-delivery-fence", now.AddSeconds(30), cancellationToken);
            Assert.Equal(2, claim!.AdmissionGeneration);
        });
        transfer.Arm("elsa_agent_operations");
        await using (var host = RaceHost(transfer))
        await using (var scope = host.CreateAsyncScope())
        {
            var receipt = await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().CancelAsync(CancelRequest(transferred.Identity, "old-child"), CancellationToken.None);
            Assert.Equal(AgentCancellationOutcome.OtherDeliveryUnchanged, receipt.Outcome);
            Assert.Null(receipt.AdmissionGeneration);
            Assert.Equal(1, transfer.Count);
        }
        Assert.Null(await recovered.ClaimAsync(transferred.Handle, transferred.Identity, "old-child", "late-old-fence", now.AddSeconds(60), CancellationToken.None));
        Assert.True(await recovered.DispatchAsync(transferred.Handle, successorIdentity, new("new-delivery-fence", 2), now.AddSeconds(31), CancellationToken.None));
        Assert.True(await recovered.AcceptAsync(transferred.Handle, successorIdentity, new("new-delivery-fence", 2), "new-delivery-result", now.AddSeconds(31), CancellationToken.None));
        await using (var host = Host())
        await using (var scope = host.CreateAsyncScope())
        {
            var fresh = scope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
            Assert.Equal(AgentCancellationOutcome.CancelledBeforeAdmission, (await fresh.CancelAsync(preCancel, CancellationToken.None)).Outcome);
            Assert.Equal("new-delivery-result", (await fresh.ReadAsync(transferred.Handle, successorIdentity, CancellationToken.None))!.ProtectedResult);
            Assert.Equal(AgentCancellationOutcome.OtherDeliveryUnchanged, (await fresh.CancelAsync(CancelRequest(transferred.Identity, "old-child"), CancellationToken.None)).Outcome);
        }

        foreach (var freshConversation in new[] { true, false })
        {
            var unclaimed = RaceDocument(freshConversation ? "retention-unclaimed-fresh" : "retention-unclaimed-reference");
            unclaimed = unclaimed with { Conversation = new(freshConversation ? unclaimed.Handle : Hash("missing-referenced-conversation"), 0) };
            await recovered.StageAsync(unclaimed, CancellationToken.None);
            Assert.Equal(AgentCancellationOutcome.Cancelled, (await recovered.CancelAsync(CancelRequest(unclaimed.Identity), CancellationToken.None)).Outcome);
            var proof = Assert.Single((await recovered.QueryRetentionAsync(new("tenant", "tenant", PageSize: 128), CancellationToken.None)).Items, item => item.Handle == unclaimed.Handle);
            Assert.Equal(freshConversation, await recovered.PurgeContentAsync(proof, now.AddDays(31), CancellationToken.None));
            Assert.Equal(freshConversation, (await recovered.ReadAsync(unclaimed.Handle, unclaimed.Identity, CancellationToken.None))!.ContentPurgedAt is not null);
        }

        var historyRace = RaceDocument("retention-history-race");
        await recovered.StageAsync(historyRace, CancellationToken.None);
        Assert.NotNull(await recovered.ClaimAsync(historyRace.Handle, historyRace.Identity, "history-child", "history-fence", now, CancellationToken.None));
        Assert.True(await recovered.DispatchAsync(historyRace.Handle, historyRace.Identity, new("history-fence", 1), now, CancellationToken.None));
        Assert.True(await recovered.AcceptAsync(historyRace.Handle, historyRace.Identity, new("history-fence", 1), "history-to-pin", now, CancellationToken.None));
        var futureIdentity = historyRace.Identity with { ActivityExecutionId = "retention-next", OperationKey = "retention-next" };
        var future = historyRace with { Identity = futureIdentity, Deliveries = [new(futureIdentity)], Handle = Hash("retention-next"),
            Key = AgentTurnDocument.CalculateOperationKey(futureIdentity), Conversation = new(historyRace.Conversation!.ConversationId, 1) };
        await recovered.StageAsync(future, CancellationToken.None);
        var idleHistory = Assert.Single((await recovered.QueryRetentionAsync(new("tenant", "tenant", PageSize: 128), CancellationToken.None)).Items, item => item.Handle == historyRace.Handle);
        var ownerRace = new RenewalBeforeUpdate(async token =>
        {
            await using var scope = restarted.CreateAsyncScope();
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().ClaimAsync(future.Handle, future.Identity, "future-child", "future-fence", now, token));
        });
        ownerRace.Arm("elsa_agent_conversations");
        await using (var host = RaceHost(ownerRace))
        await using (var scope = host.CreateAsyncScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().PurgeContentAsync(idleHistory, now.AddDays(31), CancellationToken.None));
            Assert.Equal(1, ownerRace.Count);
        }
        Assert.Equal("history-to-pin", (await recovered.ReadAsync(historyRace.Handle, historyRace.Identity, CancellationToken.None))!.ProtectedResult);
        Assert.Equal(future.Handle, (await recovered.ReadConversationAsync(historyRace.Conversation.ConversationId, historyRace.Identity, CancellationToken.None))!.ActiveTurnHandle);

        var historyPin = Assert.Single((await recovered.QueryRetentionAsync(new("tenant", "tenant", PageSize: 128), CancellationToken.None)).Items,
            item => item.Handle == candidate.Handle);
        Assert.False(await recovered.PurgeContentAsync(historyPin, now.AddDays(31), CancellationToken.None));
        var purgeTarget = transferred;
        var purgeIdentity = transferred.Identity;
        // Content-only purge uses its actual row version. Force a concurrent registered
        // delivery between its read and CAS; it must retain ciphertext until a new proof.
        var retention = Assert.Single((await recovered.QueryRetentionAsync(new("tenant", "tenant", PageSize: 128), CancellationToken.None)).Items,
            item => item.Handle == purgeTarget.Handle);
        var purgeRace = new RenewalBeforeUpdate(async cancellationToken =>
        {
            await using var scope = restarted.CreateAsyncScope();
            var delivery = purgeIdentity with { ActivityExecutionId = "retention-overlap-parent" };
            await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().StageAsync(purgeTarget with { Identity = delivery, Deliveries = [new(delivery)] }, cancellationToken);
        });
        purgeRace.Arm();
        await using (var host = RaceHost(purgeRace))
        await using (var scope = host.CreateAsyncScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().PurgeContentAsync(retention, now.AddDays(31), CancellationToken.None));
            Assert.Equal(1, purgeRace.Count);
        }
        Assert.Equal("new-delivery-result", (await recovered.ReadAsync(purgeTarget.Handle, purgeIdentity, CancellationToken.None))!.ProtectedResult);
        var latestRetention = Assert.Single((await recovered.QueryRetentionAsync(new("tenant", "tenant", PageSize: 128), CancellationToken.None)).Items,
            item => item.Handle == purgeTarget.Handle);
        Assert.True(await recovered.PurgeContentAsync(latestRetention, now.AddDays(31), CancellationToken.None));
        await using (var host = Host())
        await using (var scope = host.CreateAsyncScope())
        {
            var fresh = scope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
            var marker = (await fresh.ReadAsync(purgeTarget.Handle, purgeIdentity, CancellationToken.None))!;
            Assert.Equal(AgentTurnStatus.Accepted, marker.Status);
            Assert.Equal(now.AddDays(31), marker.ContentPurgedAt);
            Assert.Null(marker.ProtectedRequest);
            Assert.Null(marker.ProtectedResult);
            Assert.Equal(3, marker.Deliveries!.Count);
            Assert.Null(marker.Applications);
            Assert.NotNull((await fresh.StageAsync(purgeTarget with { Identity = successorIdentity, Deliveries = [new(successorIdentity)] }, false, CancellationToken.None)).ContentPurgedAt);
            Assert.Null(await fresh.ClaimAsync(purgeTarget.Handle, purgeIdentity, "purged-child", "purged-fence", now.AddDays(31), CancellationToken.None));
            Assert.True((await fresh.ReadConversationAsync(conversation.Id, identity, CancellationToken.None))!.OutcomeUnknown);
            Assert.NotNull((await fresh.ReadAsync(unknown.Handle, unknown.Identity, CancellationToken.None))!.ProtectedRequest);
        }

        AgentTurnDocument RaceDocument(string name)
        {
            var owner = identity with { ActivityExecutionId = name, OperationKey = name };
            return candidate with { Handle = Hash(name), Key = AgentTurnDocument.CalculateOperationKey(owner), Identity = owner,
                Deliveries = [new(owner)], Conversation = new(Hash("conversation-" + name), 0) };
        }
        AgentTurnCancellation CancelRequest(AgentInvocationIdentity owner, string? child = null) =>
            new(owner, child, "cancel-source:" + owner.OperationKey, Hash("cancel-" + owner.OperationKey), now.AddSeconds(1));
        static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        ServiceProvider RaceHost(DbCommandInterceptor interceptor)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
            services.AddDbContext<RaceContext>(options =>
            {
                _ = providerName switch
                {
                    "PostgreSql" => options.UseNpgsql(connection), "SqlServer" => options.UseSqlServer(connection),
                    "MySql" => options.UseMySQL(connection), _ => throw new InvalidOperationException()
                };
                options.AddInterceptors(interceptor);
            });
            services.AddAgentTurnStore<RaceContext>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        async Task<AgentTurnDocument?> ClaimContinuation(AgentTurnDocument next, int index)
        {
            await using var scope = restarted.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAgentTurnStore>().ClaimAsync(next.Handle, next.Identity, "next-child-" + index, "next-fence-" + index, now, CancellationToken.None);
        }

        ServiceProvider Host()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
            services.AddAgentEntityFrameworkCore(new() { Provider = providerName, ConnectionString = connection });
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant"));
    }

    private sealed class RaceContext(DbContextOptions<RaceContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddAgentModel(Database.ProviderName!);
    }

    private sealed class BeforeOperationInsert(Func<CancellationToken, Task> cancel) : DbCommandInterceptor
    {
        private int armed = 1;
        public int Count { get; private set; }
        private async ValueTask BeforeAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("elsa_agent_operations", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref armed, 0) == 1)
            { await cancel(cancellationToken); Count++; }
        }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { await BeforeAsync(command, cancellationToken); return result; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { await BeforeAsync(command, cancellationToken); return result; }
    }

    private sealed class RenewalBeforeUpdate(Func<CancellationToken, Task> renew) : DbCommandInterceptor
    {
        private int armed;
        private string table = "elsa_agent_turns";
        public int Count { get; private set; }
        public void Arm(string targetTable = "elsa_agent_turns") { table = targetTable; Volatile.Write(ref armed, 1); }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains(table, StringComparison.Ordinal) &&
                Interlocked.Exchange(ref armed, 0) == 1)
            {
                await renew(cancellationToken);
                Count++;
            }
            return result;
        }
    }
}
