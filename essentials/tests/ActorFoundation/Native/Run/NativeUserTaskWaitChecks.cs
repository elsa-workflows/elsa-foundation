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
            var qualified = new NativeExecutionModel(workflows, new NativeExecutionOptions { UserTaskOpenJobs = jobs });
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

            var checkpoint = await WaitForOpenAsync(store, tenant.Tenant, opened.Recipient, token);
            var admitted = await ingress.SendAsync(tenant, new Contract.Target.Ref(result.Actor!.Value), "ordinary-resume", NativeKinds.Resume,
                JsonSerializer.Serialize(new ResumeStimulus(opened.Recipient.TaskId)), cancellationToken: token);
            Require(admitted.Outcome == Contract.AdmissionOutcome.Accepted, "The ordinary stimulus must reach the correct actor.");
            var advanced = await WaitForOpenAsync(store, tenant.Tenant, opened.Recipient, token, checkpoint.Revision);
            Require(advanced.Status == InstanceStatus.Running && jobs.Calls == 1,
                "The general Event resume must leave the private task wait active without opening a second task.");

            await owner.StopAsync(token);
            Console.WriteLine("The committed Actor wait precedes task materialization, and an ordinary resume leaves it active.");
            return 0;
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static async Task<EfStore.RootImage> WaitForOpenAsync(EfStore store, string tenant, NativeUserTaskRecipient recipient,
        CancellationToken token, long after = 0)
    {
        var completedJob = "native.user-task.open/" + recipient.Wait;
        while (true)
        {
            var row = await store.ReadFirstRootAsync(tenant, recipient.Actor.Id, token);
            if (row is { Status: InstanceStatus.Faulted })
                throw new InvalidOperationException("The task workflow faulted before its private wait was observed.");
            if (row is { State: { } body } && row.Revision > after && body.Contains(recipient.TaskId, StringComparison.Ordinal) &&
                !body.Contains(completedJob, StringComparison.Ordinal))
                return row;
            await Task.Delay(25, token);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class OpenJobWitness(EfStore store) : INativeUserTaskOpenJobs
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        internal TaskCompletionSource<(string Tenant, NativeUserTaskRecipient Recipient, NativeUserTaskRequest Request)> Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask OpenAsync(string tenant, NativeUserTaskRecipient recipient, NativeUserTaskRequest request, CancellationToken cancellationToken)
        {
            var row = await store.ReadFirstRootAsync(tenant, recipient.Actor.Id, cancellationToken);
            Require(row?.State?.Contains(recipient.TaskId, StringComparison.Ordinal) == true &&
                row.Jobs?.Contains("native.user-task.open/" + recipient.Wait, StringComparison.Ordinal) == true,
                "The wait and this job must already be in the physical checkpoint before the task opens.");
            Interlocked.Increment(ref _calls);
            Opened.TrySetResult((tenant, recipient, request));
        }
    }

    private sealed class ProbeWait : StatefulActivity<Unit, Unit, Unit>
    {
        protected override ValueTask<Transition> ExecuteAsync(StartContext<Unit> context) => new(Suspend(default,
            new UserTaskWaitTrigger(new("Review", null, "person", null, ["Approve", "Reject"], [], [], "review-form", "7"))));

        protected override ValueTask<Transition> ResumeAsync(ResumeContext<Unit, Unit> context) => new(Complete(default));
    }
}
