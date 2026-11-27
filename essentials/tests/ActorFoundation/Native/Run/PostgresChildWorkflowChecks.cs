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
                [new Connection("dispatch", "use-child", "Completed")])))
            .Add(Wf.Workflow("cancel-parent",
                Wf.Parallel("race", 1, Wf.Dispatch("dispatch", "child", wait: true), Wf.Node<ReleaseChild>("win"))));
        var actor = new ChildFixtureActor(new WorkflowActor.WorkflowActor([new NativeExecutionModel(aliases)]));
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
        Check(await foundation.GetAsync(scope, childReference, token) is { Header.Parent: { } parentReference } &&
            parentReference == reference && (await foundation.ListAsync(scope, new(), cancellationToken: token)).Items
                .Any(item => item.Header.Ref == childReference && item.Header.Parent == reference),
            "point and list reads expose the tenant-bound exact parent reference");
        Check(await foundation.GetAsync(ContractScopes.Bind("other-tenant"), childReference, token) is null,
            "another tenant cannot see the exact child reference");
        await service.StopAsync(token);

        const string nextNode = "child-workflow-successor";
        var nextFoundation = await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(Open, actor.Type,
            (bound, target, kind) => ReferenceEquals(bound, scope) && target == childReference && kind == Contract.Kinds.Recover,
            PostgresPreviewHosting.CreateRequests(Open, nextNode, actor.Type), cancellationToken: token);
        await using var successor = StartService(nextNode, nextFoundation.Requests);
        await successor.StartAsync(token);
        try
        {
            Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(childReference), "fault-child", ChildFixtureActor.Fault,
                cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted, "the successor admitted the test child input");
            var parkedChild = await WaitForAsync(childReference, image => image.ParkAt is not null, successor);
            var awaitingAfterPark = await WaitForAsync(reference, image => image.Revision > 1, successor);
            Check(parkedChild.Revision == 2 && awaitingAfterPark is { Status: InstanceStatus.Running, Revision: 2, Children: not null },
                "the child's non-final Park notice commits without resuming Dispatch");
            var parked = (await nextFoundation.GetAsync(scope, childReference, token))!;
            Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(childReference), "recover-child", Contract.Kinds.Recover,
                Json.Write(new Contract.OperatorCommand(parked.Selection)), cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted,
                "selected Recover is admitted for the exact child Park");
            await WaitForAsync(childReference, image => image.Status == InstanceStatus.Running && image.Revision == 3, successor);
            var release = await nextFoundation.SendAsync(scope, new Contract.Target.Ref(childReference), "release-1",
                CompositeSignal.Kind, Json.Write(new CompositeSignal(wait.Id, "release", "continue")), cancellationToken: token);
            Check(release.Outcome == Contract.AdmissionOutcome.Accepted, "the successor accepted a signal to the exact child reference");
            var parent = await WaitForAsync(reference, image => image.Status != InstanceStatus.Running, successor);
            Check(parent.Status == InstanceStatus.Finished && parent.Revision == 3, "the parent's child continuation committed once");
            var state = Json.Read<NativeState>(parent.State!);
            Check(state.Variables.GetValueOrDefault("result")?.GetValue<string>() == "from-child",
                "the child's output reached the authored Flowchart continuation");
            Check(await store.ReadFirstRootAsync(scope.Tenant, childRef, token) is { Status: InstanceStatus.Finished, Revision: 4 } &&
                parent.Children is null, "the child finished once and the parent removed it from the live registry");
            Check(!await db.Messages.AnyAsync(message => !message.Done, token), "the creator and notice are consumed");
            var cancellation = await nextFoundation.SendAsync(scope, new Contract.Target.Start(
                WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start("cancel-parent"))), "cancel-parent-1", Contract.Kinds.Start,
                cancellationToken: token);
            var cancelParent = cancellation.Actor!.Value;
            var inRace = await WaitForAsync(cancelParent, image => image.Children is not null, successor);
            string? cancelChild;
            while ((cancelChild = await db.Database.SqlQuery<string>($"""
                SELECT "FoundationReference" AS "Value" FROM "Instances" WHERE "Parent" = {inRace.Actor.Id}
                """).SingleOrDefaultAsync(token)) is null) await Task.Delay(15, token);
            var win = Json.Read<NativeState>(inRace.State!).Frames.Values.Single(frame => frame.Node == "win");
            Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(cancelParent), "cancel-dispatch", CompositeSignal.Kind,
                Json.Write(new CompositeSignal(win.Id, "release", "continue")), cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted,
                "the other branch wins the parent's authored race");
            var cancelledChild = await WaitForAsync(new(cancelChild), image => image.Status == InstanceStatus.Cancelled, successor);
            var afterCancel = await WaitForAsync(cancelParent, image => image.Children is null, successor);
            Check(cancelledChild.Revision == 2 && afterCancel.Revision == 2 && afterCancel.Status == InstanceStatus.Finished,
                "the losing Dispatch cancels its exact child and the ended parent settles the final notice without a new model turn");
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

    private sealed class ChildFixtureActor(Contract.IActorType workflow) : Contract.IActorType
    {
        internal const string Fault = "fault-child";
        public string Type => workflow.Type;
        public bool Withholds(Contract.ActorHeader header) => workflow.Withholds(header);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => workflow.RestoreWaits(header, state);
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => workflow.ProtectsDeletion(header, state, now);
        public long TerminalGrowth(Contract.ActorHeader header, string? state) => workflow.TerminalGrowth(header, state);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => workflow.ExecuteJobAsync(job, token);
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.Inbox.Any(message => message.Kind == Fault)) throw new InvalidOperationException(Fault);
            return workflow.TurnAsync(turn, token);
        }
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
