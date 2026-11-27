using System.Text;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class RetainedRootResponseChecks
{
    private const string ActorType = "retained-response-fixture";
    private const string Node = "retained-response-owner";
    private static readonly string ResponseKey = new('a', 64);
    private const string First = """{"status":201,"body":"first reply"}""";
    private const string Second = """{"status":202,"body":"later reply"}""";
    private static readonly Contract.BoundScope Scope = ContractScopes.Bind("default");

    internal static async Task<int> RunAsync(bool postgres, bool fenceInBatch)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = deadline.Token;
        var actor = new ResponseActor();
        var loop = new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(20) };
        if (postgres)
        {
            var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                ?? throw new InvalidOperationException("Supply a dedicated PostgreSQL qualification database.");
            var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
            KernelDb Open() => PostgresPreviewHosting.CreateRetainedResponseQualificationContext(options, ActorType);
            await using (var db = Open())
                Require(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the dedicated PostgreSQL database is empty");
            var store = new EfStore(Open, postgres: true);
            await store.CreateAsync(1);
            await store.InstallPreviewRootSchemaAsync(token);

            var requests = PostgresPreviewHosting.CreateRequestsWithRootStartProtection(Open, Node, ActorType, Allow);
            var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, ActorType, requests, cancellationToken: token);
            var hosted = new PostgresPreviewHostedService(Open, Node, actor, Refused, loop, requests: requests)
            {
                StartNode = (fault, ct) => PostgresPreviewHosting.StartAsync(Open, Node, actor, Refused, loop, ct,
                    fault, requests, fenceInBatch, _ => TimeSpan.Zero, new Kernel.WorkflowActor.ScheduleActor())
            };
            await using (hosted)
                await CheckAsync(hosted, foundation, foundation, foundation, foundation.AdmitRetentionDeleteAsync,
                    PostgresPreviewHosting.BindPreparedRootStartQualification(Open, requests, Scope),
                    PostgresPreviewHosting.BindPreparedRootStartQualification(Open, requests, ContractScopes.Bind("other")),
                    () =>
                    {
                        var peer = PostgresPreviewHosting.CreateRequestsWithRootStartProtection(Open, "retained-response-peer", ActorType, Allow);
                        return PostgresPreviewHosting.BindPreparedRootStartQualification(Open, peer, Scope);
                    }, token);
            KernelDb OpenShipped() => PostgresPreviewHosting.CreateShippedContext(options, ActorType, protectsStarts: true);
            var shippedRequests = PostgresPreviewHosting.CreateRequestsWithRootStartProtection(OpenShipped, Node, ActorType, Allow);
            await RequireStockRefusalAsync(async () =>
                _ = await PostgresPreviewHosting.OpenFoundationAsync(OpenShipped, ActorType, shippedRequests, cancellationToken: token));
            await CheckInstalledPostgresAuthorityAsync(Open, store, token);
        }
        else
        {
            var folder = Directory.CreateTempSubdirectory(ActorType);
            try
            {
                var path = Path.Combine(folder.FullName, "response.db");
                var foundation = SqliteEmbeddedFoundation.ForRetainedResponseQualification(path, Node, ActorType, Allow);
                await foundation.InstallShippedAsync(token);
                var hosted = foundation.CreateHostedServiceWithAuxiliaryActor(actor, new Kernel.WorkflowActor.ScheduleActor(), Refused,
                    _ => TimeSpan.Zero, loop, fenceInBatch);
                await using (hosted)
                    await CheckAsync(hosted, await foundation.OpenIngressAsync(token), foundation.CreateReads(), foundation, foundation.AdmitRetentionDeleteAsync,
                        foundation.BindPreparedRootStartQualification(Scope),
                        foundation.BindPreparedRootStartQualification(ContractScopes.Bind("other")),
                        () => foundation.BindPreparedRootStartQualification(Scope), token);
                await RequireStockRefusalAsync(() => new SqliteEmbeddedFoundation(path, Node, ActorType).InstallShippedAsync(token));
            }
            finally { folder.Delete(recursive: true); }
        }
        return 0;
    }

    private static async Task CheckInstalledPostgresAuthorityAsync(Func<KernelDb> open, EfStore store, CancellationToken token)
    {
        await store.InstallPreviewRootSchemaAsync(token);
        await using var db = open();
        var original = await db.Database.SqlQueryRaw<long>("""
            SELECT count(*) AS "Value" FROM "FoundationRootResponses"
            """).SingleAsync(token);
        Require(original > 0, "normal reinstallation preserves the first-response ledger");

        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "FoundationRootResponses" ALTER COLUMN "ResponseKey" DROP NOT NULL
            """, token);
        await RequirePostgresAuthorityRefusalAsync(store, "the initialized profile rejects a drifted response column", token);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "FoundationRootResponses" ALTER COLUMN "ResponseKey" SET NOT NULL
            """, token);
        await store.InstallPreviewRootSchemaAsync(token);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "FoundationRootResponses" RENAME TO "PreviousRootResponseAuthority"
            """, token);
        await RequirePostgresAuthorityRefusalAsync(store, "the initialized profile rejects a missing response authority", token);
        Require(await db.Database.SqlQueryRaw<bool>("""
                SELECT pg_catalog.to_regclass('"FoundationRootResponses"') IS NULL AS "Value"
                """).SingleAsync(token) &&
            await db.Database.SqlQueryRaw<long>("""
                SELECT count(*) AS "Value" FROM "PreviousRootResponseAuthority"
                """).SingleAsync(token) == original,
            "refused installation neither creates an empty ledger nor loses prior responses");
    }

    private static async Task RequirePostgresAuthorityRefusalAsync(EfStore store, string description, CancellationToken token)
    {
        try { await store.InstallPreviewRootSchemaAsync(token); }
        catch (StoreFormatException)
        {
            Console.WriteLine("ok " + description);
            return;
        }
        throw new InvalidOperationException(description);
    }

    private static async Task CheckAsync(IHostedService owner, Contract.IIngress ingress, Contract.IActorReads reads,
        Contract.IActorRetentionPolicies policies, Func<Contract.BoundScope, Contract.ActorRef, string, CancellationToken, ValueTask<Contract.Admission>> delete,
        IPreparedRootStartHandoff handoff, IPreparedRootStartHandoff foreign,
        Func<IPreparedRootStartHandoff> reopen, CancellationToken token)
    {
        var response = await StartAsync("answer", "response");
        var missing = await StartAsync("without", "missing");
        var large = await StartAsync("oversize", "large");
        Require(await handoff.ReadResponseAsync(response.Actor, response.Issuance, ResponseKey, token) is null,
            "a proven birth with an unprocessed creator has no fabricated response");
        await owner.StartAsync(token);
        try
        {
            var first = await WaitForResponseAsync(handoff, response);
            Require(first.Present && Encoding.UTF8.GetString(first.Payload ?? []) == First &&
                (await reads.GetAsync(Scope, response.Actor, token))?.Header.Status == Contract.ActorStatus.Running,
                "the first response is committed while its actor is still running");
            Require(await handoff.ReadResponseAsync(response.Actor, response.Issuance, new string('b', 64), token) is null &&
                await handoff.ReadResponseAsync(response.Actor, missing.Issuance, ResponseKey, token) is null &&
                await foreign.ReadResponseAsync(response.Actor, response.Issuance, ResponseKey, token) is null,
                "the reader refuses a wrong key, wrong signed birth and foreign tenant");
            var absent = await WaitForResponseAsync(handoff, missing);
            Require(!absent.Present && absent.Payload is null, "a terminal turn without an authored response records committed absence");
            var oversized = await WaitForResponseAsync(handoff, large);
            Require(oversized is { Present: true, Payload: null }, "an oversized first response yields a bounded durable marker");
            var passed = await ingress.SendAsync(Scope, new Contract.Target.Ref(response.Actor), "finish-response", "finish", cancellationToken: token);
            Require(passed.Outcome == Contract.AdmissionOutcome.Accepted, "a second response runs in a later turn");
            Contract.ActorView? ended;
            do
            {
                ended = await reads.GetAsync(Scope, response.Actor, token);
                if (ended?.Header.Status == Contract.ActorStatus.Finished) break;
                await Task.Delay(20, token);
            } while (true);
            Require(await policies.TryApplyRetentionPolicyAsync(Scope, 0, new(Finished: TimeSpan.Zero),
                long.MaxValue, cancellationToken: token) is { Outcome: Contract.ActorRetentionPolicyChangeOutcome.Applied },
                "ordinary retention can retire an actor with an undispatched retained response");
            Require(ended?.Selection is { } selection &&
                (await delete(Scope, response.Actor, selection, token)).Outcome == Contract.AdmissionOutcome.Accepted,
                "normal exact retention targets the authored actor");
            while (await reads.GetAsync(Scope, response.Actor, token) is not null)
                await Task.Delay(20, token);
            Require(await handoff.ObserveAsync(response.Actor, response.Issuance, token) == PreparedRootStartDisposition.Accepted,
                "the exact signed birth survives retirement");
        }
        catch (OperationCanceledException error) when (owner is SqliteEmbeddedHostedService { LastError: { } fault })
        {
            throw new InvalidOperationException("The SQLite writer did not commit the expected response.", new AggregateException(error, fault));
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await owner.StopAsync(stop.Token);
        }

        var restored = await reopen().ReadResponseAsync(response.Actor, response.Issuance, ResponseKey, token);
        Require(restored is { Present: true, Payload: { } body } && Encoding.UTF8.GetString(body) == First,
            "a cold reader obtains the same first response after retirement and discards the later response");

        async Task<PendingRootStart> StartAsync(string operation, string mode)
        {
            PendingRootStart? observed = null;
            var result = await handoff.StartAsync(operation, new(ActorType, mode), (offer, _) =>
            {
                observed = offer;
                return ValueTask.FromResult(true);
            }, token);
            return result is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } started } && observed is { } pending && started == pending.Actor
                ? pending : throw new InvalidOperationException("The prepared root has no accepted physical birth.");
        }

        async Task<PreparedRootResponse> WaitForResponseAsync(IPreparedRootStartHandoff selected, PendingRootStart accepted)
        {
            while (true)
            {
                if (await selected.ReadResponseAsync(accepted.Actor, accepted.Issuance, ResponseKey, token) is { } observed)
                    return observed;
                await Task.Delay(20, token);
            }
        }
    }

    private static ValueTask<bool> Allow(PendingRootStart _, CancellationToken token) => ValueTask.FromResult(true);
    private static void Refused(string reason) => throw new InvalidOperationException(reason);
    private static async Task RequireStockRefusalAsync(Func<Task> open)
    {
        try { await open(); }
        catch (StoreFormatException)
        {
            Console.WriteLine("ok the shipped provider refuses the private response store");
            return;
        }
        throw new InvalidOperationException("The shipped profile opened the private response store.");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class ResponseActor : Contract.IActorType
    {
        public string Type => ActorType;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            turn.State = "{}";
            if (turn.Inbox.Single().Kind == Contract.Kinds.Start) turn.SetHeader(turn.Header.TypeData, turn.Header.Labels);
            var response = (Contract.IRetainedRootResponseTurn)turn;
            if (turn.Inbox.Single().Kind == "finish")
            {
                response.RetainFirstResponse(ResponseKey, Second);
                turn.End(Contract.ActorStatus.Finished);
            }
            else if (turn.Header.TypeData == "missing")
            {
                response.RetainFirstResponse(ResponseKey, null);
                turn.End(Contract.ActorStatus.Finished);
            }
            else if (turn.Header.TypeData == "large")
            {
                response.RetainFirstResponse(ResponseKey, new string('x', Kernel.Durable.FoundationMetadataLimits.MessageContentBytes + 1));
                turn.End(Contract.ActorStatus.Finished);
            }
            else response.RetainFirstResponse(ResponseKey, First);
            return ValueTask.CompletedTask;
        }
    }
}
