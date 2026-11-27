using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresChildWorkflowChecks
{
    internal static async Task<int> RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        const string node = "child-workflow-check";
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(
            Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
            throw new InvalidOperationException("Supply an empty dedicated PostgreSQL fixture.")).Options;
        KernelDb Open() => KernelDb.ForPostgresRootChildrenQualification(options);
        await using (var empty = Open())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(empty, token), "the fixture is empty");

        var store = new EfStore(Open, postgres: true);
        await store.CreateAsync(2);
        await store.InstallPreviewRootSchemaAsync(token);

        var aliases = new WorkflowRegistry()
            .Add(Wf.Workflow("child", Wf.Sequence("child-sequence", Wf.Node<ReleaseChild>("gate"),
                Wf.SetOutput("return", "child-value", "from-child"))))
            .Add(Wf.Workflow("parent", Wf.Flowchart("chart",
                [Wf.Dispatch("dispatch", "child", wait: true),
                 Wf.Set("use-child", "result", Binding.Result("dispatch", "Output.child-value"))],
                [new Connection("dispatch", "use-child", "Completed")])));
        var actor = new WorkflowActor.WorkflowActor([new NativeExecutionModel(aliases)]);
        var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, actor.Type,
            PostgresPreviewHosting.CreateRequests(Open, node, actor.Type), cancellationToken: token);
        await using var service = StartService(node, foundation.Requests);
        var scope = EmbeddedTenant.Bind("workflow-children");
        await service.StartAsync(token);
        var result = await foundation.SendAsync(scope, new Contract.Target.Start(
            WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start("parent"))), "parent-1", Contract.Kinds.Start, cancellationToken: token);
        Check(result.Outcome == Contract.AdmissionOutcome.Accepted && result.Actor is not null, "the authored parent was admitted");
        var reference = result.Actor!.Value;
        var waiting = await WaitForAsync(reference, image => image.Children is not null, service);
        await using var db = Open();
        string? childRef;
        while ((childRef = await db.Database.SqlQuery<string>($"""
            SELECT "FoundationReference" AS "Value" FROM "Instances"
            WHERE "Parent" = {waiting.Actor.Id} AND "FoundationTenant" = {scope.Tenant}
            """).SingleOrDefaultAsync(token)) is null)
        {
            if (service.LastError is { } failure) throw new InvalidOperationException("The owner failed.", failure);
            await Task.Delay(15, token);
        }
        var childReference = new Contract.ActorRef(childRef);
        var child = await WaitForAsync(childReference, image => image.Status == InstanceStatus.Running, service);
        var wait = Json.Read<NativeState>(child.State!).Frames.Values.Single(frame => frame.Node == "gate");
        Check(waiting.Revision == 1 && child.Revision == 1 && child.Parent == waiting.Actor.Id,
            "the parent waits at its committed first revision and the child has its own committed identity");
        Check(await foundation.GetAsync(ContractScopes.Bind("other-tenant"), childReference, token) is null,
            "another tenant cannot see the exact child reference");
        await service.StopAsync(token);

        const string nextNode = "child-workflow-successor";
        var nextFoundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, actor.Type,
            PostgresPreviewHosting.CreateRequests(Open, nextNode, actor.Type), cancellationToken: token);
        await using var successor = StartService(nextNode, nextFoundation.Requests);
        await successor.StartAsync(token);
        try
        {
            var release = await nextFoundation.SendAsync(scope, new Contract.Target.Ref(childReference), "release-1",
                CompositeSignal.Kind, Json.Write(new CompositeSignal(wait.Id, "release", "continue")), cancellationToken: token);
            Check(release.Outcome == Contract.AdmissionOutcome.Accepted, "the successor accepted a signal to the exact child reference");
            var parent = await WaitForAsync(reference, image => image.Status != InstanceStatus.Running, successor);
            Check(parent.Status == InstanceStatus.Finished && parent.Revision == 2, "the parent's child continuation committed once");
            var state = Json.Read<NativeState>(parent.State!);
            Check(state.Variables.GetValueOrDefault("result")?.GetValue<string>() == "from-child",
                "the child's output reached the authored Flowchart continuation");
            Check(await store.ReadFirstRootAsync(scope.Tenant, childRef, token) is { Status: InstanceStatus.Finished, Revision: 2 } &&
                parent.Children is null, "the child finished once and the parent removed it from the live registry");
            Check(!await db.Messages.AnyAsync(message => !message.Done, token), "the creator and notice are consumed");
            Check(service.LastError is null && successor.LastError is null, "both owners stopped and ran without fault");
        }
        finally { await successor.StopAsync(CancellationToken.None); }

        PostgresPreviewHostedService StartService(string name, PostgresDirectRequests? requests) =>
            new(Open, name, actor, message => throw new InvalidOperationException(message),
                new() { PollInterval = TimeSpan.FromMilliseconds(15) }, requests: requests);

        async Task<EfStore.RootImage> WaitForAsync(Contract.ActorRef target, Func<EfStore.RootImage, bool> ready, PostgresPreviewHostedService owner)
        {
            while (true)
            {
                if (await store.ReadFirstRootAsync(scope.Tenant, target.Id, token) is { } image && ready(image)) return image;
                if (owner.LastError is { } failure) throw new InvalidOperationException("The owner failed.", failure);
                await Task.Delay(15, token);
            }
        }
        return 0;
    }

    public sealed class ReleaseChild : CompositeActivity, ISignalledComposite
    {
        protected override void Execute(CompositeContext context) => context.SetState(false);
        public bool CanAccept(CompositeContext context) => true;
        public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
        {
            if (signal.Name == "release" && signal.Value == "continue") context.Complete();
        }
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("The signal gate does not schedule activities.");
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
