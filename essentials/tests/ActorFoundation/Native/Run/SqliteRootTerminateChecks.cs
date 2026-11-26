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
    private const string OperatorKind = "sqlite-root-terminate";

    internal static async Task<int> RunTerminateAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = budget.Token;
        var folder = Directory.CreateTempSubdirectory(OperatorKind);
        try
        {
            var path = Path.Combine(folder.FullName, "operators.db");
            await InstallFoundationAsync(path, token);
            var scope = EmbeddedTenant.Bind(OperatorKind);
            var other = ContractScopes.Bind("different-tenant");
            var ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(path, OperatorKind,
                (_, _, kind) => kind == Contract.Kinds.Terminate, cancellationToken: token);
            var unauthorized = await SqliteEmbeddedHosting.OpenFoundationAsync(path, OperatorKind, cancellationToken: token);
            var actor = new TerminationActor();
            var definitions = new SqliteAdmissionDefinitions();
            var store = new EfStore(() => OpenFoundationDb(path, definitions), postgres: false, fenceInBatch);
            await using var db = OpenFoundationDb(path);
            var first = await AdmitAsync("ordinary");
            var second = await AdmitAsync("job");
            var cursor = await SqliteRootAdmission.ReadSelectionCursorAsync(db, token);
            var firstSelection = PostgresRootSelection.SealRunning(cursor, scope.Tenant, first.Id);
            var secondSelection = PostgresRootSelection.SealRunning(cursor, scope.Tenant, second.Id);
            Require(await SendTerminateAsync(ingress, scope, first, "first", firstSelection) is
                { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
                "a selection cannot terminate a root before its creator has committed");

            await using (var owner = OpenHost("root-owner"))
            {
                await owner.StartAsync(token);
                await WaitForRootAsync(store, scope.Tenant, first, 1, token);
                await WaitForRootAsync(store, scope.Tenant, second, 1, token);
                await actor.JobEntered.Task.WaitAsync(token);
                Require(await SendTerminateAsync(unauthorized, scope, first, "forbidden", firstSelection) is
                    { Outcome: Contract.AdmissionOutcome.Refused }, "ordinary ingress cannot admit operator commands");
                Require(await SendTerminateAsync(ingress, other, first, "wrong-tenant", firstSelection) is
                    { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
                    "a running selection is bound to its tenant");
                Require(await SendTerminateAsync(ingress, scope, first, "wrong-target", secondSelection) is
                    { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
                    "a running selection is bound to its exact root");
                Require(await SendTerminateAsync(ingress, scope, second, "terminate-job", secondSelection) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "the owner admits a selected Terminate while a timed job runs");
                await WaitForRootAsync(store, scope.Tenant, second, 2, token);
                await actor.JobCancelled.Task.WaitAsync(token);
                var jobEnd = (await store.ReadFirstRootAsync(scope.Tenant, second.Id, token))!;
                var jobRows = await db.Messages.AsNoTracking()
                    .Where(row => row.Target == jobEnd.Actor.Id && row.InboxScope == EfStore.RootTimedJobScope)
                    .Select(row => row.Kind).ToListAsync(token);
                Require(jobEnd is { Status: InstanceStatus.Cancelled, TerminalRevision: 2, TerminalAt: > 0, Jobs: null, NextJobGeneration: 1 } &&
                    jobRows.Count == 0, "the Cancelled checkpoint settles the outstanding timed job and its deadline " +
                    $"({jobEnd.Status}, revision {jobEnd.Revision}, terminal {jobEnd.TerminalRevision}, next generation {jobEnd.NextJobGeneration}, " +
                    $"job rows {string.Join(", ", jobRows)})");
                await owner.StopAsync(token);
                Require(owner.LastError is null, "the first owner stops cleanly");
            }

            var exact = new Contract.Target.Ref(first);
            Require(await ingress.SendAsync(scope, exact, "future", "record", options: new(DueAt: long.MaxValue), cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "an earlier future input is admitted");
            Require(await ingress.SendAsync(scope, exact, "ordinary", "record", cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "an earlier ready input is admitted");
            await store.EnableRootJobsAsync(token);
            store.BindSqliteWorkerNode();
            var fence = (await store.TakeAsync(0, "probe", Defaults.Lease, token))!;
            var due = await store.ReadRootTimedJobPublicationTimeAsync(token) + 4_000;
            Require(await SendTerminateAsync(ingress, scope, first, "terminate", firstSelection, due) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the selected command admits a future due time");
            var poll = await store.PollAsync(0, fence.Epoch, TimeSpan.FromSeconds(10), 10, token);
            Require(poll.Messages is [{ Message.Kind: "record" }], "the worker's poll horizon cannot expose an operator before its database due time");
            await store.ReleaseAsync(0, fence.Epoch, token);

            await using var replacement = OpenHost("replacement");
            await replacement.StartAsync(token);
            await WaitForRootAsync(store, scope.Tenant, first, 3, token);
            var ended = (await store.ReadFirstRootAsync(scope.Tenant, first.Id, token))!;
            Require(ended is { Status: InstanceStatus.Cancelled, State: "\"recorded\"", Revision: 3, TerminalRevision: 3 } &&
                ended.TerminalAt >= due, "Terminate follows the ready checkpoint, passes the future input, and uses database time");
            Require(await SendTerminateAsync(ingress, scope, first, "terminate", firstSelection) is
                { Outcome: Contract.AdmissionOutcome.Duplicate } &&
                await SendTerminateAsync(ingress, scope, first, "terminate-again", firstSelection) is
                { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
                "the original operator receipt stays replayable after its root ends");
            Require(await db.Messages.CountAsync(row => row.Target == ended.Actor.Id && !row.Done, token) == 1 &&
                await db.Messages.AllAsync(row => row.InboxScope != InboxKey.FoundationOccurrenceScope || !row.Done ||
                    row.Start == null && row.Payload == null && row.ReplyTo == null, token), "each settled occurrence is scrubbed and the future input remains pending");
            await replacement.StopAsync(token);
            Require(replacement.LastError is null, "the replacement owner stops cleanly");
            return 0;

            SqliteEmbeddedHostedService OpenHost(string node) =>
                SqliteEmbeddedHostedService.ForFoundation(path, node, actor, reason => throw new InvalidOperationException(reason),
                    Loops, fenceInBatch);

            async Task<Contract.ActorRef> AdmitAsync(string definition)
            {
                var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(new(OperatorKind, definition)),
                    definition, Contract.Kinds.Start, cancellationToken: token);
                Require(admitted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the creator is admitted");
                return admitted.Actor!.Value;
            }

            ValueTask<Contract.Admission> SendTerminateAsync(Contract.IIngress api, Contract.BoundScope tenant,
                Contract.ActorRef target, string operation, string selection, long? dueAt = null) =>
                api.SendAsync(tenant, new Contract.Target.Ref(target), operation, Contract.Kinds.Terminate,
                    Json.Write(new Contract.OperatorCommand(selection)), new(DueAt: dueAt), token);
        }
        finally { folder.Delete(recursive: true); }
    }

    private sealed class TerminationActor : Contract.IActorType
    {
        internal TaskCompletionSource JobEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource JobCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Type => OperatorKind;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.StartSettings is not null)
            {
                turn.State = "\"ready\"";
                if (turn.Header.TypeData == "job") turn.StartJob("job", timeout: TimeSpan.FromSeconds(30));
            }
            else if (turn.Inbox is [{ Kind: "record" }])
            {
                turn.State = "\"recorded\"";
                turn.Record(new { Step = "recorded" });
            }
            else if (turn.Inbox is [{ Kind: Contract.Kinds.Terminate }])
                turn.End(Contract.ActorStatus.Cancelled);
            else throw new InvalidOperationException("Unexpected input to the operator fixture.");
            return ValueTask.CompletedTask;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token)
        {
            JobEntered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { if (token.IsCancellationRequested) JobCancelled.TrySetResult(); }
            return null;
        }
    }
}
