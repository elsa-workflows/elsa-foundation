using System.Text.Json;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Kernel.Foundation.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class NativeUserTaskWaitChecks
{
    private const string Receipt = "accepted-response-7";
    internal static async Task<int> RunAsync(bool park = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        var folder = Directory.CreateTempSubdirectory("actor-user-task-open");
        try
        {
            var database = Path.Combine(folder.FullName, "actor.db");
            var connection = new SqliteConnectionStringBuilder { DataSource = database, ForeignKeys = true, Pooling = false };
            var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(connection.ToString()).Options;
            var definitions = new SqliteAdmissionDefinitions();
            KernelDb Open() => KernelDb.ForSqliteRootRuntime(options, definitions);
            var store = new EfStore(Open, postgres: false);
            var jobs = new OpenJobWitness(store);
            var workflows = new WorkflowRegistry().Add(Wf.Workflow("review", Wf.Node<CreateUserTask>("approval",
                ("Title", "Review"), ("AssigneeId", "person"), ("FormId", "review-form"), ("FormVersion", "7"))));
            var ordinary = new NativeExecutionModel(workflows);
            var qualified = new NativeExecutionModel(workflows, new NativeExecutionOptions { UserTaskJobs = jobs });
            Require(qualified.StateVersion == ordinary.StateVersion + 1, "The durable task wait needs its own Native state version.");
            Require(qualified.UpgradeState(JsonSerializer.Serialize(new NativeState()), ordinary.StateVersion) is NativeState,
                "A valid earlier checkpoint remains readable on the qualified model.");
            var actor = new WorkflowActor.WorkflowActor([qualified]);
            var foundation = new SqliteEmbeddedFoundation(database, "task-open-owner", actor.Type);
            await foundation.InstallShippedAsync(token);
            await using var owner = foundation.CreateHostedService(park ? new ParksOnInput(actor) : actor,
                error => throw new InvalidOperationException(error));
            await owner.StartAsync(token);
            var ingress = await foundation.OpenIngressAsync(token);
            var tenant = EmbeddedTenant.Bind("user-task-tenant");
            var result = await ingress.SendAsync(tenant, new Contract.Target.Start(WorkflowActor.WorkflowActor.Start(
                NativeExecutionModel.Start("review"))), "open-review", Contract.Kinds.Start, cancellationToken: token);
            Require(result.Outcome == Contract.AdmissionOutcome.Accepted && result.Actor is not null, "The private profile must admit the test workflow.");

            var opened = await jobs.Opened.Task.WaitAsync(token);
            Require(opened.Tenant == tenant.Tenant && opened.Recipient.Actor == result.Actor,
                "The durable open job must carry the admitted actor and its trusted tenant.");
            Require(opened.Request.FormId == "review-form" && opened.Request.FormVersion == "7" && opened.Request.Actions.SequenceEqual(["Approve", "Reject"]),
                "The job must preserve the frozen form selection and actions.");

            if (park)
            {
                await QualifyParkAsync();
                await owner.StopAsync(token);
                Console.WriteLine("Recover preserves the exact committed task wait across a physical SQLite Park.");
                return 0;
            }

            async Task QualifyParkAsync()
            {
                jobs.ReleaseOpen.TrySetResult();
                var ready = await ObserveAsync(store, tenant.Tenant, opened.Recipient.Actor, row =>
                    NativeExecutionModel.InspectUserTask(row.State, opened.Recipient, "approval").IsWaiting &&
                    row.Jobs?.Contains("native.user-task.open/" + opened.Recipient.Wait, StringComparison.Ordinal) != true &&
                    row.State?.Contains("native.user-task.open/" + opened.Recipient.Wait, StringComparison.Ordinal) != true, token);
                Require(await ingress.SendAsync(tenant, new Contract.Target.Ref(opened.Recipient.Actor), "park-task", "force-test-park",
                    cancellationToken: token) is { Outcome: Contract.AdmissionOutcome.Accepted }, "The faulting input must reach the owner.");
                var parked = await ObserveAsync(store, tenant.Tenant, opened.Recipient.Actor, row => row.ParkAt is not null, token);
                var observed = await foundation.ReadUserTaskCheckpointAsync(tenant, opened.Recipient.Actor, token);
                Require(parked.State == ready.State && observed is { Parked: true } &&
                    NativeExecutionModel.InspectUserTask(observed.State, opened.Recipient, "approval").IsWaiting,
                    "A recoverable Park retains its committed task wait and remains distinguishable from terminal closure.");

                var active = await foundation.CreateReads().GetAsync(tenant, opened.Recipient.Actor, token);
                Require(active is { Park: not null, Selection: not null }, "The physical Park must have an exact operator selection.");
                var recovery = await foundation.OpenIngressWithOperatorsAsync((scope, target, kind) =>
                    scope.Tenant == tenant.Tenant && target == opened.Recipient.Actor && kind == Contract.Kinds.Recover, token);
                Require(await recovery.SendAsync(tenant, new Contract.Target.Ref(opened.Recipient.Actor), "recover-task", Contract.Kinds.Recover,
                    JsonSerializer.Serialize(new Contract.OperatorCommand(active!.Selection)), cancellationToken: token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "Recover must accept the current Park selection.");
                await ObserveAsync(store, tenant.Tenant, opened.Recipient.Actor, row => row.Revision > parked.Revision &&
                    row is { Status: InstanceStatus.Running, ParkAt: null }, token);
                await ReplyAsync("recovered-response", opened.Recipient);
                var completed = await ObserveAsync(store, tenant.Tenant, opened.Recipient.Actor, row => row.Status == InstanceStatus.Finished, token);
                Require(NativeExecutionModel.InspectUserTask(completed.State, opened.Recipient, "approval").ConsumedCompletionId == Receipt,
                    "The original task must complete from its exact response after Recover.");
            }

            var checkpoint = await ObserveAsync(store, tenant.Tenant, opened.Recipient.Actor,
                row => row.State?.Contains(opened.Recipient.TaskId, StringComparison.Ordinal) == true, token);
            var admitted = await ingress.SendAsync(tenant, new Contract.Target.Ref(result.Actor!.Value), "ordinary-resume", NativeKinds.Resume,
                JsonSerializer.Serialize(new ResumeStimulus(opened.Recipient.TaskId)), cancellationToken: token);
            Require(admitted.Outcome == Contract.AdmissionOutcome.Accepted, "The ordinary stimulus must reach the correct actor.");
            var advanced = await ObserveAsync(store, tenant.Tenant, opened.Recipient.Actor,
                row => row.Revision > checkpoint.Revision, token);
            Require(advanced.Status == InstanceStatus.Running && jobs.OpenCalls == 1,
                "The general Event resume must leave the private task wait active without opening a second task.");

            await ReplyAsync("wrong-frame", opened.Recipient with { Frame = "other-frame" });
            var mismatched = await ObserveAsync(store, tenant.Tenant, opened.Recipient.Actor,
                row => row.Revision > advanced.Revision, token);
            Require(mismatched.State?.Contains(Receipt, StringComparison.Ordinal) == false,
                "The actor must ignore an accepted response naming a different frame.");
            await ReplyAsync("early-response", opened.Recipient);
            var pending = await ObserveAsync(store, tenant.Tenant, opened.Recipient.Actor,
                row => row.State?.Contains(Receipt, StringComparison.Ordinal) == true, token);
            Require(pending.Jobs?.Contains("native.user-task.open/" + opened.Recipient.Wait, StringComparison.Ordinal) == true &&
                jobs.SettleCalls == 0, "The receipt must commit even before the open job has reported back.");

            await ReplyAsync("duplicate-response", opened.Recipient);
            jobs.ReleaseOpen.TrySetResult();
            await jobs.Settled.Task.WaitAsync(token);
            var final = await ObserveAsync(store, tenant.Tenant, opened.Recipient.Actor, row => row.Status == InstanceStatus.Finished, token);
            Require(jobs.SettleCalls == 2, "The exact accepted response must survive one uncertain settlement result.");
            Require(NativeExecutionModel.InspectUserTask(final.State, opened.Recipient, "approval").ConsumedCompletionId == Receipt,
                "The exact event receipt must be committed with activity completion.");

            var operators = await foundation.OpenIngressWithOperatorsAsync((scope, target, kind) =>
                scope.Tenant == tenant.Tenant && target == opened.Recipient.Actor && kind == Contract.Kinds.Delete, token);
            Require(await DeleteAsync("before-journal-commit") is { Error: "protected" },
                "The selected terminal checkpoint must survive while the task completion is unacknowledged.");
            jobs.JournalAcknowledged = true;
            Require(await DeleteAsync("after-journal-commit") is { Item: "deleted" },
                "The original terminal selection can retire once the journal durably records the exact completion.");

            async Task<Contract.Reply> DeleteAsync(string operation)
            {
                var view = await foundation.CreateReads().GetAsync(tenant, opened.Recipient.Actor, token);
                Require(view?.Selection is not null, "The terminal checkpoint needs its committed selection.");
                return await operators.RequestAsync(tenant, new Contract.Target.Ref(opened.Recipient.Actor), operation, Contract.Kinds.Delete,
                    JsonSerializer.Serialize(new Contract.OperatorCommand(view!.Selection, ExpectedTarget: opened.Recipient.Actor)), null,
                    new(null, await foundation.ReadClockAsync(token) + 10_000), token);
            }

            await owner.StopAsync(token);
            Console.WriteLine("The committed Actor wait precedes task materialization, rejects ordinary resume and a mismatched frame, and settles an early response once.");

            async Task ReplyAsync(string messageId, NativeUserTaskRecipient recipient)
            {
                var reply = await ingress.SendAsync(tenant, new Contract.Target.Ref(result.Actor!.Value), messageId, NativeKinds.UserTaskAccepted,
                    JsonSerializer.Serialize(new NativeUserTaskAccepted(recipient, Receipt)), cancellationToken: token);
                Require(reply.Outcome == Contract.AdmissionOutcome.Accepted, "The private stimulus must reach the correct actor.");
            }
            return 0;
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static async Task<EfStore.RootImage> ObserveAsync(EfStore store, string tenant, Contract.ActorRef actor,
        Func<EfStore.RootImage, bool> condition, CancellationToken token)
    {
        while (true)
        {
            var row = await store.ReadFirstRootAsync(tenant, actor.Id, token);
            if (row is { Status: InstanceStatus.Faulted, ParkAt: null })
                throw new InvalidOperationException("The task workflow faulted before the expected checkpoint: " + row.Fault);
            if (row is not null && condition(row)) return row;
            await Task.Delay(25, token);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class OpenJobWitness(EfStore store) : INativeUserTaskJobs
    {
        private int _openCalls;
        private int _settleCalls;
        internal bool JournalAcknowledged { get; set; }
        internal int OpenCalls => Volatile.Read(ref _openCalls);
        internal int SettleCalls => Volatile.Read(ref _settleCalls);
        internal TaskCompletionSource ReleaseOpen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<(string Tenant, NativeUserTaskRecipient Recipient, NativeUserTaskRequest Request)> Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<NativeUserTaskResponse?> SettleAsync(string tenant, NativeUserTaskRecipient recipient, string completionId,
            CancellationToken cancellationToken)
        {
            var row = await store.ReadFirstRootAsync(tenant, recipient.Actor.Id, cancellationToken);
            Require(row?.State?.Contains(completionId, StringComparison.Ordinal) == true &&
                row.Jobs?.Contains("native.user-task.settle/" + recipient.Wait, StringComparison.Ordinal) == true,
                "The exact accepted response and settlement job must already be committed.");
            if (Interlocked.Increment(ref _settleCalls) == 1)
                throw new InvalidOperationException("The settlement result was interrupted.");
            Settled.TrySetResult();
            return new(recipient.TaskId, completionId, "Approve", []);
        }

        public ValueTask<bool> CanReleaseCompletionAsync(string tenant, NativeUserTaskRecipient recipient, string nodeId,
            string completionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(JournalAcknowledged && nodeId == "approval" && completionId == Receipt);

        public async ValueTask<bool> OpenAsync(string tenant, NativeUserTaskRecipient recipient, string nodeId, NativeUserTaskRequest request,
            CancellationToken cancellationToken)
        {
            Require(nodeId == "approval" && recipient.TaskId.Length == 64, "The journal must receive a stable task ID and the authored node.");
            var row = await store.ReadFirstRootAsync(tenant, recipient.Actor.Id, cancellationToken);
            Require(row?.State?.Contains(recipient.TaskId, StringComparison.Ordinal) == true &&
                row.Jobs?.Contains("native.user-task.open/" + recipient.Wait, StringComparison.Ordinal) == true,
                "The wait and this job must already be in the physical checkpoint before the task opens.");
            Interlocked.Increment(ref _openCalls);
            Opened.TrySetResult((tenant, recipient, request));
            await ReleaseOpen.Task.WaitAsync(cancellationToken);
            return true;
        }
    }

    private sealed class ParksOnInput(WorkflowActor.WorkflowActor actor) : Contract.IActorType
    {
        public string Type => actor.Type;
        public bool Withholds(Contract.ActorHeader header) => actor.Withholds(header);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => actor.RestoreWaits(header, state);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => actor.TerminalGrowth(header, state);
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => actor.ProtectsDeletion(header, state, now);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => actor.ExecuteJobAsync(job, cancellationToken);
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken) =>
            turn.Inbox.Any(message => message.Kind == "force-test-park")
                ? ValueTask.FromException(new InvalidOperationException("Task Park witness"))
                : actor.TurnAsync(turn, cancellationToken);
    }
}
