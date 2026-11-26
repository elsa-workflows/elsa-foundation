using Elsa.ActorFoundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    internal static async Task<int> RunHeldOperatorsAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = budget.Token;
        var folder = Directory.CreateTempSubdirectory("sqlite-held-operators");
        try
        {
            var file = Path.Combine(folder.FullName, "held-operators.db");
            await InstallFoundationAsync(file, token);
            var scope = EmbeddedTenant.Bind("sqlite-held-operators");
            await CheckOperatorObservationBudgetAsync(token);
            var replies = SqliteEmbeddedHosting.CreateRequests(file, "held-operators", RetirementKind);
            var ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(file, RetirementKind,
                (_, _, kind) => kind is Contract.Kinds.Terminate or Contract.Kinds.Delete, requests: replies, cancellationToken: token);
            var noOperator = await SqliteEmbeddedHosting.OpenFoundationWithRequestsAsync(file, RetirementKind, replies, token);
            var definitions = new SqliteAdmissionDefinitions();
            var reader = new EfStore(() => OpenFoundationDb(file, definitions), postgres: false, fenceInBatch);
            var executor = new RetirementActor();
            await using var db = OpenFoundationDb(file);
            var cursor = await SqliteRootAdmission.ReadSelectionCursorAsync(db, token);
            var plain = await StartAsync("plain");
            var compound = await StartAsync("compound");
            var expires = await StartAsync("expires");
            var protectedActor = await StartAsync("protection", "protected");
            var capacity = await StartAsync("capacity");
            var racesJob = await StartAsync("races-job", "running-job");
            await using (var first = OpenHost())
            {
                await first.StartAsync(token);
                foreach (var root in new[] { plain, compound, expires, protectedActor, capacity, racesJob })
                    await WaitForRootAsync(reader, scope.Tenant, root, 1, token);
                await first.StopAsync(token);
                Require(first.LastError is null, "the initial owner stops before admitting held commands");
            }

            var later = await replies.ReadClockAsync(token) + 30_000;
            var target = new Contract.Target.Ref(plain);
            var payload = Command(plain);
            Require(await noOperator.RequestAsync(scope, target, "denied", Contract.Kinds.Terminate, payload, null, new(null, later), token) is
                { Error: "missing" }, "held commands require host authorization");
            Require(await ingress.RequestAsync(scope, target, "early", Contract.Kinds.Terminate, payload, null,
                new(null, later, ReleaseBeforeCommit: true), token) is { Error: "invalid operator request" },
                "held operators refuse early release");
            var pendingPlain = CallAsync(plain, "plain");
            var rawPlain = await PendingAsync(plain, 1);
            var original = rawPlain.Single();
            Require(!pendingPlain.IsCompleted && ReplyTo.Parse(original.ReplyTo!) is { Node: "held-operators", BeforeCommit: false },
                "the original held command and local reply route commit together");
            Require(await CallAsync(plain, "plain") is { Error: "repeat" } && !pendingPlain.IsCompleted &&
                (await PendingAsync(plain, 1)).Single().ReplyTo == original.ReplyTo,
                "a duplicate reports repeat and leaves the original reply registration in place");

            var remove = CallAsync(compound, "remove", thenDelete: true);
            await PendingAsync(compound, 1);
            var sibling = CallAsync(compound, "sibling");
            await PendingAsync(compound, 2);
            var laterPark = await AddFutureParkAsync(compound);
            var expired = CallAsync(expires, "expires", window: 900);
            await PendingAsync(expires, 1);
            Require(await expired is { Error: "timeout", Status: Contract.ActorStatus.Running },
                "the deadline returns a committed status while the admitted operator waits for an owner");

            var ended = (await reader.ReadFirstRootAsync(scope.Tenant, protectedActor.Id, token))!;
            var protectedSelection = PostgresRootSelection.SealTerminal(cursor, scope.Tenant, protectedActor.Id,
                ended.TerminalRevision!.Value, ended.TerminalAt!.Value);
            var protectedReply = CallAsync(protectedActor, "protected", selection: protectedSelection, kind: Contract.Kinds.Delete);
            await PendingAsync(protectedActor, 1);

            var jobInput = await ingress.SendAsync(scope, new Contract.Target.Ref(racesJob), "issue", "issue-job", cancellationToken: token);
            var refusedCompound = CallAsync(racesJob, "refused-compound", thenDelete: true);
            await PendingAsync(racesJob, 1);
            Require(jobInput.Outcome == Contract.AdmissionOutcome.Accepted, "the live-job refusal follows an earlier ordinary input");

            await AddFutureParkAsync(capacity);
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
            var many = Enumerable.Range(0, PostgresRootAdmission.HeldReplyLimit)
                .Select(i => CallAsync(capacity, $"hold-{i}", window: 40_000, caller: waiting.Token)).ToArray();
            await PendingAsync(capacity, PostgresRootAdmission.HeldReplyLimit + 1);
            Require(await CallAsync(capacity, "extra") is { Error: "operator reply capacity" } &&
                await CallAsync(capacity, "hold-0") is { Error: "repeat" },
                "the per-root reply capacity refuses a new caller and keeps its prior receipts");
            Require(await ingress.SendAsync(scope, new Contract.Target.Ref(capacity), "one-way", Contract.Kinds.Terminate,
                Command(capacity), cancellationToken: token) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "one-way operators can still be admitted at held reply capacity");
            waiting.Cancel();
            await ExpectFoundationFailureAsync<OperationCanceledException>(() => Task.WhenAll(many),
                "the held capacity fixture disconnects each cancelled caller");

            await using var successor = OpenHost();
            await successor.StartAsync(token);
            Require(await pendingPlain is { Channel: Request.Response, Item: "terminated" } &&
                await reader.ReadFirstRootAsync(scope.Tenant, plain.Id, token) is { Status: InstanceStatus.Cancelled, TerminalRevision: 2 },
                "held Terminate answers the original caller after the Cancelled checkpoint");
            Require(await remove is { Channel: Request.Response, Item: "deleted" } && await sibling is { Error: "missing" } &&
                await reader.ReadFirstRootAsync(scope.Tenant, compound.Id, token) is null,
                "compound retirement replies to the selected caller and the scrubbed sibling");
            Require(await db.Messages.AsNoTracking().SingleAsync(row => row.Id == laterPark, token) is { Done: true, ReplyTo: null, Start: null },
                "retirement scrubs the staged-out Park-selected held route");
            Require(await replies.ReadOperatorStatusAsync(scope.Tenant, compound.Id, token) == Contract.ActorStatus.Cancelled &&
                await replies.ReadOperatorStatusAsync("other-tenant", compound.Id, token) is null,
                "timeout observation reads only the exact tenant's retired checkpoint status");
            Require(await protectedReply is { Error: "protected" } &&
                await reader.ReadFirstRootAsync(scope.Tenant, protectedActor.Id, token) is { Status: InstanceStatus.Finished, Revision: 1 },
                "a protected Delete replies after settling its refusal and preserves the root");
            await WaitForRootAsync(reader, scope.Tenant, expires, 2, token);
            Require(await reader.ReadFirstRootAsync(scope.Tenant, expires.Id, token) is { Status: InstanceStatus.Cancelled },
                "the durable command can finish after its caller's deadline");
            var refusal = await refusedCompound;
            var jobState = await reader.ReadFirstRootAsync(scope.Tenant, racesJob.Id, token);
            Require(refusal is { Error: "stale selection" } &&
                jobState is { Status: InstanceStatus.Running, Revision: 2, Jobs: not null } &&
                await db.Messages.CountAsync(row => row.Target == jobState.Actor.Id && row.InboxScope == EfStore.RootTimedJobScope, token) == 2 &&
                await db.Messages.AnyAsync(row => row.Target == jobState.Actor.Id && row.Kind == Kinds.Terminate &&
                    row.Done && row.ReplyTo == null && row.Start == null, token),
                "the held compound refusal answers once and keeps the same running job pair");
            Require(await CallAsync(racesJob, "plain-after-refusal") is { Channel: Request.Response, Item: "terminated" },
                "a subsequent held Terminate still completes");
            await successor.StopAsync(token);
            Require(successor.LastError is null, "the held operator owner stops cleanly");
            return 0;

            SqliteEmbeddedHostedService OpenHost() => SqliteEmbeddedHostedService.ForFoundation(file, "held-operators",
                executor, reason => throw new InvalidOperationException(reason), Loops, fenceInBatch, requests: replies);

            async Task<Contract.ActorRef> StartAsync(string id, string definition = "running")
            {
                var result = await ingress.SendAsync(scope, new Contract.Target.Start(new(RetirementKind, definition)),
                    id, Contract.Kinds.Start, cancellationToken: token);
                Require(result is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the held operator fixture admits a root");
                return result.Actor!.Value;
            }

            string Command(Contract.ActorRef root, bool thenDelete = false, string? selection = null) =>
                Json.Write(new Contract.OperatorCommand(selection ?? PostgresRootSelection.SealRunning(cursor, scope.Tenant, root.Id),
                    ThenDelete: thenDelete, ExpectedTarget: root));

            async Task<Contract.Reply> CallAsync(Contract.ActorRef root, string id, bool thenDelete = false,
                long window = 20_000, CancellationToken? caller = null, string? selection = null, string kind = Contract.Kinds.Terminate)
            {
                var deadline = await replies.ReadClockAsync(token) + window;
                return await ingress.RequestAsync(scope, new Contract.Target.Ref(root), id, kind, Command(root, thenDelete, selection),
                    null, new(null, deadline), caller ?? token);
            }

            async Task<List<MessageEntity>> PendingAsync(Contract.ActorRef root, int count)
            {
                var actor = (await reader.ReadFirstRootAsync(scope.Tenant, root.Id, token))!.Actor.Id;
                while (true)
                {
                    var rows = await db.Messages.AsNoTracking().Where(row => row.Target == actor && row.ReplyTo != null && !row.Done)
                        .OrderBy(row => row.Seq).ToListAsync(token);
                    if (rows.Count == count) return rows;
                    await Task.Delay(25, token);
                }
            }

            async Task<string> AddFutureParkAsync(Contract.ActorRef root)
            {
                var image = (await reader.ReadFirstRootAsync(scope.Tenant, root.Id, token))!;
                var registration = new Requests().OpenBound("queued-park-control", later, scope.Tenant, null);
                return await InsertLaterOperatorAsync(db, root.Id, image.Actor.Id, token, registration.ReplyAddress("later-stage"));
            }
        }
        finally { folder.Delete(recursive: true); }
    }

    private static async Task CheckOperatorObservationBudgetAsync(CancellationToken token)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var status = SqliteEmbeddedHosting.SqliteDirectRequests.ObserveOperatorStatusAsync(_ =>
        {
            entered.TrySetResult();
            release.Wait();
            exited.TrySetResult();
            return ValueTask.FromResult<Contract.ActorStatus?>(Contract.ActorStatus.Running);
        }, token).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1), token);
            Require(await status.WaitAsync(TimeSpan.FromSeconds(1), token) is null,
                "a synchronous optional status observation cannot hold the settled deadline");
        }
        finally
        {
            release.Set();
            if (entered.Task.IsCompleted) await exited.Task.WaitAsync(token);
        }
        Require(await SqliteEmbeddedHosting.SqliteDirectRequests.ObserveOperatorStatusAsync(
            _ => throw new StoreFormatException("Fixture status read failed."), token) is null,
            "an optional status read failure leaves the settled timeout without status");
    }
}
