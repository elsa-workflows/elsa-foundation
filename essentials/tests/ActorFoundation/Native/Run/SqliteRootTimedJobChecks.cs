using System.Collections.Concurrent;
using Elsa.ActorFoundation.Hosting;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    private const string TimedJobKind = "sqlite-timed-jobs";

    internal static async Task<int> RunTimedJobsAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = budget.Token;
        var directory = Directory.CreateTempSubdirectory(TimedJobKind);
        try
        {
            var path = Path.Combine(directory.FullName, "timed.db");
            await InstallFoundationAsync(path, token);
            var scope = EmbeddedTenant.Bind(TimedJobKind);
            var ingress = await SqliteEmbeddedHosting.OpenFoundationAsync(path, TimedJobKind, cancellationToken: token);
            var actor = new TimedJobActor();
            var definitions = new SqliteAdmissionDefinitions();
            var store = new EfStore(() => OpenFoundationDb(path, definitions), postgres: false, fenceInBatch);
            await using var db = OpenFoundationDb(path);

            var legacy = await StartAsync("legacy");
            await SeedPreJobCheckpointAsync(db, scope.Tenant, legacy.Id, token);
            Require(await store.ReadFirstRootAsync(scope.Tenant, legacy.Id, token)
                is { Revision: 1, NextJobGeneration: null, Jobs: null }, "the staged no-job checkpoint has no physical generation");

            await using var service = SqliteEmbeddedHostedService.ForFoundation(path, "sqlite-timed-node", actor,
                reason => throw new InvalidOperationException(reason), Loops, fenceInBatch);
            await service.StartAsync(token);
            var startOnLegacy = await ingress.SendAsync(scope, new Contract.Target.Ref(legacy), "issue", "issue", cancellationToken: token);
            Require(startOnLegacy.Outcome == Contract.AdmissionOutcome.Accepted, "a continuation can issue a job from the older checkpoint");
            var legacyEnd = await WaitForAsync(legacy, 4);
            Require(legacyEnd is { Status: InstanceStatus.Finished, TerminalRevision: 4, NextJobGeneration: 1, Jobs: null },
                "a fresh node treats the absent generation as zero and persists the successor generation");
            await RequireNoJobRowsAsync(legacyEnd);

            var chained = await StartAsync("chained");
            var issued = await WaitForAsync(chained, 1);
            var initialJob = await actor.FirstEntered.Task.WaitAsync(token);
            Require(issued is { Status: InstanceStatus.Running, NextJobGeneration: 1 } &&
                JobRegistry.Read(issued.Jobs) is [{ Generation: 0 }] &&
                initialJob is { AttemptNumber: 1, Deadline: > 0 } && initialJob.Actor == chained,
                "the first bind persists its running job generation and exposes the exact leased attempt");
            var stored = await db.Messages.AsNoTracking().Where(message => message.Target == issued.Actor.Id &&
                message.InboxScope == EfStore.RootTimedJobScope).OrderBy(message => message.Seq).ToListAsync(token);
            Require(stored.Count == 2 && stored[0].Kind == Kinds.JobDeadline && stored[0].Due == initialJob.Deadline &&
                stored[1].Kind == Kinds.Job && stored.All(message => message.JobGeneration == 0),
                "the checkpoint, ticket and absolute deadline commit together");

            actor.ReleaseFirst.TrySetResult();
            var chainedEnd = await WaitForAsync(chained, 5);
            Require(chainedEnd is { Status: InstanceStatus.Finished, TerminalRevision: 5, Jobs: null, NextJobGeneration: 2 } &&
                actor.ChainedApplied.SequenceEqual(["success", "failure"]),
                "a recorded result can issue the next timed generation and both outcomes are applied once");
            await RequireNoJobRowsAsync(chainedEnd);

            var timeout = await StartAsync("timeout");
            var expired = await WaitForAsync(timeout, 3);
            await actor.TimeoutCancelled.Task.WaitAsync(token);
            Require(expired is { Status: InstanceStatus.Finished, Jobs: null, NextJobGeneration: 1 } &&
                actor.TimeoutApplied == 1, "the database deadline wins and the actor applies one timeout");
            await RequireNoJobRowsAsync(expired);

            var terminalTimeout = await StartAsync("fault-on-timeout");
            var faulted = await WaitForAsync(terminalTimeout, 3);
            Require(faulted is { Status: InstanceStatus.Faulted, TerminalRevision: 3, TerminalAt: > 0, Jobs: null },
                "the model can end Faulted when applying a recorded timeout and commits an exact terminal mark");
            await RequireNoJobRowsAsync(faulted);

            var interrupted = await StartAsync("interrupted");
            await actor.InterruptedEntered.Task.WaitAsync(token);
            var stop = await ingress.SendAsync(scope, new Contract.Target.Ref(interrupted), "finish", "finish", cancellationToken: token);
            Require(stop.Outcome == Contract.AdmissionOutcome.Accepted, "an ordinary turn can finish with a job in flight");
            var finished = await WaitForAsync(interrupted, 2);
            await actor.InterruptedCancelled.Task.WaitAsync(token);
            Require(finished is { Status: InstanceStatus.Finished, TerminalRevision: 2, Jobs: null, NextJobGeneration: 1 },
                "the terminal checkpoint clears the active job atomically");
            await RequireNoJobRowsAsync(finished);

            await service.StopAsync(token);
            Require(service.LastError is null, "the timed job host stops cleanly");
            return 0;

            async Task<Contract.ActorRef> StartAsync(string definition)
            {
                var result = await ingress.SendAsync(scope, new Contract.Target.Start(new(TimedJobKind, definition)),
                    definition, Contract.Kinds.Start, cancellationToken: token);
                Require(result is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the timed job root is admitted");
                return result.Actor!.Value;
            }

            async Task<EfStore.RootImage> WaitForAsync(Contract.ActorRef reference, long revision)
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (service.LastError is { } error) throw new InvalidOperationException("The timed job host failed.", error);
                    var image = await store.ReadFirstRootAsync(scope.Tenant, reference.Id, token);
                    if (image is not null && image.Revision >= revision) return image;
                    await Task.Delay(25, token);
                }
            }

            async Task RequireNoJobRowsAsync(EfStore.RootImage image) =>
                Require(!await db.Messages.AnyAsync(message => message.Target == image.Actor.Id &&
                    message.InboxScope == EfStore.RootTimedJobScope, token), "settlement leaves no physical ticket, deadline or result");
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task SeedPreJobCheckpointAsync(KernelDb db, string tenant, string reference, CancellationToken token)
    {
        // Bind the real admitted creator with the physical fields from the preceding SQLite slice.
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var actor = await db.Database.SqlQuery<string>($"""
            SELECT "Actor" AS "Value" FROM "FoundationRootBirths" WHERE "Tenant" = {tenant} AND "Reference" = {reference}
            """).SingleAsync(token);
        var home = new InstanceRef(actor).Partition;
        db.Instances.Add(new()
        {
            Id = actor, Partition = home, Status = InstanceStatus.Running, Revision = 1, StateVersion = 1,
            State = "wire", History = History.Full
        });
        db.Journal.Add(new() { Partition = home, Position = 1, Instance = actor, Revision = 1, Entries = "[]" });
        await db.SaveChangesAsync(token);
        var header = DurableKeyJson.Write(new
        {
            Model = TimedJobKind, Definition = "legacy", TypeData = "legacy",
            Labels = Array.Empty<KeyValuePair<string, string>>(), EscapedState = false
        });
        var wire = RootSelfContent.Encode("\"waiting\"");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "Target" = {actor} AND "InboxScope" = 0;
            UPDATE "Instances" SET "FoundationReference" = {reference}, "FoundationTenant" = {tenant},
                "FoundationHeader" = {header}, "FoundationStateWire" = {wire}, "State" = {db.RootStateFamily.Prefix} || "State"
            WHERE "Id" = {actor};
            UPDATE "Partitions" SET "Position" = 1 WHERE "Id" = {home};
            """, token);
        await tx.CommitAsync(token);
    }

    private sealed class TimedJobActor : Contract.IActorType
    {
        internal TaskCompletionSource<Contract.IJob> FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource TimeoutCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource InterruptedEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource InterruptedCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentQueue<string> ChainedApplied { get; } = new();
        internal int TimeoutApplied { get; private set; }

        public string Type => TimedJobKind;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.StartSettings is not null)
            {
                var key = turn.Header.TypeData;
                turn.StartJob(key, timeout: TimeSpan.FromSeconds(key is "timeout" or "fault-on-timeout" ? 4 : 20));
                turn.State = "\"waiting\"";
                return ValueTask.CompletedTask;
            }
            if (turn.Inbox is [{ Kind: "issue" }] && turn.Header.TypeData == "legacy")
            {
                turn.StartJob("legacy", timeout: TimeSpan.FromSeconds(20));
                turn.Record(new { Step = "issued" });
            }
            else if (turn.Inbox is [{ Kind: "finish" }] && turn.Header.TypeData == "interrupted")
                turn.End(Contract.ActorStatus.Finished);
            else
            {
                if (turn.Inbox is not [{ Kind: Contract.Kinds.JobFinished, Payload: { } payload }])
                    throw new InvalidOperationException("The actor expected one recorded job result.");
                var result = Json.Read<Contract.JobFinished>(payload);
                switch (result)
                {
                    case { Key: "chained", Error: null, Result: "\"success\"" }:
                        ChainedApplied.Enqueue("success");
                        turn.StartJob("failure", timeout: TimeSpan.FromSeconds(20));
                        turn.Record(new { Step = "second" });
                        break;
                    case { Key: "failure", Error: "test failure" }:
                        ChainedApplied.Enqueue("failure");
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                    case { Key: "timeout", Error: "timeout" }:
                        TimeoutApplied++;
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                    case { Key: "fault-on-timeout", Error: "timeout" }:
                        turn.End(Contract.ActorStatus.Faulted);
                        break;
                    case { Key: "legacy", Error: null, Result: "\"success\"" }:
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                    default:
                        throw new InvalidOperationException("The recorded result did not match the active job.");
                }
            }
            return ValueTask.CompletedTask;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token)
        {
            switch (job.Key)
            {
                case "chained":
                    FirstEntered.TrySetResult(job);
                    await ReleaseFirst.Task.WaitAsync(token);
                    return "success";
                case "failure":
                    throw new InvalidOperationException("test failure");
                case "legacy":
                    return "success";
                case "timeout":
                case "fault-on-timeout":
                    return await WaitForCancellationAsync(TimeoutCancelled, token);
                case "interrupted":
                    InterruptedEntered.TrySetResult();
                    return await WaitForCancellationAsync(InterruptedCancelled, token);
                default:
                    throw new InvalidOperationException("The timed job attempt is unexpected.");
            }
        }

        private static async Task<object?> WaitForCancellationAsync(TaskCompletionSource cancelled, CancellationToken token)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { if (token.IsCancellationRequested) cancelled.TrySetResult(); }
            return null;
        }
    }
}
