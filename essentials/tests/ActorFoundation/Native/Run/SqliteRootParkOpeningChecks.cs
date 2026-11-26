using Elsa.ActorFoundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    private const string ParkKind = "sqlite-park-opening";
    private const string CreatorFault = "deliberate creator failure";
    private const string ContinuationFault = "deliberate continuation failure";
    private const string TerminateFault = "deliberate Terminate failure";
    private const string JobResultFault = "deliberate result application failure";
    private const string ParkState = "\"last committed state\"";

    internal static async Task<int> RunParkOpeningAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = budget.Token;
        var directory = Directory.CreateTempSubdirectory(ParkKind);
        try
        {
            var file = Path.Combine(directory.FullName, "park.db");
            await InstallFoundationAsync(file, token);
            var scope = EmbeddedTenant.Bind(ParkKind);
            var definitions = new SqliteAdmissionDefinitions();
            var store = new EfStore(() => OpenFoundationDb(file, definitions), postgres: false, fenceInBatch);
            var requests = SqliteEmbeddedHosting.CreateRequests(file, "park-first", ParkKind);
            var ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(file, ParkKind,
                (_, _, kind) => kind == Contract.Kinds.Terminate, requests: requests, cancellationToken: token);
            await using var initialDb = OpenFoundationDb(file);
            var cursor = await SqliteRootAdmission.ReadSelectionCursorAsync(initialDb, token);
            var creator = await StartAsync("throw-at-start");
            var continuation = await StartAsync("running");
            var faultingOperator = await StartAsync("throw-at-terminate");
            var faultingResult = await StartAsync("throw-on-job-result");
            var creatorInput = await initialDb.Messages.FromSqlInterpolated($"""
                SELECT m.* FROM "Messages" m JOIN "FoundationRootBirths" b
                    ON b."Creator" = m."Id" AND b."Actor" = m."Target" AND b."Partition" = m."Partition"
                WHERE b."Tenant" = {scope.Tenant} AND b."Reference" = {creator.Id} AND b."Creation" = 1
                    AND m."InboxScope" = 0 AND NOT m."Done"
                """).AsNoTracking().SingleAsync(token);
            MessageEntity continuationInput;
            var firstActor = new ParkOpeningActor();
            long firstEpoch;
            EfStore.RootImage beforeStart;
            EfStore.RootImage beforeTurn;
            await using (var owner = Host("park-first", firstActor, requests))
            {
                await owner.StartAsync(token);
                beforeStart = await ImageAsync(creator, 1, owner);
                Require(beforeStart is { Status: InstanceStatus.Faulted, Fault: CreatorFault, ParkAt: > 0, State: null,
                    TerminalRevision: null, TerminalAt: null }, "a throwing creator commits a nonterminal Park at revision one");
                var running = await ImageAsync(continuation, 1, owner);
                firstEpoch = (await FenceAsync(file, token)).Epoch;
                Require(running is { Status: InstanceStatus.Running, State: ParkState, Jobs: not null },
                    "a healthy creator commits the prior state and an ordinary timed job");
                await firstActor.JobEntered.Task.WaitAsync(token);
                var due = await requests.ReadClockAsync(token) + 15_000;
                var target = new Contract.Target.Ref(continuation);
                var pendingFault = ingress.RequestAsync(scope, target, "fault", "fail", "\"original\"", null, new(null, due), token).AsTask();
                await firstActor.FaultEntered.Task.WaitAsync(token);
                continuationInput = await initialDb.Messages.AsNoTracking().SingleAsync(row => row.Target == running.Actor.Id &&
                    row.InboxScope == InboxKey.FoundationOccurrenceScope && row.ReplyTo != null && !row.Done, token);
                var selection = PostgresRootSelection.SealRunning(cursor, scope.Tenant, continuation.Id);
                var command = Json.Write(new Contract.OperatorCommand(selection, ExpectedTarget: continuation));
                var pendingOperator = ingress.RequestAsync(scope, target, "terminate", Contract.Kinds.Terminate, command,
                    null, new(null, due), token).AsTask();
                while (!await initialDb.Messages.AnyAsync(row => row.Target == running.Actor.Id && !row.Done &&
                    row.Kind == Kinds.Terminate, token))
                    await Task.Delay(25, token);
                firstActor.ReleaseFault.TrySetResult();
                var reply = await pendingFault;
                Require(await pendingOperator is { Error: "parked" },
                    "a held operator admitted during the callback receives the committed Park refusal");
                beforeTurn = await ImageAsync(continuation, 3, owner);
                Require(reply is { Status: Contract.ActorStatus.Faulted, Error: null } &&
                    beforeTurn is { Status: InstanceStatus.Faulted, Fault: ContinuationFault, ParkAt: > 0, State: ParkState,
                        TerminalRevision: null, TerminalAt: null } && beforeTurn.Jobs == running.Jobs &&
                    beforeTurn.NextJobGeneration == running.NextJobGeneration,
                    "a throwing continuation replies after the Park keeps its prior state and job generation");
                var operatorRoot = await ImageAsync(faultingOperator, 1, owner);
                var terminate = Json.Write(new Contract.OperatorCommand(
                    PostgresRootSelection.SealRunning(cursor, scope.Tenant, faultingOperator.Id), ExpectedTarget: faultingOperator));
                Require(await ingress.RequestAsync(scope, new Contract.Target.Ref(faultingOperator), "faulting-terminate", Contract.Kinds.Terminate,
                    terminate, null, new(null, await requests.ReadClockAsync(token) + 15_000), token) is { Error: "parked", Status: null },
                    "the throwing held Terminate receives its own committed refusal after the Park opens");
                var terminated = await ImageAsync(faultingOperator, 3, owner);
                Require(terminated is { Fault: TerminateFault, TerminalRevision: null, ParkAt: > 0, State: ParkState } &&
                    await initialDb.Database.SqlQuery<bool>($"""
                        SELECT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {operatorRoot.Actor.Id} AND "Kind" = {Kinds.Terminate}
                            AND "Done" AND "FoundationKeptBy" IS NULL AND "Start" IS NULL) AS "Value"
                        """).SingleAsync(token), "a callback throwing on Terminate opens the Park and the owner settles the command outside the kept set");
                var jobPark = await ImageAsync(faultingResult, 3, owner);
                Require(jobPark is { Status: InstanceStatus.Faulted, Fault: JobResultFault, ParkAt: > 0, State: ParkState,
                    NextJobGeneration: 1, TerminalRevision: null } && JobRegistry.Read(jobPark.Jobs) is [{ Key: "faulting-result", Generation: 0 }] &&
                    await initialDb.Database.SqlQuery<bool>($"""
                        SELECT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {jobPark.Actor.Id} AND "InboxScope" = {EfStore.RootTimedJobScope}
                            AND "Kind" = {Kinds.JobFinished} AND "JobGeneration" = 0 AND "Done" AND "Trial" = 0
                            AND "FoundationKeptBy" = {(int)ParkKeepReason.Consumed}
                            AND "Payload" = {Json.Write(new JobFinished("faulting-result", "\"published\"", null))}) AS "Value"
                        """).SingleAsync(token), "a callback throwing on a recorded timed result preserves the result and its registration under the Park");
                Require(firstActor.CreatorFaults == 1 && firstActor.ContinuationFaults == 1 && firstActor.JobResultFaults == 1,
                    "each failing callback runs once before the owner stops");
                await owner.StopAsync(token);
                Require(owner.LastError is null && await FenceAsync(file, token) == (null, firstEpoch),
                    "the callback faults leave the host able to stop and release ownership");
            }

            await CheckParkAsync(creator, beforeStart, creatorInput, openedAtRevision: 1);
            await CheckParkAsync(continuation, beforeTurn, continuationInput, openedAtRevision: 2);
            var successorActor = new ParkOpeningActor();
            await using var successor = Host("park-next", successorActor, null);
            await successor.StartAsync(token);
            var marker = await StartAsync("after-takeover");
            await ImageAsync(marker, 1, successor);
            var (next, nextEpoch) = await FenceAsync(file, token);
            Require(next == "park-next" && nextEpoch > firstEpoch, "a fresh owner opens the same file at a higher epoch");
            Require(successorActor.CreatorFaults == 0 && successorActor.ContinuationFaults == 0 &&
                await ImageAsync(creator, 1, successor) is { Fault: CreatorFault, Revision: 1 } &&
                await ImageAsync(continuation, 3, successor) is { Fault: ContinuationFault, Revision: 3, State: ParkState } &&
                successorActor.JobResultFaults == 0 && await ImageAsync(faultingResult, 3, successor) is { Fault: JobResultFault, Revision: 3 },
                "the new owner makes progress without replaying the retained fault inputs");
            await using (var db = OpenFoundationDb(file))
                Require(!successorActor.JobEntered.Task.IsCompleted && await db.Messages.AsNoTracking().CountAsync(row =>
                    row.Target == beforeTurn.Actor.Id && row.InboxScope == EfStore.RootTimedJobScope && !row.Done, token) == 2,
                    "replacement ownership leaves the parked job and deadline pending without running either");
            await CheckParkAsync(creator, beforeStart, creatorInput, openedAtRevision: 1);
            await CheckParkAsync(continuation, beforeTurn, continuationInput, openedAtRevision: 2);
            await successor.StopAsync(token);
            Require(successor.LastError is null, "replacement ownership stops cleanly after reading the Parked file");
            return 0;

            SqliteEmbeddedHostedService Host(string node, ParkOpeningActor actor, SqliteEmbeddedHosting.SqliteDirectRequests? replies) =>
                SqliteEmbeddedHostedService.ForFoundation(file, node, actor,
                    reason => throw new InvalidOperationException(reason), Loops, fenceInBatch, requests: replies);

            async Task<Contract.ActorRef> StartAsync(string definition)
            {
                var result = await ingress.SendAsync(scope, new Contract.Target.Start(new(ParkKind, definition)),
                    definition, Contract.Kinds.Start, cancellationToken: token);
                Require(result is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the Park fixture admits a root");
                return result.Actor!.Value;
            }

            async Task<EfStore.RootImage> ImageAsync(Contract.ActorRef actor, long revision, SqliteEmbeddedHostedService owner)
            {
                while (true)
                {
                    if (owner.LastError is { } error) throw new InvalidOperationException("The Park fixture owner failed.", error);
                    var image = await store.ReadFirstRootAsync(scope.Tenant, actor.Id, token);
                    if (image is not null && image.Revision >= revision) return image;
                    await Task.Delay(25, token);
                }
            }

            async Task CheckParkAsync(Contract.ActorRef reference, EfStore.RootImage prior, MessageEntity original, long openedAtRevision)
            {
                await using var db = OpenFoundationDb(file);
                var actor = prior.Actor.Id;
                Require(await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "FoundationActorList" p JOIN "Instances" i
                        ON i."FoundationTenant" = p."Tenant" AND i."FoundationReference" = p."Reference"
                        WHERE i."Id" = {actor} AND p."Tenant" = {scope.Tenant} AND p."Reference" = {reference.Id}
                            AND p."ParkAt" = {prior.ParkAt} AND i."FoundationParkAt" = p."ParkAt"
                            AND p."ParkRevision" = {openedAtRevision} AND i."FoundationParkRevision" = p."ParkRevision"
                            AND p."ParkFault" = {DurableKeyJson.Write(prior.Fault!)} AND i."Fault" = p."ParkFault"
                            AND p."Status" = {(int)InstanceStatus.Faulted} AND p."EndedAt" IS NULL
                            AND EXISTS (SELECT 1 FROM "Journal" j WHERE j."Instance" = i."Id" AND j."Partition" = i."Partition"
                                AND j."Revision" = p."ParkRevision" AND j."Entries" = '["parked"]')) AS "Value"
                    """).SingleAsync(token), "the rooted checkpoint, actor projection and fault journal name the same Park");
                Require(await db.Database.SqlQuery<bool>($"""
                    SELECT count(*) = 1 AS "Value" FROM "Messages"
                    WHERE "Target" = {actor} AND "Partition" = {original.Partition} AND "InboxScope" = {original.InboxScope}
                        AND "Id" = {original.Id} AND "Seq" = {original.Seq} AND "Kind" = {original.Kind} AND "Due" = {original.Due}
                        AND "Start" IS NOT DISTINCT FROM {original.Start} AND "Payload" IS NOT DISTINCT FROM {original.Payload}
                        AND "ReplyTo" IS NOT DISTINCT FROM {original.ReplyTo} AND "KeepUntil" IS NOT DISTINCT FROM {original.KeepUntil}
                        AND "Sender" IS NOT DISTINCT FROM {original.Sender} AND "JobGeneration" = {original.JobGeneration}
                        AND "Done" AND "Trial" = 0 AND "FoundationKeptBy" = {(int)ParkKeepReason.Consumed}
                    """).SingleAsync(token), "the exact fault envelope and reply route are durably retained outside the pending mailbox");
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class ParkOpeningActor(bool protectDeletion = false, Func<Contract.ActorHeader, bool>? protection = null) : Contract.IActorType
    {
        public string Type => ParkKind;
        public TaskCompletionSource JobEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FaultEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFault { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CreatorFaults { get; private set; }
        public int ContinuationFaults { get; private set; }
        public int JobResultFaults { get; private set; }
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) =>
            protection?.Invoke(header) ?? (protectDeletion && header.TypeData == "protected");
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            if (job.Key == "faulting-result") return "published";
            JobEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            turn.State = "\"uncommitted\"";
            if (turn.Header.TypeData == "throw-at-start")
            {
                CreatorFaults++;
                throw new InvalidOperationException(CreatorFault);
            }
            if (turn.Inbox.Any(input => input.Kind == "fail"))
            {
                ContinuationFaults++;
                FaultEntered.TrySetResult();
                await ReleaseFault.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                throw new InvalidOperationException(ContinuationFault);
            }
            if (turn.Inbox.Any(input => input.Kind == Contract.Kinds.JobFinished))
            {
                JobResultFaults++;
                throw new InvalidOperationException(JobResultFault);
            }
            if (turn.Header.TypeData == "throw-at-terminate" && turn.Inbox.Any(input => input.Kind == Contract.Kinds.Terminate))
                throw new InvalidOperationException(TerminateFault);
            turn.State = ParkState;
            if (turn.Header.TypeData == "running" && turn.Inbox[0].Kind == Contract.Kinds.Start)
                turn.StartJob("job-at-park", timeout: TimeSpan.FromSeconds(30));
            if (turn.Header.TypeData == "throw-on-job-result" && turn.Inbox[0].Kind == Contract.Kinds.Start)
                turn.StartJob("faulting-result", timeout: TimeSpan.FromSeconds(30));
        }
    }
}
