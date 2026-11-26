using Elsa.ActorFoundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    private const string RetirementKind = "sqlite-root-retirement";

    internal static async Task<int> RunRetirementAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = budget.Token;
        var temp = Directory.CreateTempSubdirectory(RetirementKind);
        try
        {
            var file = Path.Combine(temp.FullName, "retirement.db");
            await InstallFoundationAsync(file, token);
            var scope = EmbeddedTenant.Bind(RetirementKind);
            var replies = SqliteEmbeddedHosting.CreateRequests(file, "retirement-owner", RetirementKind);
            var ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(file, RetirementKind,
                (_, _, kind) => kind is Contract.Kinds.Terminate or Contract.Kinds.Delete, requests: replies, cancellationToken: token);
            var terminateOnly = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(file, RetirementKind,
                (_, _, kind) => kind == Contract.Kinds.Terminate, cancellationToken: token);
            var definitions = new SqliteAdmissionDefinitions();
            var reader = new EfStore(() => OpenFoundationDb(file, definitions), postgres: false, fenceInBatch);
            var executor = new RetirementActor();
            await using var db = OpenFoundationDb(file);
            var cursor = await SqliteRootAdmission.ReadSelectionCursorAsync(db, token);
            var finished = await AdmitAsync("finished");
            var expired = await AdmitAsync("expired");
            var protectedActor = await AdmitAsync("protected");
            var fresh = await AdmitAsync("fresh");
            var compound = await AdmitAsync("running");
            var racesJob = await AdmitAsync("running-job");
            await using (var host = OpenHost())
            {
                await host.StartAsync(token);
                foreach (var root in new[] { finished, expired, protectedActor, fresh, compound, racesJob })
                    await WaitForRootAsync(reader, scope.Tenant, root, 1, token);
                var initial = (await reader.ReadFirstRootAsync(scope.Tenant, finished.Id, token))!;
                var stamp = Seal(initial, finished);
                var wrong = PostgresRootSelection.SealTerminal(cursor, scope.Tenant, finished.Id, initial.Revision, initial.TerminalAt!.Value + 1);
                Require(await DeleteAsync(finished, "stale", wrong) is
                    { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
                    "Delete refuses a terminal selection with a different committed time");
                Require(await DeleteAsync(finished, "delete", stamp) is { Outcome: Contract.AdmissionOutcome.Accepted },
                    "Delete admits the exact terminal stamp");
                await WaitForRetirementAsync(initial.Actor.Id, InstanceStatus.Finished);
                Require(await DeleteAsync(finished, "delete", stamp) is { Outcome: Contract.AdmissionOutcome.Duplicate },
                    "the original Delete operation stays replayable after retirement");

                var eligible = (await reader.ReadFirstRootAsync(scope.Tenant, expired.Id, token))!;
                Require(await RetainAsync(expired, eligible) is { Outcome: Contract.AdmissionOutcome.Accepted },
                    "the trusted retention operation admits an eligible terminal actor");
                await WaitForRetirementAsync(eligible.Actor.Id, InstanceStatus.Finished);
                Require(await RetainAsync(expired, eligible) is { Outcome: Contract.AdmissionOutcome.Duplicate },
                    "retention derives the same operation from the same terminal mark");

                foreach (var kept in new[] { protectedActor, fresh })
                {
                    var prior = (await reader.ReadFirstRootAsync(scope.Tenant, kept.Id, token))!;
                    Require(await RetainAsync(kept, prior) is { Outcome: Contract.AdmissionOutcome.Accepted },
                        "retention stages the candidate for the owner to check");
                    while (!await db.Messages.AnyAsync(message => message.Target == prior.Actor.Id && message.Kind == Kinds.Delete && message.Done, token))
                        await Task.Delay(25, token);
                    Require(await reader.ReadFirstRootAsync(scope.Tenant, kept.Id, token) is { Revision: 1, Status: InstanceStatus.Finished },
                        "the owner preserves protected actors and actors within their retention window");
                }
                await host.StopAsync(token);
                Require(host.LastError is null, "the retirement owner stops cleanly");
            }

            var before = (await reader.ReadFirstRootAsync(scope.Tenant, compound.Id, token))!;
            var running = PostgresRootSelection.SealRunning(cursor, scope.Tenant, compound.Id);
            var payload = Json.Write(new Contract.OperatorCommand(running, ThenDelete: true, ExpectedTarget: compound));
            var target = new Contract.Target.Ref(compound);
            Require(await terminateOnly.SendAsync(scope, target, "compound", Contract.Kinds.Terminate, payload, cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Refused }, "atomic termination and deletion requires both privileges");
            Require(await ingress.SendAsync(scope, target, "compound", Contract.Kinds.Terminate, payload, cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the compound operation admits a running root");
            var deadline = await replies.ReadClockAsync(token) + 20_000;
            var held = ingress.RequestAsync(scope, target, "held-after-compound", "signal", null, null, new(null, deadline), token).AsTask();
            while (!await db.Messages.AnyAsync(row => row.Target == before.Actor.Id && row.ReplyTo != null, token))
                await Task.Delay(25, token);

            var jobRoot = (await reader.ReadFirstRootAsync(scope.Tenant, racesJob.Id, token))!;
            var jobTarget = new Contract.Target.Ref(racesJob);
            var jobSelection = PostgresRootSelection.SealRunning(cursor, scope.Tenant, racesJob.Id);
            var jobPayload = Json.Write(new Contract.OperatorCommand(jobSelection, ThenDelete: true, ExpectedTarget: racesJob));
            Require(await ingress.SendAsync(scope, jobTarget, "issue", "issue-job", cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted } &&
                await ingress.SendAsync(scope, jobTarget, "compound-after-issue", Contract.Kinds.Terminate, jobPayload, cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the ready job-issuing input precedes the admitted compound operation");

            await using var successor = OpenHost();
            await successor.StartAsync(token);
            await WaitForRetirementAsync(before.Actor.Id, InstanceStatus.Cancelled);
            Require(await held.WaitAsync(token) is { Error: "missing" }, "compound retirement answers the pending held caller once");
            Require(await db.Messages.AllAsync(row => row.Target != before.Actor.Id || row.Done && row.Start == null &&
                row.Payload == null && row.ReplyTo == null, token), "compound retirement clears its entire pending receipt footprint");
            Require(await ingress.SendAsync(scope, target, "compound", Contract.Kinds.Terminate, payload, cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Duplicate } &&
                await ingress.SendAsync(scope, target, "new", "signal", cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Refused }, "retired creation keeps the compound receipt and refuses fresh ordinary ingress");
            while (!await db.Messages.AnyAsync(row => row.Target == jobRoot.Actor.Id && row.Kind == Kinds.Terminate && row.Done, token))
                await Task.Delay(25, token);
            var runningJob = await reader.ReadFirstRootAsync(scope.Tenant, racesJob.Id, token);
            Require(runningJob is { Revision: 2, Status: InstanceStatus.Running, Jobs: not null, TerminalRevision: null } &&
                await db.Messages.CountAsync(row => row.Target == jobRoot.Actor.Id && row.InboxScope == EfStore.RootTimedJobScope, token) == 2,
                "the compound refusal settles on the exact running checkpoint and keeps its job pair");
            await executor.JobEntered.Task.WaitAsync(token);
            Require(await ingress.SendAsync(scope, jobTarget, "stop-job", Contract.Kinds.Terminate,
                Json.Write(new Contract.OperatorCommand(jobSelection)), cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "a plain Terminate can follow the settled refusal");
            await WaitForRootAsync(reader, scope.Tenant, racesJob, 3, token);
            Require(await reader.ReadFirstRootAsync(scope.Tenant, racesJob.Id, token) is { Status: InstanceStatus.Cancelled, Jobs: null } &&
                !await db.Messages.AnyAsync(row => row.Target == jobRoot.Actor.Id && row.InboxScope == EfStore.RootTimedJobScope, token),
                "the next command ends the actor and settles the retained job");
            await successor.StopAsync(token);
            Require(successor.LastError is null, "the successor stops cleanly");
            return 0;

            SqliteEmbeddedHostedService OpenHost() => SqliteEmbeddedHostedService.ForFoundation(file, "retirement-owner",
                executor, reason => throw new InvalidOperationException(reason), Loops, fenceInBatch, requests: replies,
                retentionWindow: header => header.TypeData == "fresh" ? TimeSpan.FromHours(1) : TimeSpan.Zero);

            async Task<Contract.ActorRef> AdmitAsync(string definition)
            {
                var admission = await ingress.SendAsync(scope, new Contract.Target.Start(new(RetirementKind, definition)),
                    definition, Contract.Kinds.Start, cancellationToken: token);
                Require(admission is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the retirement fixture admits a creator");
                return admission.Actor!.Value;
            }

            string Seal(EfStore.RootImage image, Contract.ActorRef reference) =>
                PostgresRootSelection.SealTerminal(cursor, scope.Tenant, reference.Id, image.TerminalRevision!.Value, image.TerminalAt!.Value);

            ValueTask<Contract.Admission> DeleteAsync(Contract.ActorRef root, string id, string selection) =>
                ingress.SendAsync(scope, new Contract.Target.Ref(root), id, Contract.Kinds.Delete,
                    Json.Write(new Contract.OperatorCommand(selection)), cancellationToken: token);

            ValueTask<Contract.Admission> RetainAsync(Contract.ActorRef root, EfStore.RootImage image) =>
                SqliteEmbeddedHosting.AdmitRetentionDeleteAsync(file, scope, root, Seal(image, root), TimeSpan.FromHours(1), token);

            async Task WaitForRetirementAsync(string actor, InstanceStatus expected)
            {
                while (!await db.Instances.AnyAsync(row => row.Id == actor && row.Retired, token)) await Task.Delay(25, token);
                var row = await db.Instances.AsNoTracking().SingleAsync(row => row.Id == actor, token);
                var history = await db.Journal.AsNoTracking().Where(entry => entry.Instance == actor).ToArrayAsync(token);
                Require(row.Status == expected && row.State == "null" && row.Revision == 2 &&
                    history is [{ Revision: -1, Entries: """["delete"]""" }],
                    "retirement commits the scrubbed row and its sole deletion marker");
            }
        }
        finally { temp.Delete(recursive: true); }
    }

    private sealed class RetirementActor : Contract.IActorType
    {
        internal TaskCompletionSource JobEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Type => RetirementKind;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => header.TypeData == "protected";
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => throw new NotSupportedException();
        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token)
        {
            JobEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.StartSettings is not null)
            {
                turn.State = "\"created\"";
                if (turn.Header.TypeData is not ("running" or "running-job")) turn.End(Contract.ActorStatus.Finished);
            }
            else if (turn.Inbox is [{ Kind: "issue-job" }])
            {
                turn.StartJob("work", timeout: TimeSpan.FromSeconds(30));
                turn.Record(new { Step = "issued" });
            }
            else if (turn.Inbox is [{ Kind: Contract.Kinds.Terminate }])
                turn.End(Contract.ActorStatus.Cancelled);
            else throw new InvalidOperationException("Unexpected input in the retirement fixture.");
            return ValueTask.CompletedTask;
        }
    }
}
