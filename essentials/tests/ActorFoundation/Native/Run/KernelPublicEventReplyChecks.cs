using System.Collections.Concurrent;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Runtime.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    private const string ReplyTopic = "reply-topic";
    private const string SharedRecipientTopic = "shared-recipient-topic";
    private const string LocalReplyNode = "event-local";
    private const string RemoteReplyNode = "event-remote";

    private sealed record EventTurn(Contract.ActorRef Actor, object? Ambient, int Inputs, string? Wait);

    private sealed class EventTurns
    {
        internal readonly ConcurrentDictionary<(string Kind, string Id, string Node), EventTurn> Observed = [];
        internal readonly TaskCompletionSource EarlyTurnEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseEarlyTurn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource LocalFanoutEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SecondWaitEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseSecondWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal static async Task<int> RunPublicEventRepliesAsync(string provider, bool inBatch)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = stop.Token;
        var turns = new EventTurns();
        var model = new ReplyEventActor(turns, LocalReplyNode);
        var scope = ContractScopes.Bind("held-event-reply");
        var directory = Directory.CreateTempSubdirectory("held-event-reply");
        try
        {
            var (open, ingress, reads, owner, _, _) = await OpenAsync(model, provider,
                Path.Combine(directory.FullName, "store.db"), inBatch, token, publicStarts: true);
            await using var owned = owner;
            var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(model.Type, "live", "arm")),
                "arm", Contract.Kinds.Start, null, cancellationToken: token);
            var live = started.Actor ?? throw new InvalidOperationException("The live Event waiter was refused.");
            await WaitForReplySubscriptionsAsync(open, scope, ReplyTopic, 1, token);

            var firstObject = new object();
            var secondObject = new object();
            var first = await RequestAsync(ReplyTopic, "reply-before-emit", "reply-first", firstObject);
            Check(first is { Channel: "reply", Error: null } && first.Item?.ToString() == "reply-before-emit" &&
                (await reads.GetAsync(scope, live, token))?.Header.TypeData == "reply-before-emit",
                "explicit Reply before Emit wins after the consuming checkpoint commits");
            var second = await RequestAsync(ReplyTopic, "emit-before-reply", "emit-first", secondObject);
            Check(second is { Channel: "emit", Error: null } && second.Item?.ToString() == "emit-before-reply" &&
                (await reads.GetAsync(scope, live, token))?.Header.TypeData == "emit-before-reply",
                "Emit before explicit Reply wins after the consuming checkpoint commits");
            Check(HasAmbient("event", "reply-before-emit", firstObject) && HasAmbient("event", "emit-before-reply", secondObject),
                "separate held Events give the sole local turn each original Ambient object");

            var earlyObject = new object();
            var early = RequestAsync(ReplyTopic, "early-reply", "early", earlyObject, early: true);
            try
            {
                await turns.EarlyTurnEntered.Task.WaitAsync(token);
                // Hold ordinary database locks while the owner releases the opted-out reply.
                await using var blocker = open();
                if (blocker.Database.IsSqlite())
                {
                    await blocker.Database.OpenConnectionAsync(token);
                    var connection = (SqliteConnection)blocker.Database.GetDbConnection();
                    await using var writer = connection.BeginTransaction(deferred: false);
                    await CheckEarlyReplyAsync();
                    await writer.RollbackAsync(token);
                }
                else
                {
                    await using var transaction = await blocker.Database.BeginTransactionAsync(token);
                    var locked = await blocker.Database.SqlQuery<string>($"""
                        SELECT "Id" AS "Value" FROM "Instances" WHERE "FoundationReference" = {live.Id} FOR UPDATE
                        """).ToListAsync(token);
                    if (locked is not [_]) throw new InvalidOperationException("The early reply fixture could not lock the actor.");
                    await CheckEarlyReplyAsync();
                    await transaction.RollbackAsync(token);
                }
            }
            finally { turns.ReleaseEarlyTurn.TrySetResult(); }
            await UntilReplyAsync(async () => (await reads.GetAsync(scope, live, token))?.Header.TypeData == "early-reply", token);

            const string startTopic = "start-reply-topic";
            var starterAmbient = new object();
            await ingress.SetStartAsync(scope, startTopic, "spawn", new(model.Type, "starter", "starter"), token);
            var spawned = await RequestAsync(startTopic, "spawned-event", "end", starterAmbient);
            var actor = turns.Observed[("event", "spawned-event", LocalReplyNode)].Actor;
            Check(spawned is { Channel: "reply", Error: null } && spawned.Item?.ToString() == "spawned-event" &&
                HasAmbient("start", "starter", starterAmbient) && HasAmbient("event", "spawned-event", null) &&
                (await reads.GetAsync(scope, actor, token))?.Header.Status == Contract.ActorStatus.Finished,
                "a silent creator gets Ambient once and its companion Event's explicit Reply wins over End");

            const string creatorTopic = "creator-reply-topic";
            var creatorAmbient = new object();
            await ingress.SetStartAsync(scope, creatorTopic, "spawn", new(model.Type, "creator", "creator"), token);
            var creatorReply = await RequestAsync(creatorTopic, "creators-event", "quiet", creatorAmbient);
            await UntilReplyAsync(() => Task.FromResult(turns.Observed.ContainsKey(("event", "creators-event", LocalReplyNode))), token);
            Check(creatorReply is { Channel: "creator", Error: null } && HasAmbient("start", "creator", creatorAmbient) &&
                HasAmbient("event", "creators-event", null), "a captured Event's creator can answer the admitted request itself");

            var armedShared = await ingress.SendAsync(scope, new Contract.Target.Ref(live), "arm-shared", "arm-shared", null,
                cancellationToken: token);
            Check(armedShared.Outcome == Contract.AdmissionOutcome.Accepted, "the same exact actor arms two named waits");
            await WaitForReplySubscriptionsAsync(open, scope, SharedRecipientTopic, 2, token);
            var sharedAmbient = new object();
            var shared = RequestAsync(SharedRecipientTopic, "shared-recipient", "shared", sharedAmbient);
            string? winningWait;
            try
            {
                await turns.SecondWaitEntered.Task.WaitAsync(token);
                var firstWait = turns.Observed[("first-wait", "shared-recipient", LocalReplyNode)];
                var secondWait = turns.Observed[("second-wait", "shared-recipient", LocalReplyNode)];
                Check(!shared.IsCompleted && firstWait.Actor == live && secondWait.Actor == live &&
                    firstWait.Wait is "left" or "right" && secondWait.Wait is "left" or "right" && firstWait.Wait != secondWait.Wait &&
                    firstWait.Inputs == 1 && secondWait.Inputs == 1 &&
                    ReferenceEquals(firstWait.Ambient, sharedAmbient) && secondWait.Ambient is null,
                    "two waits on the same actor consume Ambient only on its first turn while the request is still open");
                winningWait = secondWait.Wait;
            }
            finally { turns.ReleaseSecondWait.TrySetResult(); }
            var sharedReply = await shared;
            Check(sharedReply is { Channel: "shared", Error: null } && sharedReply.Item?.ToString() == winningWait,
                "the later wait can still answer the held Event");

            var absent = await RequestAsync("no-waiter", "unmatched-id", null, new object());
            Check(absent is { Channel: null, Error: "no waiter" }, "Ambient leaves a no-waiter request without a publication");
            var refusedDirect = false;
            try
            {
                await ingress.RequestAsync(scope, new Contract.Target.Ref(live), "ordinary", "ordinary", null, null,
                    new(new object(), await ReplyClockAsync(open, token) + 4_000), token);
            }
            catch (NotSupportedException) { refusedDirect = true; }
            Check(refusedDirect, "ordinary exact-ref requests retain their previous Ambient contract");
            return 0;

            async Task CheckEarlyReplyAsync()
            {
                turns.ReleaseEarlyTurn.TrySetResult();
                var reply = await early.WaitAsync(TimeSpan.FromSeconds(5), token);
                Check(reply is { Channel: "reply", Error: null } && HasAmbient("event", "early-reply", earlyObject) &&
                    (await reads.GetAsync(scope, live, token))?.Header.TypeData == "emit-before-reply",
                    "opted-out explicit Reply reaches the caller while the consuming commit is blocked");
            }

            bool HasAmbient(string kind, string id, object? expected) =>
                turns.Observed.TryGetValue((kind, id, LocalReplyNode), out var seen) &&
                seen.Inputs == 1 && ReferenceEquals(seen.Ambient, expected);

            async Task<Contract.Reply> RequestAsync(string key, string id, string? payload, object ambient, bool early = false) =>
                await ingress.RequestAsync(scope, new Contract.Target.Event(key), id, "ignored", payload, null,
                    new(ambient, await ReplyClockAsync(open, token) + 12_000, ReleaseBeforeCommit: early), token);
        }
        finally { directory.Delete(recursive: true); }
    }

    internal static async Task<int> RunRemotePublicEventReplyAsync(bool inBatch, bool protectedObserver = false)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = stop.Token;
        var scope = ContractScopes.Bind("held-event-remote");
        var turns = new EventTurns();
        var local = new ReplyEventActor(turns, LocalReplyNode);
        var remote = new ReplyEventActor(turns, RemoteReplyNode);
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
            throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.")).Options;
        KernelDb Open() => protectedObserver
            ? PostgresPreviewHosting.CreatePublicEventHostQualificationContext(options, local.Type, protectsStarts: true)
            : KernelDb.ForPostgresRootPublicEventStartsQualification(options);
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the remote Event witness uses a dedicated PostgreSQL store");
        var store = new EfStore(Open, postgres: true);
        await store.CreateAsync(2);
        await store.InstallPreviewRootSchemaAsync(token);
        var loops = new LoopOptions { BalanceEvery = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(25) };
        var localEdge = CreateEdge(LocalReplyNode, local.Type);
        var remoteEdge = CreateEdge(RemoteReplyNode, remote.Type);
        var ingress = await PostgresPreviewHosting.OpenFoundationAsync(Open, local.Type, localEdge, cancellationToken: token);
        await using var localOwner = await StartLocalOwnerAsync();
        await using var remoteOwner = await PostgresPreviewHosting.StartAsync(Open, RemoteReplyNode, remote, Fatal, loops, token,
            Console.Error.WriteLine, remoteEdge, fenceInBatch: inBatch);
        await UntilReplyAsync(async () => await db.Partitions.AsNoTracking().CountAsync(p => p.Owner == LocalReplyNode, token) == 1 &&
            await db.Partitions.AsNoTracking().CountAsync(p => p.Owner == RemoteReplyNode, token) == 1, token);

        var candidates = new Dictionary<string, Contract.ActorRef>(StringComparer.Ordinal);
        for (var index = 0; index < 32 && candidates.Count != 2; index++)
        {
            var name = "candidate-" + index;
            var result = await ingress.SendAsync(scope, new Contract.Target.Start(new(local.Type, "candidate", name)),
                name, Contract.Kinds.Start, null, cancellationToken: token);
            var actor = result.Actor ?? throw new InvalidOperationException($"Candidate {name} was refused.");
            await UntilReplyAsync(() => Task.FromResult(turns.Observed.ContainsKey(("start", name, LocalReplyNode)) ||
                turns.Observed.ContainsKey(("start", name, RemoteReplyNode))), token);
            if (turns.Observed.ContainsKey(("start", name, LocalReplyNode))) candidates.TryAdd(LocalReplyNode, actor);
            if (turns.Observed.ContainsKey(("start", name, RemoteReplyNode))) candidates.TryAdd(RemoteReplyNode, actor);
        }
        Check(candidates.Count == 2, "both PostgreSQL owners run an admitted candidate");
        foreach (var actor in candidates.Values)
        {
            var armed = await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "arm", "arm", null, cancellationToken: token);
            Check(armed.Outcome == Contract.AdmissionOutcome.Accepted, "the selected owner's actor is armed");
        }
        await WaitForReplySubscriptionsAsync(Open, scope, ReplyTopic, 2, token);
        var ambient = new object();
        var reply = await ingress.RequestAsync(scope, new Contract.Target.Event(ReplyTopic), "remote-wins", "ignored", "remote",
            null, new(ambient, await localEdge.ReadClockAsync(token) + 12_000), token);
        var localTurn = turns.Observed[("event", "remote-wins", LocalReplyNode)];
        var remoteTurn = turns.Observed[("event", "remote-wins", RemoteReplyNode)];
        Check(reply is { Channel: "remote", Error: null } && reply.Item?.ToString() == "remote-wins" &&
            localTurn.Actor == candidates[LocalReplyNode] && remoteTurn.Actor == candidates[RemoteReplyNode] &&
            localTurn.Inputs == 1 && remoteTurn.Inputs == 1 &&
            ReferenceEquals(localTurn.Ambient, ambient) && remoteTurn.Ambient is null &&
            (await ingress.GetAsync(scope, remoteTurn.Actor, token))?.Header.TypeData == "remote-wins",
            "a remote explicit Reply crosses the real relay after commit while only the local waiter sees Ambient");
        return 0;

        PostgresDirectRequests CreateEdge(string node, string actorType) => protectedObserver
            ? PostgresPreviewHosting.CreateRequestsWithRootStartProtection(Open, node, actorType, (_, _) => ValueTask.FromResult(true))
            : PostgresPreviewHosting.CreateRequests(Open, node, actorType);

        async Task<IAsyncDisposable> StartLocalOwnerAsync()
        {
            if (!protectedObserver)
                return await PostgresPreviewHosting.StartAsync(Open, LocalReplyNode, local, Fatal, loops, token,
                    Console.Error.WriteLine, localEdge, fenceInBatch: inBatch);
            var host = new PostgresPreviewHostedService(Open, LocalReplyNode, local, Fatal, loops, requests: localEdge)
                { ObserveReplies = edge => new EventReplyObserver(edge) };
            await host.StartAsync(token);
            return host;
        }

        static void Fatal(string message) => throw new InvalidOperationException(message);
    }

    private static async Task<long> ReplyClockAsync(Func<KernelDb> open, CancellationToken token)
    {
        await using var db = open();
        return db.Database.IsSqlite() ? await SqliteRootAdmission.ReadClockAsync(db, token) :
            await RootEventAdmission.ReadPostgresClockAsync(db, token);
    }

    private static Task WaitForReplySubscriptionsAsync(Func<KernelDb> open, Contract.BoundScope scope, string topic,
        int count, CancellationToken token) =>
        UntilReplyAsync(async () =>
        {
            await using var db = open();
            return await db.Subscriptions.CountAsync(wait => wait.Key == scope.Tenant + "_" + topic, token) == count;
        }, token);

    private static async Task UntilReplyAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        while (!await condition()) await Task.Delay(20, token);
    }

    private sealed class ReplyEventActor(EventTurns turns, string node) : Contract.IActorType
    {
        public string Type => "held-event-reply-check";
        public bool Withholds(Contract.ActorHeader header) => false;

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start)
                {
                    Observe("start", message.Payload!);
                    turn.State = "started";
                    if (message.Payload == "arm") turn.Subscribe(ReplyTopic, "live");
                    if (message.Payload == "creator") turn.Reply(message, "creator", "started");
                }
                if (message.Kind == "arm") turn.Subscribe(ReplyTopic, "live");
                if (message.Kind == "arm-shared")
                {
                    turn.Subscribe(SharedRecipientTopic, "left");
                    turn.Subscribe(SharedRecipientTopic, "right");
                }
                if (message.Event is not { } received) continue;
                Observe("event", received.Id);
                turn.SetHeader(received.Id, turn.Header.Labels);
                switch (message.Payload)
                {
                    case "emit-first":
                        turn.Emit("emit", received.Id);
                        turn.Reply(message, "reply", received.Id);
                        break;
                    case "reply-first":
                        turn.Reply(message, "reply", received.Id);
                        turn.Emit("emit", received.Id);
                        break;
                    case "early":
                        turn.Reply(message, "reply", received.Id);
                        turns.EarlyTurnEntered.TrySetResult();
                        await turns.ReleaseEarlyTurn.Task.WaitAsync(TimeSpan.FromSeconds(8));
                        break;
                    case "end":
                        turn.Reply(message, "reply", received.Id);
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                    case "shared" when turn.Header.TypeData != received.Id:
                        Observe("first-wait", received.Id, received.Wait);
                        break;
                    case "shared":
                        Observe("second-wait", received.Id, received.Wait);
                        turns.SecondWaitEntered.TrySetResult();
                        await turns.ReleaseSecondWait.Task.WaitAsync(TimeSpan.FromSeconds(8));
                        turn.Reply(message, "shared", received.Wait);
                        break;
                    case "remote" when node == LocalReplyNode:
                        turns.LocalFanoutEntered.TrySetResult();
                        break;
                    case "remote":
                        await turns.LocalFanoutEntered.Task.WaitAsync(TimeSpan.FromSeconds(8));
                        turn.Reply(message, "remote", received.Id);
                        break;
                }
            }

            void Observe(string kind, string id, string? wait = null) =>
                turns.Observed.TryAdd((kind, id, node), new(turn.Header.Ref, turn.Ambient, turn.Inbox.Count, wait));
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
