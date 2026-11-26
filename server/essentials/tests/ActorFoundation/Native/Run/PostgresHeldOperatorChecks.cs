using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        EfStore Store(Func<KernelDb>? open = null) => new(open ?? Open, postgres: true, fenceInBatch: inBatch)
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
        var (databaseTime, observedAt) = await PostgresHttpPreview.SampleClockAsync(admission, delayedSample: true, stop);
        long Clock() => databaseTime + (long)Stopwatch.GetElapsedTime(observedAt).TotalMilliseconds;
        const string receiver = "operator-edge";
        var routes = new Dictionary<string, (Message Input, long Revision)>(StringComparer.Ordinal);
        var transport = new EdgeLink(receiver, reply =>
        {
            var (input, revision) = routes[reply.Request];
            return IsCommitted(input, reply.Item is "deleted" || reply.Error == "missing", revision).GetAwaiter().GetResult();
        });
        var allowDelete = false;
        var ingress = await cold.OpenSelectedRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1),
            (scope, _) => ReferenceEquals(scope, tenant), stop, (scope, _) => allowDelete && ReferenceEquals(scope, tenant),
            (requests, receiver, Clock));
        var runtime = new WorkflowRuntime(ingress, reads);
        var registry = new WorkflowRegistry().Add(Wf.Workflow("wait", Wf.Node<PostgresRootTerminateChecks.InvocationWait>("receive")));
        var remove = await Start("remove");
        var keep = await Start("keep");
        var expire = await Start("expire");
        var rollback = await Start("rollback");
        var plain = await Start("plain");
        var competing = await Start("competing");
        var delayed = await Start("delayed");
        var stalled = await Start("stalled");
        var limited = await Start("limited");
        var crossing = await Start("crossing");
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
            Check((await reads.InboxAsync(tenant, remove, cancellationToken: stop)).Items is
                [{ Id: "remove", Kind: Contract.Kinds.Terminate, State: Contract.DeliveryState.Pending, Sender: null }],
                "held operator inspection reports only the pending operation's public metadata");
            await PostgresRootInboxChecks.RejectRouteAsync(Open, reads, tenant, remove, pending, pending.ReplyTo + "|early", stop);
            var registration = ReplyTo.Parse(pending.ReplyTo!).Request;
            Remember(pending);
            Check(requests.All.Single().Registration == registration && !removal.IsCompleted &&
                ready.Revision == (await cold.ReadFirstRootAsync(tenant.Tenant, remove.Id, stop))?.Revision,
                "the original tenant-bound reply route commits with the operator input while its caller waits");
            await RejectEarlierDecoder(pending, remove);
            Check((await Call(selection, "remove", now + 15_000)).Error == "repeat" && requests.All.Count == 1 &&
                (await admission.ReadDueRootOperatorAsync(tenant.Tenant, remove.Id, stop)) == pending,
                "a duplicate cannot replace the first route or register another waiter");

            kept = Call(keptSelection, "keep", now + 15_000).AsTask();
            var pendingKeep = await ReadPending(keep);
            Remember(pendingKeep);
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
            Remember(protectedInput);
            while (transport.Attempts < 3) await Next(owner);
            Check(transport.Committed == 3 && transport.Deliver(requests, await admission.ReadReplyTimeAsync(stop)) == 1 &&
                await protectedCall is { Error: "protected" }, "a protected qualified Delete returns a committed refusal");

            var expires = (await reads.GetAsync(tenant, expire, stop))!;
            var elapsed = Call(expires, "timeout", await admission.ReadReplyTimeAsync(stop) + 900).AsTask();
            var expiredInput = await ReadPending(expire);
            Check(await elapsed is { Error: "timeout", Status: Contract.ActorStatus.Running } && requests.All.Count == 0 && transport.Attempts == 3,
                "the waiting edge releases the caller at the deadline before the actor takes the command");
            while (!await IsCommitted(expiredInput)) await Next(owner);
            Check(transport.Attempts == 3 && await reads.GetAsync(tenant, expire, stop) is null,
                "the admitted command can commit after timeout without a late result");

            await Drive(owner, rollback, InstanceStatus.Running);
            var retrySelection = (await reads.GetAsync(tenant, rollback, stop))!;
            rolledBack = Call(retrySelection, "rollback", await admission.ReadReplyTimeAsync(stop) + 15_000).AsTask();
            var retryInput = await ReadPending(rollback);
            Remember(retryInput);
            await ForceRollback(owner, rollback, retryInput, rolledBack);
        }
        var retirement = new RetirementRead();
        var retirementOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(retirement).Options;
        await using (var owner = await Own("after-rollback", open: () => KernelDb.ForPostgresRootOperatorReplies(retirementOptions)))
        {
            while (transport.Attempts < 4) await Next(owner);
            Check(transport.Committed == 4 && transport.Deliver(requests, await admission.ReadReplyTimeAsync(stop)) == 1 &&
                await rolledBack is { Channel: "response", Item: "deleted" },
                "after rollback a new owner commits and replies to the original caller");

            await Drive(owner, plain, InstanceStatus.Running);
            var plainSelection = (await reads.GetAsync(tenant, plain, stop))!;
            var termination = Call(plainSelection, "plain", Clock() + 15_000, thenDelete: false).AsTask();
            Remember(await ReadPending(plain));
            await Deliver(owner, 5);
            Check(await termination is { Channel: "response", Item: "terminated" } &&
                await reads.GetAsync(tenant, plain, stop) is { Header.Status: Contract.ActorStatus.Cancelled },
                "plain held Terminate reports the landed Cancelled checkpoint");

            var ended = (await reads.GetAsync(tenant, plain, stop))!;
            var deletion = Call(ended, "delete", Clock() + 15_000, kind: Contract.Kinds.Delete).AsTask();
            Remember(await ReadPending(plain), revision: 3);
            await Deliver(owner, 6);
            Check(await deletion is { Channel: "response", Item: "deleted" } && await reads.GetAsync(tenant, plain, stop) is null,
                "standalone held Delete reports the exact retired checkpoint");

            await Drive(owner, competing, InstanceStatus.Running);
            var sameRoot = (await reads.GetAsync(tenant, competing, stop))!;
            var first = Call(sameRoot, "first", Clock() + 15_000).AsTask();
            var firstInput = await ReadPending(competing);
            Remember(firstInput);
            var second = Call(sameRoot, "second", Clock() + 15_000).AsTask();
            await using (var db = Open())
            {
                var following = await db.Messages.AsNoTracking().SingleOrDefaultAsync(row =>
                    row.Target == firstInput.Target.Id && row.Id != firstInput.InboxKey.Id && !row.Done && row.ReplyTo != null, stop);
                while (following is null)
                {
                    await Task.Delay(1, stop);
                    following = await db.Messages.AsNoTracking().SingleOrDefaultAsync(row =>
                        row.Target == firstInput.Target.Id && row.Id != firstInput.InboxKey.Id && !row.Done && row.ReplyTo != null, stop);
                }
                Remember(PostgresRootAdmission.DecodeOperator(db, following, competing.Id));
                var key = following.Id;
                retirement.BeforeScrub = async () =>
                {
                    await using var read = Open();
                    var replaceable = await read.Database.SqlQuery<string>($"""
                        SELECT "ReplyTo" AS "Value" FROM "Messages" WHERE "Id" = {key} AND "Target" = {firstInput.Target.Id}
                            AND NOT "Done" FOR UPDATE SKIP LOCKED
                        """).ToListAsync(stop);
                    if (replaceable.Count != 0)
                        throw new InvalidOperationException("Another transaction could replace the captured sibling address.");
                };
            }
            await Deliver(owner, 8);
            Check(retirement.BeforeScrub is null && await first is { Item: "deleted" } && await second is { Error: "missing" },
                "retirement returns committed missing to a different held command it settles");

            await Drive(owner, delayed, InstanceStatus.Running);
            var delayedSelection = (await reads.GetAsync(tenant, delayed, stop))!;
            var queue = new AdmissionPause();
            var pausedOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(queue.Opening, queue.Commit).Options;
            var pausedStore = new EfStore(() => KernelDb.ForPostgresRootOperatorReplies(pausedOptions), postgres: true, fenceInBatch: inBatch);
            var pausedIngress = await pausedStore.OpenSelectedRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1),
                (scope, _) => ReferenceEquals(scope, tenant), stop, (scope, _) => ReferenceEquals(scope, tenant), (requests, receiver, Clock));

            var opening = queue.Opening.Arm();
            var blocked = Call(delayedSelection, "blocked", Clock() + 700, edge: pausedIngress).AsTask();
            await opening.Seen.Task.WaitAsync(stop);
            Check(await blocked is { Error: "timeout", Status: null } && requests.All.Count == 0 &&
                await admission.ReadDueRootOperatorAsync(tenant.Tenant, delayed.Id, stop) is null,
                "the deadline cancels the first admission I/O without a durable operator or known admission");

            var longOpening = queue.Opening.Arm();
            var far = Clock() + int.MaxValue + 10_000L;
            var longWait = Call(delayedSelection, "long-wait", far, edge: pausedIngress).AsTask();
            await longOpening.Seen.Task.WaitAsync(stop);
            requests.Expire(far);
            Check(await longWait.WaitAsync(stop) is { Error: "timeout", Status: null } &&
                await longOpening.Cancelled.Task.WaitAsync(stop) && requests.All.Count == 0,
                "the deadline releases the caller and cancels admission even beyond the timer's maximum interval");

            var acknowledgement = queue.Commit.Arm();
            var due = Clock() + 4_000;
            var slow = Call(delayedSelection, "delayed", due, edge: pausedIngress).AsTask();
            await acknowledgement.Seen.Task.WaitAsync(stop);
            try
            {
                Remember(await ReadPending(delayed));
                await Deliver(owner, 9);
                Check(await slow.WaitAsync(stop) is { Channel: "response", Item: "deleted" } &&
                    !acknowledgement.Resume.Task.IsCompleted && Clock() < due && requests.All.Count == 0,
                    "a timely accepted reply releases the caller while admission acknowledgement remains paused");
            }
            finally { acknowledgement.Resume.TrySetResult(); }
            await Drive(owner, stalled, InstanceStatus.Running);
            var stalledSelection = (await reads.GetAsync(tenant, stalled, stop))!;
            var later = queue.Commit.Arm();
            var waiting = Call(stalledSelection, "stalled", Clock() + 900, edge: pausedIngress).AsTask();
            await later.Seen.Task.WaitAsync(stop);
            Message stalledInput;
            try
            {
                stalledInput = await ReadPending(stalled);
                Check(await waiting.WaitAsync(stop) is { Error: "timeout", Status: null } &&
                    !later.Resume.Task.IsCompleted && requests.All.Count == 0,
                    "the caller times out even when committed admission acknowledgement remains paused");
            }
            finally { later.Resume.TrySetResult(); }
            while (!await IsCommitted(stalledInput)) await Next(owner);
            Check(transport.Attempts == 9, "a stalled acknowledgement cannot revive a timed-out registration");

            await Drive(owner, crossing, InstanceStatus.Running);
            var crossingSelection = (await reads.GetAsync(tenant, crossing, stop))!;
            var crossingReply = Call(crossingSelection, "crossing", Clock() + 2_000).AsTask();
            var crossingInput = await ReadPending(crossing);
            var markerWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            retirement.BeforeScrub = () =>
            {
                markerWritten.TrySetResult();
                return finish.Task;
            };
            async Task ApplyCrossing()
            {
                while (!await IsCommitted(crossingInput)) await Next(owner);
            }
            var applying = ApplyCrossing();
            try
            {
                await markerWritten.Task.WaitAsync(stop);
                Check(await crossingReply.WaitAsync(stop) is { Error: "timeout", Status: Contract.ActorStatus.Running } &&
                    !applying.IsCompleted, "the timeout reads the committed Running status while a terminal write is still uncommitted");
            }
            finally { finish.TrySetResult(); }
            await applying.WaitAsync(stop);
            Check(transport.Attempts == 9 && await reads.GetAsync(tenant, crossing, stop) is null,
                "a terminal transaction that lands after the timeout cannot deliver a late result");
        }

        var mismatchedSelection = (await reads.GetAsync(tenant, keep, stop))!;
        var mismatched = Call(mismatchedSelection, "mismatched", Clock() + 15_000, kind: Contract.Kinds.Delete).AsTask();
        var original = await ReadPending(keep);
        Remember(original);
        var alternate = requests.OpenBound("alternate", Clock() + 15_000, tenant.Tenant, null);
        var refusal = new RefusalRead();
        var refusalOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(refusal).Options;
        await using (var owner = await Own("refusal-mismatch", open: () => KernelDb.ForPostgresRootOperatorReplies(refusalOptions)))
        {
            var epoch = owner.Epoch;
            refusal.BeforeRead = () => UpdateAddress(alternate.ReplyAddress(receiver));
            while (owner.Owns) await Next(owner);
            Check(refusal.BeforeRead is null && !mismatched.IsCompleted && !alternate.Done && transport.Attempts == 9 &&
                (await admission.ReadDueRootOperatorAsync(tenant.Tenant, keep.Id, stop))?.ReplyTo == alternate.ReplyAddress(receiver),
                "the refusal checkpoint rejects a changed reply address without settling the receipt or answering either caller");
            await UpdateAddress(original.ReplyTo!);
            await cold.ReleaseAsync(0, epoch, stop);
        }
        requests.Disconnect(alternate);
        await using (var owner = await Own("refusal-retry"))
        {
            await Deliver(owner, 10);
            Check(await mismatched is { Error: "protected" } && requests.All.Count == 0,
                "a successor settles the refusal after the exact admitted address is restored");
        }
        await using (var owner = await Own("reply-capacity"))
        {
            await Drive(owner, limited, InstanceStatus.Running);
            var limitedSelection = (await reads.GetAsync(tenant, limited, stop))!;
            for (var i = 0; i < PostgresRootAdmission.HeldReplyLimit - 1; i++)
            {
                var id = $"reserved-{i}";
                var reservedUntil = Clock() + 15_000;
                var receipt = await admission.AdmitRootOperatorAsync(tenant.Tenant, limited.Id, id, Kinds.Terminate, null, null, stop,
                    limitedSelection.Selection, reply: new(ReplyTo.Of(receiver, id, reservedUntil), reservedUntil));
                if (receipt is not { Duplicate: false }) throw new InvalidOperationException("The held capacity fixture could not fill its first slots.");
            }
            var reservation = new CapacityRead();
            var limitOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(reservation).Options;
            var limitStore = Store(() => KernelDb.ForPostgresRootOperatorReplies(limitOptions));
            var limitEdge = await limitStore.OpenSelectedRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1),
                (scope, _) => ReferenceEquals(scope, tenant), stop, (scope, _) => ReferenceEquals(scope, tenant), (requests, receiver, Clock));
            var slotUnderLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSlot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            reservation.BeforeCount = () =>
            {
                slotUnderLock.TrySetResult();
                return releaseSlot.Task;
            };
            var lastA = Call(limitedSelection, "last-a", Clock() + 4_000, edge: limitEdge).AsTask();
            await slotUnderLock.Task.WaitAsync(stop);
            Task<Contract.Reply> lastB;
            try
            {
                await using var db = Open();
                var available = await db.Database.SqlQuery<string>($"""
                    SELECT "Reference" AS "Value" FROM "FoundationRootBirths"
                    WHERE "Tenant" = {tenant.Tenant} AND "Reference" = {limited.Id} FOR NO KEY UPDATE SKIP LOCKED
                    """).ToListAsync(stop);
                if (available.Count != 0) throw new InvalidOperationException("The held count ran without holding the birth lock.");
                lastB = Call(limitedSelection, "last-b", Clock() + 4_000).AsTask();
            }
            finally { releaseSlot.TrySetResult(); }
            await using (var db = Open())
            {
                var birthLocked = await (await Task.WhenAny(lastA, lastB)).WaitAsync(stop);
                var limitedRoot = (await cold.ReadFirstRootAsync(tenant.Tenant, limited.Id, stop))!.Actor.Id;
                var held = await db.Messages.AsNoTracking().Where(row => row.Target == limitedRoot && !row.Done && row.ReplyTo != null)
                    .ToListAsync(stop);
                var remainingOperation = held.Select(row => PostgresRootAdmission.DecodeOperator(db, row, limited.Id).Id)
                    .SingleOrDefault(id => id is "last-a" or "last-b");
                var contenderDigests = new[] { FoundationRootCreator.OperationDigest("last-a"), FoundationRootCreator.OperationDigest("last-b") };
                var lastReceipts = await db.Database.SqlQuery<string>($"""
                    SELECT "Operation" AS "Value" FROM "FoundationOperatorReceipts"
                    WHERE "Tenant" = {tenant.Tenant} AND "Reference" = {limited.Id} AND "Operation" = ANY({contenderDigests})
                    """).ToListAsync(stop);
                Check(birthLocked.Error == "operator reply capacity" && held.Count == PostgresRootAdmission.HeldReplyLimit &&
                    remainingOperation is not null && lastReceipts.SequenceEqual([FoundationRootCreator.OperationDigest(remainingOperation)]) &&
                    (await lastA).Error == "timeout" && (await lastB).Error == "operator reply capacity",
                    "the final held slot leaves exactly one competing occurrence and its receipt under the birth lock");
            }
            var duplicate = await Call(limitedSelection, "reserved-0", Clock() + 10_000);
            var oneWay = await ingress.SendAsync(tenant, new Contract.Target.Ref(limited), "one-way", Contract.Kinds.Terminate,
                JsonSerializer.Serialize(new Contract.OperatorCommand(limitedSelection.Selection)), cancellationToken: stop);
            Check(duplicate.Error == "repeat" && oneWay.Outcome == Contract.AdmissionOutcome.Accepted,
                "held capacity preserves the original receipt and leaves one-way operator admission available");
        }
        return 0;

        async Task UpdateAddress(string address)
        {
            await using var db = Open();
            if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "ReplyTo" = {address} WHERE "Target" = {original.Target.Id}
                        AND "Id" = {original.InboxKey.Id} AND "InboxScope" = {original.InboxKey.Scope} AND NOT "Done"
                    """, stop) != 1)
                throw new InvalidOperationException("The fixture did not reach the admitted held Delete.");
        }

        void Remember(Message input, long revision = 2) => routes.Add(ReplyTo.Parse(input.ReplyTo!).Request, (input, revision));

        async Task Deliver(Worker owner, int count)
        {
            while (transport.Attempts < count) await Next(owner);
            if (transport.Attempts != count || transport.Committed != count ||
                transport.Deliver(requests, await admission.ReadReplyTimeAsync(stop)) == 0)
                throw new InvalidOperationException("The transport did not deliver the expected committed operator replies.");
        }

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
            bool early = false, string kind = Contract.Kinds.Terminate, bool thenDelete = true, Contract.IIngress? edge = null) =>
            (edge ?? ingress).RequestAsync(tenant, new Contract.Target.Ref(selected.Header.Ref), operation, kind,
                JsonSerializer.Serialize(new Contract.OperatorCommand(selected.Selection,
                    ThenDelete: thenDelete && kind == Contract.Kinds.Terminate, ExpectedTarget: selected.Header.Ref)),
                null, new(null, due, early), stop);

        async Task<Contract.ActorRef> Start(string id) =>
            (await runtime.StartAsync(tenant, id, NativeExecutionModel.Start("wait"), cancellationToken: stop)).Actor!.Value;

        async Task<Worker> Own(string node, Action<TurnCommit, object?, Message?>? prepared = null, Func<KernelDb>? open = null)
        {
            var store = Store(open);
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

        async Task<bool> IsCommitted(Message input, bool retired = true, long revision = 2)
        {
            await using var db = Open();
            return await db.Messages.AsNoTracking().AnyAsync(row => row.Id == input.InboxKey.Id && row.Done && row.ReplyTo == null, stop) &&
                await db.Instances.AsNoTracking().AnyAsync(row => row.Id == input.Target.Id && row.Status == InstanceStatus.Cancelled &&
                    row.Retired == retired && row.Revision == revision, stop);
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

    private sealed class CapacityRead : DbCommandInterceptor
    {
        internal Func<Task>? BeforeCount;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("""SELECT EXISTS (SELECT 1 FROM "Messages" """.TrimEnd(), StringComparison.Ordinal) &&
                Interlocked.Exchange(ref BeforeCount, null) is { } pause)
                await pause();
            return result;
        }
    }

    private sealed class RetirementRead : DbCommandInterceptor
    {
        internal Func<Task>? BeforeScrub;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("""DELETE FROM "Journal" WHERE "Instance" """.TrimEnd(), StringComparison.Ordinal) &&
                Interlocked.Exchange(ref BeforeScrub, null) is { } change)
                await change();
            return result;
        }
    }

    private sealed class RefusalRead : DbCommandInterceptor
    {
        internal Func<Task>? BeforeRead;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("""SELECT i."FoundationTerminalRevision" AS "Value" """.TrimEnd(), StringComparison.Ordinal) &&
                Interlocked.Exchange(ref BeforeRead, null) is { } change)
                await change();
            return result;
        }
    }

    private sealed class AdmissionPause
    {
        internal readonly OpeningPause Opening = new();
        internal readonly CommitPause Commit = new();

        internal sealed class OpeningPause : DbConnectionInterceptor
        {
            internal sealed record Gate
            {
                internal TaskCompletionSource Seen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                internal TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            private Gate? next;
            internal Gate Arm() => next = new();

            public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
            {
                if (Interlocked.Exchange(ref next, null) is { } opening)
                {
                    opening.Seen.TrySetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    finally { opening.Cancelled.TrySetResult(cancellationToken.IsCancellationRequested); }
                }
                return result;
            }
        }

        internal sealed class CommitPause : DbTransactionInterceptor
        {
            internal sealed record Gate
            {
                internal TaskCompletionSource Seen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            private Gate? next;
            internal Gate Arm() => next = new();

            public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
                CancellationToken cancellationToken = default)
            {
                var gate = Interlocked.Exchange(ref next, null);
                if (gate is null) return Task.CompletedTask;
                gate.Seen.TrySetResult();
                return gate.Resume.Task;
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
