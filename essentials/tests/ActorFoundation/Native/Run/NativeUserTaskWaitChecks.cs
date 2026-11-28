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
    internal static async Task<int> RunAsync()
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
            KernelDb Open() => KernelDb.ForSqliteRootRuntime(options, definitions, StoreFormat.RootUserTaskWaitsQualification);
            var store = new EfStore(Open, postgres: false);
            var jobs = new OpenJobWitness(store);
            var workflows = new WorkflowRegistry().Add(Wf.Workflow("review", Wf.Node<ProbeWait>("approval")));
            var ordinary = new NativeExecutionModel(workflows);
            var qualified = new NativeExecutionModel(workflows, new NativeExecutionOptions { UserTaskJobs = jobs });
            Require(qualified.StateVersion == ordinary.StateVersion + 1, "The durable task wait needs its own Native state version.");
            Require(qualified.UpgradeState(JsonSerializer.Serialize(new NativeState()), ordinary.StateVersion) is NativeState,
                "A valid earlier checkpoint remains readable on the qualified model.");
            var actor = new WorkflowActor.WorkflowActor([qualified]);
            var foundation = SqliteEmbeddedFoundation.ForUserTaskWaitsQualification(database, "task-open-owner", actor.Type);
            await foundation.InstallShippedAsync(token);
            await using var owner = foundation.CreateHostedService(actor, error => throw new InvalidOperationException(error));
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
            await ObserveAsync(store, tenant.Tenant, opened.Recipient.Actor, row => row.Status == InstanceStatus.Finished, token);
            Require(jobs.SettleCalls == 1, "The exact accepted response must finish the activity once.");

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
            if (row is { Status: InstanceStatus.Faulted })
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
            Interlocked.Increment(ref _settleCalls);
            Settled.TrySetResult();
            return new(recipient.TaskId, completionId, "Approve", []);
        }

        public async ValueTask OpenAsync(string tenant, NativeUserTaskRecipient recipient, string nodeId, NativeUserTaskRequest request,
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
        }
    }

    private sealed class ProbeWait : StatefulActivity<NativeUserTaskResponse, Unit, NativeUserTaskResponse>
    {
        protected override ValueTask<Transition> ExecuteAsync(StartContext<NativeUserTaskResponse> context) => new(Suspend(default,
            new UserTaskWaitTrigger(new("Review", null, "person", null, ["Approve", "Reject"], [], [], "review-form", "7"))));

        protected override ValueTask<Transition> ResumeAsync(ResumeContext<Unit, NativeUserTaskResponse> context) =>
            context.Trigger is { CompletionId: Receipt, Action: "Approve" } response
                ? new(Complete(response, response.Action))
                : new(Fault("UnexpectedResponse", "The activity resumed without the exact accepted response."));
    }
}
