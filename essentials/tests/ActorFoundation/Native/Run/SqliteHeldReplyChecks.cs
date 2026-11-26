using Elsa.ActorFoundation.Hosting;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    private const string HeldReplyKind = "sqlite-held-replies";

    internal static async Task<int> RunHeldRepliesAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = budget.Token;
        var folder = Directory.CreateTempSubdirectory(HeldReplyKind);
        try
        {
            var path = Path.Combine(folder.FullName, "held.db");
            await InstallFoundationAsync(path, token);
            var scope = EmbeddedTenant.Bind(HeldReplyKind);
            var other = ContractScopes.Bind("other-tenant");
            var replies = SqliteEmbeddedHosting.CreateRequests(path, "held", HeldReplyKind);
            var ingress = await SqliteEmbeddedHosting.OpenFoundationWithRequestsAsync(path, HeldReplyKind, replies, token);
            var start = new Contract.Target.Start(new(HeldReplyKind, "held"));
            var actor = new HeldReplyActor();
            var definitions = new SqliteAdmissionDefinitions();
            var reader = new EfStore(() => OpenFoundationDb(path, definitions), postgres: false);
            await using var db = OpenFoundationDb(path);

            await ExpectFoundationFailureAsync<ArgumentException>(() => SqliteEmbeddedHosting.StartFoundationAsync(path, "other",
                actor, _ => { }, cancellationToken: token, requests: replies), "the owner refuses an edge bound to another node");
            var secondPath = Path.Combine(folder.FullName, "other.db");
            await InstallFoundationAsync(secondPath, token);
            await ExpectFoundationFailureAsync<ArgumentException>(() =>
                SqliteEmbeddedHosting.OpenFoundationWithRequestsAsync(secondPath, HeldReplyKind, replies, token),
                "ingress refuses an edge bound to another SQLite file");

            await using var service = OpenHost("held", actor, replies);
            await service.StartAsync(token);
            var pendingStart = RequestAsync(scope, start, "start", Contract.Kinds.Start, 20_000);
            var root = await actor.StartEntered.Task.WaitAsync(token);
            var exact = new Contract.Target.Ref(root);
            var creator = await db.Messages.AsNoTracking().SingleAsync(row => row.InboxScope == 0, token);
            Require(!pendingStart.IsCompleted && creator is { ReplyTo: not null, Done: false } &&
                await reader.ReadFirstRootAsync(scope.Tenant, root.Id, token) is null,
                "a held start waits on its durable creator before the first checkpoint");
            var missingWhenPending = await RequestAsync(scope, exact, "before-bind", "complete", 5_000);
            Require(missingWhenPending is { Error: "missing" }, "a held direct call needs the exact root to have a checkpoint");
            var duplicateStart = await RequestAsync(scope, start, "start", Contract.Kinds.Start, 700);
            Require(duplicateStart is { Error: "timeout", Status: null } && !pendingStart.IsCompleted &&
                await db.Messages.CountAsync(token) == 1, "a repeated start waits independently and admits no extra creator");

            actor.ReleaseStart.TrySetResult();
            Require(await pendingStart is { Channel: Request.Response, Item: "started", Error: null } &&
                await reader.ReadFirstRootAsync(scope.Tenant, root.Id, token) is { Revision: 1, Status: InstanceStatus.Running },
                "the release after commit answers the caller and leaves its first checkpoint readable");
            var crossTenant = await RequestAsync(other, exact, "cross-tenant", "complete", 5_000);
            Require(crossTenant is { Error: "missing" }, "a different tenant cannot request work on the root reference");

            var pendingEnd = RequestAsync(scope, exact, "complete", "complete", 20_000);
            await actor.EndEntered.Task.WaitAsync(token);
            var direct = await db.Messages.AsNoTracking().SingleAsync(row => !row.Done, token);
            Require(direct.InboxScope == InboxKey.FoundationOccurrenceScope && direct.ReplyTo is not null &&
                direct.Start?.StartsWith(RootAdmissionWire.HeldDirectPrefix, StringComparison.Ordinal) == true && !pendingEnd.IsCompleted,
                "the accepted direct request waits on a held occurrence in the ordinary FIFO");
            var duplicateEnd = await RequestAsync(scope, exact, "complete", "complete", 700);
            Require(duplicateEnd is { Error: "timeout", Status: Contract.ActorStatus.Running } &&
                await db.Messages.CountAsync(row => !row.Done, token) == 1, "a duplicate direct request times out with the committed live status");

            actor.ReleaseEnd.TrySetResult();
            Require(await pendingEnd is { Channel: Request.Response, Item: "direct", Error: null },
                "the response arrives before the terminal notification from the same turn");
            Require(await reader.ReadFirstRootAsync(scope.Tenant, root.Id, token) is { Revision: 2, TerminalRevision: 2 } &&
                await db.Messages.AsNoTracking().SingleAsync(token) is { Done: true, ReplyTo: null, Start: null, Payload: null },
                "the terminal checkpoint consumes the exact occurrence and clears its reply route");
            var expired = await RequestAsync(scope, exact, "late", "complete", -1);
            Require(expired is { Error: "timeout", Status: Contract.ActorStatus.Finished } &&
                await db.Messages.CountAsync(token) == 1, "an expired call reports the committed terminal status without admission");
            await service.StopAsync(token);
            Require(service.LastError is null, "the SQLite reply owner stops cleanly");

            var abandoned = await RequestAsync(scope, start, "abandoned", Contract.Kinds.Start, 700);
            Require(abandoned is { Error: "timeout", Status: null } &&
                await db.Messages.AnyAsync(row => row.InboxScope == 0 && row.ReplyTo != null && !row.Done, token),
                "a caller can time out while its durable held creator remains pending");
            var replacement = new HeldReplyActor();
            replacement.ReleaseStart.TrySetResult();
            await using var recovery = OpenHost("replacement", replacement, null);
            await recovery.StartAsync(token);
            var recovered = await replacement.StartEntered.Task.WaitAsync(token);
            await WaitForRootAsync(reader, scope.Tenant, recovered, 1, token);
            Require(await reader.ReadFirstRootAsync(scope.Tenant, recovered.Id, token) is { Status: InstanceStatus.Running } &&
                !await db.Messages.AnyAsync(row => row.InboxScope == 0, token),
                "a new owner commits the durable creator after the caller's reply registration has gone");
            await recovery.StopAsync(token);
            Require(recovery.LastError is null, "the replacement stops cleanly");
            return 0;

            SqliteEmbeddedHostedService OpenHost(string node, HeldReplyActor executor, SqliteEmbeddedHosting.SqliteDirectRequests? edge) =>
                SqliteEmbeddedHostedService.ForFoundation(path, node, executor, reason => throw new InvalidOperationException(reason),
                    Loops, fenceInBatch, requests: edge);

            async Task<Contract.Reply> RequestAsync(Contract.BoundScope tenant, Contract.Target target, string id, string kind, long remaining)
            {
                var deadline = await replies.ReadClockAsync(token) + remaining;
                return await ingress.RequestAsync(tenant, target, id, kind, null, null, new(null, deadline), token);
            }
        }
        finally { folder.Delete(recursive: true); }
    }

    private sealed class HeldReplyActor : Contract.IActorType
    {
        internal TaskCompletionSource<Contract.ActorRef> StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource EndEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseEnd { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Type => HeldReplyKind;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => throw new NotSupportedException();
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => throw new NotSupportedException();

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.StartSettings is not null)
            {
                StartEntered.TrySetResult(turn.Header.Ref);
                await ReleaseStart.Task.WaitAsync(token);
                turn.State = "\"waiting\"";
                turn.Emit(Request.Response, "started");
                return;
            }
            if (turn.Inbox is not [{ Kind: "complete" }]) throw new InvalidOperationException("Expected exactly one direct request.");
            EndEntered.TrySetResult();
            await ReleaseEnd.Task.WaitAsync(token);
            turn.Emit(Request.Response, "direct");
            turn.End(Contract.ActorStatus.Finished, "end");
        }
    }
}
