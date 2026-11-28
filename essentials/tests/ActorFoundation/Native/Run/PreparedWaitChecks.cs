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

internal static class PreparedWaitChecks
{
    private const string ActorType = "prepared-wait-fixture";
    private const string Node = "prepared-wait-owner";
    private const string Literal = "http:POST:orders/amy";
    private const string Resolved = "http:POST=orders/Amy";
    private const string Alternate = "http:POST=orders/amy";
    private const string Empty = "http:POST:empty";
    private const string Historical = "http:POST=historical/Amy";
    private const string HistoricalLiteral = "http:POST:historical/amy";
    private static readonly string RouteSuffix = HttpRoutes.PreparedWaitSuffix("orders/{order}");
    private static readonly Contract.BoundScope Tenant = ContractScopes.Bind("default");
    private static readonly Contract.BoundScope Other = ContractScopes.Bind("other");
    private static readonly string Source = new('a', 64);
    private static readonly string Zero = new('b', 64);
    private static readonly string Digest = new('c', 64);
    private static readonly PreparedWaitDelivery[] Deliveries = [new(Literal, "literal reply"), new(Resolved, "resolved reply", RouteSuffix), new(Alternate, null, RouteSuffix)];

    internal static async Task<int> RunAsync(bool postgres, bool fenceInBatch)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var actor = new WaitActor();
        var loop = new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(20) };
        var token = deadline.Token;
        if (postgres)
        {
            var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                ?? throw new InvalidOperationException("Supply a dedicated PostgreSQL qualification database.");
            var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
            KernelDb Open() => PostgresPreviewHosting.CreatePreparedWaitQualificationContext(options, ActorType);
            await using (var db = Open()) Require(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "dedicated PostgreSQL is empty");
            var store = new EfStore(Open, postgres: true);
            await store.CreateAsync(1);
            await store.InstallPreviewRootSchemaAsync(token);
            var requests = PostgresPreviewHosting.CreateRequestsWithRootStartProtection(Open, Node, ActorType, Allow, TimeSpan.FromMilliseconds(150));
            var peer = PostgresPreviewHosting.CreateRequestsWithRootStartProtection(Open, Node + "-peer", ActorType, Allow, TimeSpan.FromMilliseconds(150));
            var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, ActorType, requests, cancellationToken: token);
            await using (var hosted = new PostgresPreviewHostedService(Open, Node, actor, Refused, loop, requests: requests)
            {
                StartNode = (fault, ct) => PostgresPreviewHosting.StartAsync(Open, Node, actor, Refused, loop, ct,
                    fault, requests, fenceInBatch, _ => null, new Kernel.WorkflowActor.ScheduleActor())
            })
                await QualifyAsync(hosted, foundation, foundation, PostgresPreviewHosting.BindPreparedRootStartQualification(Open, requests, Tenant),
                    PostgresPreviewHosting.BindPreparedRootStartQualification(Open, peer, Tenant), token);
            await store.InstallPreviewRootSchemaAsync(token);
            await using var proof = Open();
            Require(await proof.Database.SqlQueryRaw<long>("""
                SELECT count(*) AS "Value" FROM "FoundationPreparedWaitReceipts"
                """)
                .SingleAsync(token) == 2, "reinstallation preserves both original pinned source receipts");
            await proof.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "FoundationPreparedWaitReceipts" RENAME TO "MissingPreparedWaitReceipts"
                """, token);
            try { await store.InstallPreviewRootSchemaAsync(token); throw new InvalidOperationException("The missing installed receipt relation was recreated."); }
            catch (StoreFormatException) { Console.WriteLine("ok the initialized profile refuses a missing prepared wait ledger"); }
        }
        else
        {
            var folder = Directory.CreateTempSubdirectory("prepared-wait-fixture");
            try
            {
                var path = Path.Combine(folder.FullName, "prepared-waits.db");
                var foundation = SqliteEmbeddedFoundation.ForPreparedWaitQualification(path, Node, ActorType, Allow);
                await foundation.InstallShippedAsync(token);
                await using (var hosted = foundation.CreateHostedServiceWithAuxiliaryActor(actor, new Kernel.WorkflowActor.ScheduleActor(), Refused,
                    _ => null, loop, fenceInBatch))
                    await QualifyAsync(hosted, await foundation.OpenIngressAsync(token), foundation.CreateReads(),
                        foundation.BindPreparedRootStartQualification(Tenant), foundation.BindPreparedRootStartQualification(Tenant), token);
                try
                {
                    await SqliteEmbeddedFoundation.ForRetainedResponseQualification(path, Node, ActorType, Allow).InstallShippedAsync(token);
                    throw new InvalidOperationException("The previous private provider accepted the newer physical definitions.");
                }
                catch (StoreFormatException) { Console.WriteLine("ok private response-only profile refuses the prepared wait ledger"); }
            }
            finally { folder.Delete(recursive: true); }
        }
        return 0;
    }

    private static async Task QualifyAsync(IHostedService owner, Contract.IIngress ingress, Contract.IActorReads reads,
        IPreparedRootStartHandoff handoff, IPreparedRootStartHandoff peer, CancellationToken token)
    {
        await owner.StartAsync(token);
        try
        {
            var literal = await InstallWaitAsync(Tenant, "literal-first", Literal);
            var resolved = await InstallWaitAsync(Tenant, "resolved-first", Resolved, RouteSuffix);
            var foreign = await InstallWaitAsync(Other, "foreign", Literal);
            var wrongCase = await InstallWaitAsync(Tenant, "wrong-case", Alternate, RouteSuffix);
            var historical = await InstallWaitAsync(Tenant, "historical", Historical);
            var differentTemplate = await InstallWaitAsync(Tenant, "different-template", Historical, HttpRoutes.PreparedWaitSuffix("historical/{customer}"));
            var historicalLiteral = await InstallWaitAsync(Tenant, "historical-literal", HistoricalLiteral);
            foreach (var (source, key) in new[]
            {
                (new string('e', 64), new PreparedWaitDelivery(Historical, "wrong route", HttpRoutes.PreparedWaitSuffix("historical/{order}"))),
                (new string('f', 64), new PreparedWaitDelivery(Historical, null, HttpRoutes.PreparedWaitSuffix("historical/{order}")))
            })
            {
                try
                {
                    _ = await handoff.DeliverWaitsAsync(source, Digest, [new(HistoricalLiteral, "never enqueue"), key], allowNew: true, token);
                    throw new InvalidOperationException("A historical ambiguous wait allowed a partial cohort.");
                }
                catch (NotSupportedException) { Console.WriteLine("ok historical ambiguity refuses the entire exact cohort"); }
            }
            foreach (var ambiguous in new[] { historical, differentTemplate, historicalLiteral })
                Require((await reads.GetAsync(Tenant, ambiguous, token))?.Header.Status == Contract.ActorStatus.Running &&
                    (await reads.InboxAsync(Tenant, ambiguous, cancellationToken: token)).Items.Count == 0,
                    "the refused historical cohort enqueues nothing");
            try
            {
                _ = await handoff.DeliverWaitsAsync(Source, Digest, Deliveries, allowNew: false, token);
                throw new InvalidOperationException("A closed route enqueued its initial waiting cohort.");
            }
            catch (NotSupportedException) { Console.WriteLine("ok current route conflict refuses new external waits"); }
            var zero = await handoff.DeliverWaitsAsync(Zero, Digest, [new(Empty, "no match")], allowNew: true, token);
            Require(zero.Count == 0, "empty wait capture records an explicit committed receipt");
            var lateEmpty = await InstallWaitAsync(Tenant, "late-empty", Empty);

            var receipts = await Task.WhenAll(
                handoff.DeliverWaitsAsync(Source, Digest, Deliveries, allowNew: true, token).AsTask(),
                peer.DeliverWaitsAsync(Source, Digest, Deliveries.Reverse().ToArray(), allowNew: true, token).AsTask());
            Require(receipts[0].Count == 2 && receipts[0] == receipts[1], "concurrent same-source holders agree on the two exact live waits");
            var late = await InstallWaitAsync(Tenant, "late", Literal);
            await Task.Delay(180, token);
            Require(await peer.DeliverWaitsAsync(Zero, Digest, [new(Empty, "no match")], allowNew: false, token) == zero &&
                    await peer.DeliverWaitsAsync(Source, Digest, Deliveries, allowNew: false, token) == receipts[0],
                "later holders read pinned empty or nonempty receipts after the route stops serving");
            try
            {
                _ = await peer.DeliverWaitsAsync(Source, new('d', 64), Deliveries, allowNew: true, token);
                throw new InvalidOperationException("A changed source digest reused the retained receipt.");
            }
            catch (InvalidOperationException error) when (error.Message.StartsWith("The source identity", StringComparison.Ordinal))
            { Console.WriteLine("ok changed exact source input conflicts"); }
            Require((await AwaitAsync(Tenant, literal, Contract.ActorStatus.Finished)).Header.TypeData == "literal reply" &&
                    (await AwaitAsync(Tenant, resolved, Contract.ActorStatus.Finished)).Header.TypeData == "resolved reply",
                "both exact detached event inputs travel through the ordinary actor turns");
            foreach (var (scope, actor) in new[] { (Tenant, late), (Tenant, lateEmpty), (Tenant, wrongCase), (Other, foreign) })
                Require((await reads.GetAsync(scope, actor, token))?.Header.Status == Contract.ActorStatus.Running &&
                    (await reads.InboxAsync(scope, actor, cancellationToken: token)).Items.Count == 0,
                    "late, case-mismatched or foreign waits never enter the replay cohort");
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await owner.StopAsync(stop.Token);
        }

        async Task<Contract.ActorRef> InstallWaitAsync(Contract.BoundScope scope, string operation, string key, string suffix = "")
        {
            var result = await ingress.SendAsync(scope, new Contract.Target.Start(new(ActorType, key + "\n" + suffix)), operation, Contract.Kinds.Start, cancellationToken: token);
            if (result is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } actor })
                throw new InvalidOperationException("Standalone wait fixture root was not admitted.");
            _ = await AwaitAsync(scope, actor, Contract.ActorStatus.Running);
            return actor;
        }

        async Task<Contract.ActorView> AwaitAsync(Contract.BoundScope scope, Contract.ActorRef actor, Contract.ActorStatus expected)
        {
            while (true)
            {
                if (await reads.GetAsync(scope, actor, token) is { } observed && observed.Header.Status == expected) return observed;
                await Task.Delay(20, token);
            }
        }
    }

    private static ValueTask<bool> Allow(PendingRootStart _, CancellationToken token) => ValueTask.FromResult(true);
    private static void Refused(string reason) => throw new InvalidOperationException(reason);
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class WaitActor : Contract.IActorType
    {
        public string Type => ActorType;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var message = turn.Inbox.Single();
            if (message.Kind == Contract.Kinds.Start)
            {
                turn.State = "{}";
                var registration = turn.Header.TypeData.Split('\n', 2);
                turn.Subscribe(registration[0], "wait" + registration[1]);
            }
            else if (message is { Kind: Contract.Kinds.Event, Event.Wait: { } wait, Payload: { } payload } &&
                wait.StartsWith("wait", StringComparison.Ordinal))
            {
                turn.SetHeader(payload, turn.Header.Labels);
                turn.End(Contract.ActorStatus.Finished);
            }
            else throw new InvalidOperationException("The fixture did not receive an exact wait event.");
            return ValueTask.CompletedTask;
        }
    }
}
