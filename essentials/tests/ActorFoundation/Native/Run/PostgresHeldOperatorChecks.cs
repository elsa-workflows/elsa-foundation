using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresHeldOperatorChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootOperatorReplies(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stop = deadline.Token;
        await using (var db = Open())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the fixture uses an empty PostgreSQL database");
        var cold = Store();
        await cold.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admission.InstallAsync(stop);
        var reads = new PostgresRootReads(cold, await admission.OpenJournalCursorAsync(stop), 8);
        var tenant = EmbeddedTenant.Bind("held-operator");
        var requests = new Requests();
        const string receiver = "operator-edge";
        var routes = new Dictionary<string, Message>(StringComparer.Ordinal);
        var transport = new EdgeLink(receiver, reply => IsCommitted(routes[reply.Request], reply.Item is "deleted").GetAwaiter().GetResult());
        var allowDelete = false;
        var ingress = await cold.OpenSelectedRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1),
            (scope, _) => ReferenceEquals(scope, tenant), stop, (scope, _) => allowDelete && ReferenceEquals(scope, tenant),
            (requests, receiver));
        var runtime = new WorkflowRuntime(ingress, reads);
        var registry = new WorkflowRegistry().Add(Wf.Workflow("wait", Wf.Node<PostgresRootTerminateChecks.InvocationWait>("receive")));
        var remove = await Start("remove");
        var keep = await Start("keep");
        var expire = await Start("expire");
        var rollback = await Start("rollback");
        var actorType = new PublicActor([new PostgresRootThenDeleteChecks.SeededReceipt(new NativeExecutionModel(registry),
            reference => reference == keep)]);
        long oldEpoch;
        Task<Contract.Reply> removal;
        Task<Contract.Reply> kept;
        await using (var owner = await Own("original"))
        {
            var ready = (await Drive(owner, remove, InstanceStatus.Running))!;
            await Drive(owner, keep, InstanceStatus.Running);
            await Drive(owner, expire, InstanceStatus.Running);
            var selection = (await reads.GetAsync(tenant, remove, stop))!;
            var keptSelection = (await reads.GetAsync(tenant, keep, stop))!;
            var now = await admission.ReadReplyTimeAsync(stop);
            Check((await Call(selection, "remove", now + 15_000)).Error == "missing" && requests.All.Count == 0,
                "a held ThenDelete still requires both permissions");
            allowDelete = true;
            Check((await Call(selection, "past", now - 1)).Error == "timeout" &&
                (await Call(selection, "early", now + 15_000, early: true)).Error == "invalid operator request" &&
                await admission.ReadDueRootOperatorAsync(tenant.Tenant, remove.Id, stop) is null,
                "an expired request and a before-commit request leave the inbox empty");
            removal = Call(selection, "remove", now + 15_000).AsTask();
            var pending = await ReadPending(remove);
            var registration = ReplyTo.Parse(pending.ReplyTo!).Request;
            routes.Add(registration, pending);
            Check(requests.All.Single().Registration == registration && !removal.IsCompleted &&
                ready.Revision == (await cold.ReadFirstRootAsync(tenant.Tenant, remove.Id, stop))?.Revision,
                "the original tenant-bound reply route commits with the operator input while its caller waits");
            await RejectEarlierDecoder(pending, remove);
            Check((await Call(selection, "remove", now + 15_000)).Error == "repeat" && requests.All.Count == 1 &&
                (await admission.ReadDueRootOperatorAsync(tenant.Tenant, remove.Id, stop)) == pending,
                "a duplicate cannot replace the first route or register another waiter");

            kept = Call(keptSelection, "keep", now + 15_000).AsTask();
            var pendingKeep = await ReadPending(keep);
            routes.Add(ReplyTo.Parse(pendingKeep.ReplyTo!).Request, pendingKeep);
            oldEpoch = owner.Epoch;
        }

        var prepared = 0;
        Task<Contract.Reply> rolledBack;
        await using (var owner = await Own("successor", (turn, _, _) =>
        {
            if (turn.FoundationRootOperator is not { ReplyTo: { } address, Id: "remove" or "keep" }) return;
            if (transport.RepliedTo(ReplyTo.Parse(address).Request) || removal.IsCompleted || kept.IsCompleted)
                throw new InvalidOperationException("The waiting callers received a result before commit.");
            prepared++;
        }))
        {
            Check(owner.Epoch > oldEpoch && !removal.IsCompleted && !kept.IsCompleted,
                "an ownership change leaves both original callers waiting");
            while (transport.Attempts < 2) await Next(owner);
            Check(prepared == 2 && transport.Attempts == 2 && transport.Committed == 2,
                "both outgoing results observe settled operator rows and their final checkpoint on an independent connection");
            var arrival = await admission.ReadReplyTimeAsync(stop);
            var replies = transport.Deliver(requests, arrival);
            Check(replies == 2 && await removal is { Channel: "response", Item: "deleted" } &&
                await kept is { Channel: "response", Item: "terminated and kept" } && requests.All.Count == 0,
                "the original callers receive the distinct committed results once");

            var keptView = (await reads.GetAsync(tenant, keep, stop))!;
            var protectedCall = Call(keptView, "delete-protected", arrival + 15_000, kind: Contract.Kinds.Delete).AsTask();
            var protectedInput = await ReadPending(keep);
            routes.Add(ReplyTo.Parse(protectedInput.ReplyTo!).Request, protectedInput);
            while (transport.Attempts < 3) await Next(owner);
            Check(transport.Committed == 3 && transport.Deliver(requests, await admission.ReadReplyTimeAsync(stop)) == 1 &&
                await protectedCall is { Error: "protected" }, "a protected qualified Delete returns a committed refusal");

            var expires = (await reads.GetAsync(tenant, expire, stop))!;
            var elapsed = Call(expires, "timeout", await admission.ReadReplyTimeAsync(stop) + 900).AsTask();
            var expiredInput = await ReadPending(expire);
            Check(await elapsed is { Error: "timeout" } && requests.All.Count == 0 && transport.Attempts == 3,
                "the waiting edge releases the caller at the deadline before the actor takes the command");
            while (!await IsCommitted(expiredInput)) await Next(owner);
            Check(transport.Attempts == 3 && await reads.GetAsync(tenant, expire, stop) is null,
                "the admitted command can commit after timeout without a late result");

            await Drive(owner, rollback, InstanceStatus.Running);
            var retrySelection = (await reads.GetAsync(tenant, rollback, stop))!;
            rolledBack = Call(retrySelection, "rollback", await admission.ReadReplyTimeAsync(stop) + 15_000).AsTask();
            var retryInput = await ReadPending(rollback);
            routes.Add(ReplyTo.Parse(retryInput.ReplyTo!).Request, retryInput);
            await ForceRollback(owner, rollback, retryInput, rolledBack);
        }
        await using (var owner = await Own("after-rollback"))
        {
            while (transport.Attempts < 4) await Next(owner);
            Check(transport.Committed == 4 && transport.Deliver(requests, await admission.ReadReplyTimeAsync(stop)) == 1 &&
                await rolledBack is { Channel: "response", Item: "deleted" },
                "after rollback a new owner commits and replies to the original caller");
        }
        return 0;

        async Task ForceRollback(Worker owner, Contract.ActorRef actor, Message input, Task<Contract.Reply> result)
        {
            var epoch = owner.Epoch;
            try
            {
                await using var db = Open();
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION fixture_skip_operator_marker() RETURNS trigger LANGUAGE plpgsql AS $fn$
                    BEGIN RETURN NULL; END
                    $fn$;
                    CREATE TRIGGER fixture_skip_operator_marker BEFORE INSERT ON "Journal"
                    FOR EACH ROW WHEN (NEW."Revision" = -1) EXECUTE FUNCTION fixture_skip_operator_marker();
                    """, stop);
                while (owner.Owns) await Next(owner);
                Check(!result.IsCompleted && transport.Attempts == 3 &&
                    (await admission.ReadDueRootOperatorAsync(tenant.Tenant, actor.Id, stop)) == input &&
                    (await reads.GetAsync(tenant, actor, stop)) is { Header.Status: Contract.ActorStatus.Running },
                    "a failed fenced retirement emits no reply and preserves the admitted registration");
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var db = Open();
                await db.Database.ExecuteSqlRawAsync("""
                    DROP TRIGGER IF EXISTS fixture_skip_operator_marker ON "Journal";
                    DROP FUNCTION IF EXISTS fixture_skip_operator_marker();
                    """, cleanup.Token);
                await cold.ReleaseAsync(0, epoch, cleanup.Token);
            }
        }

        ValueTask<Contract.Reply> Call(Contract.ActorView selected, string operation, long due,
            bool early = false, string kind = Contract.Kinds.Terminate) =>
            ingress.RequestAsync(tenant, new Contract.Target.Ref(selected.Header.Ref), operation, kind,
                JsonSerializer.Serialize(new Contract.OperatorCommand(selected.Selection,
                    ThenDelete: kind == Contract.Kinds.Terminate, ExpectedTarget: selected.Header.Ref)),
                null, new(null, due, early), stop);

        async Task<Contract.ActorRef> Start(string id) =>
            (await runtime.StartAsync(tenant, id, NativeExecutionModel.Start("wait"), cancellationToken: stop)).Actor!.Value;

        async Task<Worker> Own(string node, Action<TurnCommit, object?, Message?>? prepared = null)
        {
            var store = Store();
            var roots = (await store.BindWorkerPartitionAsync(0, stop)).Scope;
            var worker = new Worker(node, 0, store, new ActorAdapter(new EfStore.RootHeaders(roots), actorType),
                new WorkerOptions { Partitions = 1, Transport = transport, OnPreparedCheckpoint = prepared });
            var host = new DurableHost([worker]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return (Worker?)await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture owner could not take.");
        }

        async Task Next(Worker worker)
        {
            await worker.WokenAsync(null, stop);
            await worker.RunAsync(stop);
        }

        async Task<EfStore.RootImage?> Drive(Worker worker, Contract.ActorRef actor, InstanceStatus status)
        {
            while (true)
            {
                await Next(worker);
                if (await cold.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop) is { } image && image.Status == status) return image;
            }
        }

        async Task<Message> ReadPending(Contract.ActorRef actor)
        {
            while (true)
            {
                if (await admission.ReadDueRootOperatorAsync(tenant.Tenant, actor.Id, stop) is { } message) return message;
                await Task.Delay(1, stop);
            }
        }

        async Task<bool> IsCommitted(Message input, bool retired = true)
        {
            await using var db = Open();
            return await db.Messages.AsNoTracking().AnyAsync(row => row.Id == input.InboxKey.Id && row.Done && row.ReplyTo == null, stop) &&
                await db.Instances.AsNoTracking().AnyAsync(row => row.Id == input.Target.Id && row.Status == InstanceStatus.Cancelled &&
                    row.Retired == retired && row.Revision == 2, stop);
        }

        async Task RejectEarlierDecoder(Message input, Contract.ActorRef actor)
        {
            await using var db = Open();
            var stored = await db.Messages.AsNoTracking().SingleAsync(row => row.Id == input.InboxKey.Id, stop);
            try
            {
                PostgresRootAdmission.DecodeOperator(stored, actor.Id, true, true, true, true);
                throw new InvalidOperationException("An earlier profile accepted the held operator envelope.");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite)
            {
                Console.WriteLine("ok the earlier operator format refuses the stored held envelope");
            }
        }
    }

    private sealed class EdgeLink(string receiver, Func<Reply, bool> committed) : IReplyTransport
    {
        private readonly Queue<Reply> _replies = new();
        internal int Attempts { get; private set; }
        internal int Committed { get; private set; }
        internal bool RepliedTo(string registration) => _replies.Any(reply => reply.Request == registration);

        public void Send(string node, Reply reply)
        {
            Attempts++;
            if (node == receiver && committed(reply)) Committed++;
            _replies.Enqueue(reply);
        }

        internal int Deliver(Requests requests, long now)
        {
            var count = 0;
            while (_replies.TryDequeue(out var reply))
            {
                if (requests.TryReceive(reply with { Tenant = "wrong-tenant" }, now))
                    throw new InvalidOperationException("The receiver accepted the wrong tenant.");
                if (requests.TryReceive(reply, now)) count++;
                if (requests.TryReceive(reply, now)) throw new InvalidOperationException("The receiver accepted the reply twice.");
            }
            return count;
        }
    }

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
