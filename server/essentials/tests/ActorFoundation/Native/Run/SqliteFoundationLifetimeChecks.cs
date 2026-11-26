using System.Data.Common;
using Elsa.ActorFoundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    private const string FoundationKind = "sqlite-foundation-lifetime";
    private const string WaitingState = "\"waiting\"";
    private const string AdvancedState = "\"advanced\"";

    internal static async Task<int> RunFoundationAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        var token = budget.Token;
        var directory = Directory.CreateTempSubdirectory(FoundationKind);
        try
        {
            var path = Path.Combine(directory.FullName, "foundation.db");
            await ExpectFoundationFailureAsync<ArgumentException>(() => SqliteEmbeddedHosting.StartFoundationAsync(path, "invalid",
                new FoundationLifetimeActor { Type = "" }, _ => { }, cancellationToken: token),
                "host startup refuses an empty configured actor type before opening the file");
            await InstallFoundationAsync(path, token);
            var scope = EmbeddedTenant.Bind(FoundationKind);
            await CheckWideFoundationFenceAsync(Path.Combine(directory.FullName, "wide.db"), scope, fenceInBatch, token);
            await CheckFoundationDefinitionDriftAsync(Path.Combine(directory.FullName, "definitions.db"), scope, fenceInBatch, token);
            var ingress = await SqliteEmbeddedHosting.OpenFoundationAsync(path, FoundationKind, cancellationToken: token);
            var slow = await AdmitFoundationStartAsync(ingress, scope, "slow", token);
            var sibling = await AdmitFoundationStartAsync(ingress, scope, "sibling", token);
            var definitions = new SqliteAdmissionDefinitions();
            var store = new EfStore(() => OpenFoundationDb(path, definitions), postgres: false, fenceInBatch);
            await store.EnableRootJobsAsync(token);
            store.BindSqliteWorkerNode();
            var polled = await store.PollAsync([new PartitionFence(0, 1, 0)], TimeSpan.Zero, 8, token);
            Require(polled.Messages.Count == 2 && polled.Messages.All(message => message.Message.FoundationRootBinding is not null),
                "the owned-set poll discovers both exact creator envelopes");
            Require((await store.LoadAsync(polled.Messages.Select(message => message.Message.Target.Id).ToArray(), token)).Count == 0,
                "admitted creators have no checkpoint before binding");
            var stale = await store.CommitAsync(new(0, 1, 0, Defaults.Lease, false, [],
                polled.Messages.Select(message => message.Message.InboxKey).ToArray(), []), token);
            Require(!stale.Landed, "a stale fence refuses the mark of the admitted creator rows");
            var actor = new FoundationLifetimeActor();
            await using var first = FoundationService(path, "first", actor, fenceInBatch, reason => throw new InvalidOperationException(reason));
            await first.StartAsync(token);
            Task? stopping = null;
            try
            {
                await AwaitEntryAsync(first, Task.WhenAll(actor.SlowEntered.Task, actor.SiblingEntered.Task), token);
                var (owner, epoch) = await FenceAsync(path, token);
                var firstLease = await LeaseAsync(path, "first", token);
                stopping = first.StopAsync(token);
                Require(owner == "first" && !stopping.IsCompleted && await ReadRootAsync(slow) is null,
                    "the shipped host waits for an entered Foundation turn under its original fence");

                actor.ReleaseSibling.TrySetResult();
                await WaitForRootAsync(store, scope.Tenant, sibling, 1, token);
                Require(!stopping.IsCompleted && await ReadRootAsync(slow) is null &&
                    await ReadRootAsync(sibling) is { State: WaitingState }, "a sibling's qualified creator commits during the drain");

                await Task.Delay(Defaults.Lease, token);
                Require(!stopping.IsCompleted && await FenceAsync(path, token) == ("first", epoch) &&
                    await LeaseAsync(path, "first", token) > firstLease, "the shipped host renews its lease while draining an entered turn");

                actor.ReleaseSlow.TrySetResult();
                await stopping.WaitAsync(token);
                Require(await FenceAsync(path, token) == (null, epoch) && first.LastError is null &&
                    await ReadRootAsync(slow) is { Revision: 1, State: WaitingState },
                    "the shipped host commits the creator and releases the same fence cleanly");

                var next = new FoundationLifetimeActor(acceptStart: false);
                await using var replacement = FoundationService(path, "replacement", next, fenceInBatch,
                    reason => throw new InvalidOperationException(reason));
                await replacement.StartAsync(token);
                Require(await SendAsync("advance") is { Outcome: Contract.AdmissionOutcome.Accepted },
                    "the replacement accepts a direct continuation of the committed root");
                await WaitForRootAsync(store, scope.Tenant, slow, 2, token);
                Require(await ReadRootAsync(slow) is { Status: InstanceStatus.Running, State: AdvancedState, TerminalRevision: null },
                    "a direct turn persists its Running checkpoint and leaves its terminal mark empty");

                Require(await SendAsync("late", new(DueAt: long.MaxValue)) is { Outcome: Contract.AdmissionOutcome.Accepted }
                    && await SendAsync("complete") is { Outcome: Contract.AdmissionOutcome.Accepted },
                    "a future input and a ready completion are admitted before the terminal checkpoint");
                await WaitForRootAsync(store, scope.Tenant, slow, 3, token);
                var (nextOwner, nextEpoch) = await FenceAsync(path, token);
                var finished = await ReadRootAsync(slow);
                Require(nextOwner == "replacement" && nextEpoch > epoch && next.Turns == 2 &&
                    finished is { Status: InstanceStatus.Finished, TerminalRevision: 3 },
                    "the replacement resumes the saved checkpoint and commits its terminal mark under a newer fence");

                Require(await SendAsync("complete") is { Outcome: Contract.AdmissionOutcome.Duplicate }, "the terminal root replays its direct receipt");
                await using (var db = OpenFoundationDb(path))
                {
                    var actorId = finished!.Actor.Id;
                    Require(await db.Messages.Where(row => row.Target == actorId && row.Due == long.MaxValue && !row.Done)
                            .ExecuteUpdateAsync(setter => setter.SetProperty(row => row.Due, 0L), token) == 1,
                        "the later input becomes ready only after the terminal checkpoint");
                    var pending = db.Messages.AsNoTracking().Where(row => row.Target == actorId && !row.Done);
                    while (await pending.AnyAsync(token)) await Task.Delay(25, token);
                    var revisions = await db.Journal.AsNoTracking().Where(entry => entry.Instance == actorId)
                        .OrderBy(entry => entry.Position).Select(entry => entry.Revision).ToArrayAsync(token);
                    Require(revisions is [1, 2, 3] && next.Turns == 2,
                        $"terminal settlement leaves two model turns and revisions 1, 2, 3 (turns {next.Turns}, revisions {string.Join(", ", revisions)})");
                    Require(await db.Messages.AsNoTracking().CountAsync(row =>
                            row.Target == actorId && row.Done && row.Payload == null && row.Start == null, token) == 3,
                        "all three direct occurrences are scrubbed");
                }
                await replacement.StopAsync(token);
                Require(await FenceAsync(path, token) == (null, nextEpoch) && replacement.LastError is null,
                    "the replacement releases shipped ownership cleanly");
            }
            finally
            {
                actor.ReleaseSlow.TrySetResult();
                actor.ReleaseSibling.TrySetResult();
                if (stopping is not null) await stopping.WaitAsync(token);
            }

            var deadlinePath = Path.Combine(directory.FullName, "foundation-deadline.db");
            await InstallFoundationAsync(deadlinePath, token);
            await RunPeerAsync("foundation-stuck", deadlinePath, fenceInBatch, token);
            await RunPeerAsync("foundation-recover", deadlinePath, fenceInBatch, token);
            return 0;

            Task<EfStore.RootImage?> ReadRootAsync(Contract.ActorRef actorRef) => store.ReadFirstRootAsync(scope.Tenant, actorRef.Id, token);
            ValueTask<Contract.Admission> SendAsync(string kind, Contract.AddOptions? options = null) =>
                ingress.SendAsync(scope, new Contract.Target.Ref(slow), kind, kind, options: options, cancellationToken: token);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task CheckWideFoundationFenceAsync(string path, Contract.BoundScope scope, bool fenceInBatch, CancellationToken token)
    {
        const int partitions = 1024;
        await InstallFoundationAsync(path, token, partitions);
        var ingress = await SqliteEmbeddedHosting.OpenFoundationAsync(path, FoundationKind, cancellationToken: token);
        await using var db = OpenFoundationDb(path);
        var rootCount = 0;
        do
        {
            await AdmitFoundationStartAsync(ingress, scope, $"wide-{++rootCount}", token);
        } while (await db.Messages.Select(message => message.Partition).Distinct().CountAsync(token) < 2);

        var definitions = new SqliteAdmissionDefinitions();
        var store = new EfStore(() => OpenFoundationDb(path, definitions), postgres: false, fenceInBatch);
        await store.EnableRootJobsAsync(token);
        store.BindSqliteWorkerNode();
        var take = await store.TakeAsync(Enumerable.Range(0, partitions).ToArray(), "wide-worker", Defaults.Lease, token);
        var fences = take.Taken.Select(lease => new PartitionFence(lease.Partition, lease.Epoch, lease.Position)).ToArray();
        Require(fences.Length == partitions, "the shipped SQLite writer acquires all 1,024 partitions");
        var polled = await store.PollAsync(fences, TimeSpan.Zero, 8, token);
        var seen = polled.Messages.Select(message => message.Message.InboxKey).ToArray();
        Require(seen.Length == rootCount, "the 1,024-partition poll discovers creators in distinct partitions");

        var home = polled.Messages[0].Message.Target.Partition;
        var renewed = (await store.TakeAsync(home, "wide-worker", Defaults.Lease, token))!;
        var stale = await store.CommitAsync(CommitBatch.Owned(fences, Defaults.Lease, false, [], seen, []), token);
        Require(!stale.Landed && await db.Messages.AllAsync(message => message.Seen == 0, token),
            "one stale epoch refuses all marks in the 1,024-partition batch");
        var current = fences.Select(fence => fence.Partition == home ? new PartitionFence(home, renewed.Epoch, renewed.Position) : fence)
            .ToArray();
        var marked = await store.CommitAsync(CommitBatch.Owned(current, Defaults.Lease, false, [], seen[1..], [seen[0]]), token);
        var messages = await db.Messages.AsNoTracking().ToListAsync(token);
        var epochs = current.ToDictionary(fence => fence.Partition, fence => fence.Epoch);
        Require(marked.Landed && messages.All(message => message.Seen == epochs[message.Partition]) &&
            messages.Single(message => message.Id == seen[0].Id).Trial == epochs[home],
            "the fresh 1,024-partition batch marks each creator with its own epoch");

        await db.Nodes.Where(node => node.Id == "wide-worker")
            .ExecuteUpdateAsync(update => update.SetProperty(node => node.LeaseUntil, 0L), token);
        var takeover = await store.TakeAsync(Enumerable.Range(0, partitions).ToArray(), "wide-successor", Defaults.Lease, token);
        messages = await db.Messages.AsNoTracking().ToListAsync(token);
        Require(takeover.Taken.Count == partitions && takeover.Taken.All(fence => fence.Epoch == epochs[fence.Partition] + 1)
            && await db.Partitions.AllAsync(partition => partition.Died && partition.Owner == "wide-successor", token),
            "a new node takes all 1,024 expired partitions together");
        Require(messages.All(message => message.Attempts == (message.Id == seen[0].Id ? 1 : 0)),
            "the 1,024-partition takeover counts only a matching previous-epoch trial");
        await store.ReleaseAsync(takeover.Taken, token);
    }

    private static async Task CheckFoundationDefinitionDriftAsync(string path, Contract.BoundScope scope, bool fenceInBatch, CancellationToken token)
    {
        await InstallFoundationAsync(path, token);
        var ingress = await SqliteEmbeddedHosting.OpenFoundationAsync(path, FoundationKind, cancellationToken: token);
        var root = await AdmitFoundationStartAsync(ingress, scope, "drift", token);
        var definitions = new SqliteAdmissionDefinitions();
        var drift = new DefinitionDrift();
        var store = new EfStore(() => OpenFoundationDb(path, definitions, drift), postgres: false, fenceInBatch);
        await store.EnableRootJobsAsync(token);
        store.BindSqliteWorkerNode();
        var lease = (await store.TakeAsync(0, "drift", Defaults.Lease, token))!;
        var polled = await store.PollAsync(0, lease.Epoch, TimeSpan.Zero, 2, token);
        var creator = polled.Messages.Single().Message;
        var start = creator.Start!;
        var header = new FoundationHeaderChange(start.Definition, new Dictionary<string, string>());
        var row = new InstanceRow(creator.Target.Id, start.Model, start.Definition, InstanceStatus.Running, WaitingState, 1, null,
            History: start.History, StateVersion: 1, Creation: 1, StartIntent: CreationProtocol.Fingerprint(start, creator.Kind, creator.Payload))
        {
            NextJobGeneration = 0,
            FoundationDispatchHeader = new(scope.Tenant, root.Id, header)
        };
        var turn = new TurnCommit(row.Id, row, null, [creator.InboxKey], [], [], [], [], "[]") { FoundationHeader = header };
        var batch = new CommitBatch(0, lease.Epoch, lease.Position, Defaults.Lease, false, [turn], [], []);

        await using var db = OpenFoundationDb(path);
        var definitionsToRestore = await db.Database.SqlQueryRaw<string>("""
            SELECT sql AS "Value" FROM main.sqlite_schema
            WHERE name IN ('kernel_project_root_actor_list', 'kernel_require_root_actor_list')
            """).ToListAsync(token);
        Require(definitionsToRestore.Count == 2, "the actor-list projector and its guard are installed");

        drift.Armed = true;
        try
        {
            await store.CommitAsync(batch, token);
            throw new InvalidOperationException("The owned commit accepted missing projection definitions.");
        }
        catch (StoreException error) when (error is StoreFormatException || error.InnerException is StoreFormatException)
        {
            Require(drift.Changed, "the owned commit verifies the manifest again after preparation and before the fence");
        }

        Require(!await db.Instances.AnyAsync(token) && !await db.Journal.AnyAsync(token) &&
            await db.Messages.AnyAsync(message => message.Id == creator.InboxKey.Id && !message.Done, token),
            "schema drift rolls back the checkpoint and leaves its creator pending");
        await using (var update = await db.Database.BeginTransactionAsync(token))
        {
            foreach (var definition in definitionsToRestore)
                await db.Database.ExecuteSqlRawAsync(definition, token);
            await update.CommitAsync(token);
        }
        Require((await store.CommitAsync(batch, token)).Landed && await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "FoundationActorList" WHERE "Tenant" = {scope.Tenant}
                AND "Reference" = {root.Id}) AS "Value"
            """).SingleAsync(token), "restoring the definitions lets the same fenced checkpoint and projection commit together");
        await store.ReleaseAsync(0, lease.Epoch, token);
    }

    private sealed class DefinitionDrift : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Changed { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (!Armed) return result;
            Armed = false;
            await using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TRIGGER kernel_project_root_actor_list; DROP TRIGGER kernel_require_root_actor_list;";
            await drop.ExecuteNonQueryAsync(cancellationToken);
            Changed = true;
            return result;
        }
    }

    private static async Task<int> RunFoundationPeerAsync(string path, bool fenceInBatch, bool recovering, CancellationToken token)
    {
        var scope = EmbeddedTenant.Bind(FoundationKind);
        var ingress = await SqliteEmbeddedHosting.OpenFoundationAsync(path, FoundationKind, cancellationToken: token);
        var admission = await ingress.SendAsync(scope, new Contract.Target.Start(new(FoundationKind, "slow")), "slow", Contract.Kinds.Start,
            cancellationToken: token);
        Require(admission.Outcome == (recovering ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted) && admission.Actor is not null,
            "the deadline fixture resolves the admitted Foundation creator");
        var root = admission.Actor!.Value;
        var actor = new FoundationLifetimeActor(blocks: !recovering);
        var policy = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new LifetimeLog();
        await using var service = FoundationService(path, recovering ? "recovery" : "deadline", actor, fenceInBatch,
            reason => policy.TrySetResult(reason), log);
        var before = await FenceAsync(path, token);
        var definitions = new SqliteAdmissionDefinitions();
        var store = new EfStore(() => OpenFoundationDb(path, definitions), postgres: false, fenceInBatch);
        Require(await store.ReadFirstRootAsync(scope.Tenant, root.Id, token) is null,
            "the creator is still pending before the deadline or takeover attempt");
        await service.StartAsync(token);
        await AwaitEntryAsync(service, actor.SlowEntered.Task, token);
        if (recovering)
        {
            actor.ReleaseSlow.TrySetResult();
            await WaitForRootAsync(store, scope.Tenant, root, 1, token);
            var after = await FenceAsync(path, token);
            Require(after.Owner == "recovery" && after.Epoch > before.Epoch,
                "a fresh shipped host binds the rolled back creator once under a newer fence");
            await service.StopAsync(token);
            return 0;
        }

        try
        {
            using var deadline = new CancellationTokenSource();
            var stopping = service.StopAsync(deadline.Token);
            deadline.Cancel();
            await RequireCancellationAsync(stopping, deadline.Token, token);
            Require(policy.Task.IsCompletedSuccessfully, "a shipped deadline invokes the process policy for a stuck turn");
            Require(log.Kills == 1, "a shipped deadline logs one warning");
            Require(service.LastError is null, "a shipped deadline records its expected cancellation without a fault");
            Require(await store.ReadFirstRootAsync(scope.Tenant, root.Id, token) is null && (await FenceAsync(path, token)).Owner == "deadline",
                "the deadline leaves its entered creator uncommitted and its fence to expire");
        }
        finally { actor.ReleaseSlow.TrySetResult(); }
        return 0;
    }

    private static SqliteEmbeddedHostedService FoundationService(string path, string node, FoundationLifetimeActor actor, bool fenceInBatch,
        Action<string> endProcess, LifetimeLog? log = null) =>
        SqliteEmbeddedHostedService.ForFoundation(path, node, actor, endProcess,
            Loops with { DisposeDeadline = TimeSpan.FromSeconds(15) }, fenceInBatch, log);

    private static async Task InstallFoundationAsync(string path, CancellationToken token, int partitions = 1)
    {
        await using var db = OpenFoundationDb(path);
        await EfStore.InstallSqliteShippedAsync(db, partitions, token);
    }

    private static KernelDb OpenFoundationDb(string path, SqliteAdmissionDefinitions? definitions = null, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<KernelDb>().UseSqlite(new SqliteConnectionStringBuilder
        {
            DataSource = path, ForeignKeys = true, Pooling = false
        }.ToString());
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return definitions is not null ? KernelDb.ForSqliteRootRuntime(builder.Options, definitions) : KernelDb.ForSqliteRootShipped(builder.Options);
    }

    private static async Task<Contract.ActorRef> AdmitFoundationStartAsync(Contract.IIngress ingress, Contract.BoundScope scope, string id,
        CancellationToken token)
    {
        var start = await ingress.SendAsync(scope, new Contract.Target.Start(new(FoundationKind, id)), id, Contract.Kinds.Start,
            cancellationToken: token);
        Require(start is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the shipped store admits the durable creator");
        return start.Actor!.Value;
    }

    private static async Task ExpectFoundationFailureAsync<T>(Func<Task> action, string witness) where T : Exception
    {
        try { await action(); }
        catch (T)
        {
            Require(true, witness);
            return;
        }
        throw new InvalidOperationException(witness);
    }

    private static async Task AwaitEntryAsync(SqliteEmbeddedHostedService service, Task entered, CancellationToken token)
    {
        while (!entered.IsCompleted)
        {
            if (service.LastError is { } error) throw new InvalidOperationException("The Foundation host faulted before entering a turn.", error);
            await Task.WhenAny(entered, Task.Delay(25, token));
            token.ThrowIfCancellationRequested();
        }
        await entered;
    }

    private static async Task WaitForRootAsync(EfStore store, string tenant, Contract.ActorRef actor, long revision, CancellationToken token)
    {
        while ((await store.ReadFirstRootAsync(tenant, actor.Id, token))?.Revision != revision) await Task.Delay(25, token);
    }

    private sealed class FoundationLifetimeActor(bool acceptStart = true, bool blocks = false) : Contract.IActorType
    {
        public string Type { get; init; } = FoundationKind;
        internal TaskCompletionSource SlowEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SiblingEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSlow { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSibling { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Turns { get; private set; }

        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            Turns++;
            if (turn.StartSettings is not null)
            {
                if (!acceptStart) throw new InvalidOperationException("The replacement received an extra creator.");
                var sibling = turn.Header.TypeData == "sibling";
                (sibling ? SiblingEntered : SlowEntered).TrySetResult();
                var release = (sibling ? ReleaseSibling : ReleaseSlow).Task;
                if (blocks) release.GetAwaiter().GetResult();
                else await release.WaitAsync(token);
                turn.State = WaitingState;
                return;
            }

            if (turn.State == WaitingState && turn.Inbox is [{ Kind: "advance" }])
            {
                turn.State = AdvancedState;
                turn.Record(new { Step = "advanced" });
                return;
            }
            if (turn.State != AdvancedState || turn.Inbox is not [{ Kind: "complete" }])
                throw new InvalidOperationException("The continuation needs the previous Foundation checkpoint.");
            turn.End(Contract.ActorStatus.Finished);
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => throw new NotSupportedException();
    }
}
