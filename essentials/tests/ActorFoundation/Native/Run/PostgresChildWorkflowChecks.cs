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
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(70));
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
                Wf.Parallel("race", 1, Wf.Dispatch("dispatch", "child", wait: true), Wf.Node<ReleaseChild>("win"))))
            .Add(Wf.Workflow("cascade-parent", Wf.Parallel("cascade", null,
                Wf.Dispatch("left", "child", wait: true), Wf.Dispatch("right", "child", wait: true))))
            .Add(Wf.Workflow("nested-parent", Wf.Dispatch("dispatch", "parent", wait: true)))
            .Add(Wf.Workflow("job-parent", Wf.Dispatch("dispatch", "job-child", wait: true)))
            .Add(Wf.Workflow("job-child", Wf.Http("fetch", "https://partner.example/stuck", timeout: TimeSpan.FromMinutes(2))))
            .Add(Wf.Workflow("bulk-parent", Wf.Node<ReleaseChild>("wait")));
        var partner = new BlockingPartner();
        using var http = new HttpClient(partner);
        var actor = new ChildFixtureActor(new WorkflowActor.WorkflowActor([new NativeExecutionModel(aliases,
            new NativeExecutionOptions { Http = new NativeHttpOptions { Client = http } })]));
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
            (bound, target, kind) => ReferenceEquals(bound, scope) &&
                (kind is Contract.Kinds.Terminate or Contract.Kinds.Delete || target == childReference && kind == Contract.Kinds.Recover),
            PostgresPreviewHosting.CreateRequests(Open, nextNode, actor.Type), cancellationToken: token);
        await using var successor = StartService(nextNode, nextFoundation.Requests);
        await successor.StartAsync(token);
        try
        {
            Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(childReference), "arm-child-fault", ChildFixtureActor.ArmFault,
                cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted, "the successor admitted the child's timer trigger");
            var parkedChild = await WaitForAsync(childReference, image => image.ParkAt is not null, successor);
            var awaitingAfterPark = await WaitForAsync(reference, image => image.Revision > 1, successor);
            Check(parkedChild.Revision == 3 && awaitingAfterPark is { Status: InstanceStatus.Running, Revision: 2, Children: not null },
                "the child's S6 timer opens Park and its non-final notice commits without resuming Dispatch");
            var parked = (await nextFoundation.GetAsync(scope, childReference, token))!;
            Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(childReference), "recover-child", Contract.Kinds.Recover,
                Json.Write(new Contract.OperatorCommand(parked.Selection)), cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted,
                "selected Recover is admitted for the exact child Park");
            await WaitForAsync(childReference, image => image.Status == InstanceStatus.Running && image.Revision == 4, successor);
            var release = await nextFoundation.SendAsync(scope, new Contract.Target.Ref(childReference), "release-1",
                CompositeSignal.Kind, Json.Write(new CompositeSignal(wait.Id, "release", "continue")), cancellationToken: token);
            Check(release.Outcome == Contract.AdmissionOutcome.Accepted, "the successor accepted a signal to the exact child reference");
            var parent = await WaitForAsync(reference, image => image.Status != InstanceStatus.Running, successor);
            Check(parent.Status == InstanceStatus.Finished && parent.Revision == 3, "the parent's child continuation committed once");
            var state = Json.Read<NativeState>(parent.State!);
            Check(state.Variables.GetValueOrDefault("result")?.GetValue<string>() == "from-child",
                "the child's output reached the authored Flowchart continuation");
            Check(await store.ReadFirstRootAsync(scope.Tenant, childRef, token) is { Status: InstanceStatus.Finished, Revision: 5 } &&
                parent.Children is null, "the child finished once and the parent removed it from the live registry");
            Check(!await db.Messages.AnyAsync(message => !message.Done, token), "the creator and notice are consumed");
            var endedChild = (await nextFoundation.GetAsync(scope, childReference, token))!;
            Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(childReference), "delete-child", Contract.Kinds.Delete,
                Json.Write(new Contract.OperatorCommand(endedChild.Selection)), cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted,
                "the exact terminal selection admits attached child retirement");
            while (await nextFoundation.GetAsync(scope, childReference, token) is not null) await Task.Delay(15, token);
            Check(await nextFoundation.GetAsync(scope, reference, token) is { Header.Status: Contract.ActorStatus.Finished },
                "retiring the attached child preserves the parent's committed continuation");
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

            await CheckParkedAttachedTerminationAsync("authored", fromParent: true);
            await CheckParkedAttachedTerminationAsync("selected", fromParent: false);

            var cascadeAdmitted = await nextFoundation.SendAsync(scope, new Contract.Target.Start(
                WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start("cascade-parent"))), "cascade-parent-1", Contract.Kinds.Start,
                cancellationToken: token);
            var cascadeReference = cascadeAdmitted.Actor!.Value;
            var cascadeParent = await WaitForAsync(cascadeReference, image => ChildRegistry.Read(image.Children).Count == 2, successor);
            List<string> children;
            do
            {
                children = await db.Database.SqlQuery<string>($"""
                    SELECT "FoundationReference" AS "Value" FROM "Instances" WHERE "Parent" = {cascadeParent.Actor.Id}
                    """).ToListAsync(token);
                if (children.Count != 2) await Task.Delay(15, token);
            } while (children.Count != 2);
            var parkedCascadeChild = new Contract.ActorRef(children[0]);
            Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(parkedCascadeChild), "park-for-cascade", ChildFixtureActor.Fault,
                cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted, "the cascade fixture admits a fault on an attached child");
            await WaitForAsync(parkedCascadeChild, image => image.ParkAt is not null, successor);
            var selected = (await nextFoundation.GetAsync(scope, cascadeReference, token))!.Selection;
            Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(cascadeReference), "terminate-parent", Contract.Kinds.Terminate,
                Json.Write(new Contract.OperatorCommand(selected)), cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted,
                "the operator terminates the exact parent while both children are waiting");
            var terminated = await WaitForAsync(cascadeReference, image => image.Status == InstanceStatus.Cancelled, successor);
            foreach (var childToCancel in children)
                await WaitForAsync(new(childToCancel), image => image is { Status: InstanceStatus.Cancelled, ParkAt: null }, successor);
            Check(terminated.Children is null, "the committed parent Terminate cascades to both exact children, including the parked child");
            var bulkAdmitted = await nextFoundation.SendAsync(scope, new Contract.Target.Start(
                WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start("bulk-parent"))), "bulk-parent-1", Contract.Kinds.Start,
                cancellationToken: token);
            var bulkParent = bulkAdmitted.Actor!.Value;
            await WaitForAsync(bulkParent, image => image.Status == InstanceStatus.Running, successor);
            Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(bulkParent), "bulk-spawn", ChildFixtureActor.Spawn,
                cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted, "the bounded cascade fixture starts one registry larger than a page");
            var registry = await WaitForAsync(bulkParent, image => ChildRegistry.Read(image.Children).Count > CascadeProtocol.PageLimit, successor);
            var last = ChildRegistry.Read(registry.Children)[^1].Id;
            var lastReference = await db.Database.SqlQuery<string>($"""
                SELECT "Reference" AS "Value" FROM "FoundationRootBirths" WHERE "Actor" = {last}
                """).SingleAsync(token);
            var bulkSelection = (await nextFoundation.GetAsync(scope, bulkParent, token))!.Selection;
            Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(bulkParent), "bulk-terminate", Contract.Kinds.Terminate,
                Json.Write(new Contract.OperatorCommand(bulkSelection)), cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted,
                "the operator admits the large parent's Terminate");
            await WaitForAsync(new(lastReference), image => image.Status == InstanceStatus.Cancelled, successor);
            Check(!await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "FoundationRootChildCascades" WHERE "Reference" = {bulkParent.Id}) AS "Value"
                """).SingleAsync(token), "the durable second cascade page reaches its child and clears the ledger");
            Check(service.LastError is null && successor.LastError is null, "both owners stopped and ran without fault");
        }
        finally { await successor.StopAsync(CancellationToken.None); }

        PostgresPreviewHostedService StartService(string name, PostgresDirectRequests? requests) =>
            new(Open, name, actor, message => throw new InvalidOperationException(message),
                new() { PollInterval = TimeSpan.FromMilliseconds(15) }, requests: requests);

        async Task CheckParkedAttachedTerminationAsync(string lane, bool fromParent)
        {
            var added = await nextFoundation.SendAsync(scope, new Contract.Target.Start(
                WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start(fromParent ? "nested-parent" : "job-parent"))),
                lane + "-parent", Contract.Kinds.Start, cancellationToken: token);
            var parent = added.Actor!.Value;
            var waitingParent = await WaitForAsync(parent, image => image.Children is not null, successor);
            var childLife = ChildRegistry.Read(waitingParent.Children).Single();
            var childId = await ReferenceAsync(childLife);
            var attached = new Contract.ActorRef(childId);
            var runningChild = await WaitForAsync(attached, image => image.Status == InstanceStatus.Running &&
                (fromParent ? image.Children is not null : image.Jobs is not null), successor);
            var descendant = fromParent ? await ReferenceAsync(ChildRegistry.Read(runningChild.Children).Single()) : null;
            if (descendant is not null)
                await WaitForAsync(new(descendant), image => image.Status == InstanceStatus.Running, successor);
            else await partner.Entered.Task.WaitAsync(token);
            Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(attached), lane + "-park", ChildFixtureActor.Fault,
                cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted, $"the {lane} child fault is admitted");
            var parkedChild = await WaitForAsync(attached, image => image.ParkAt is not null, successor);
            var view = (await nextFoundation.GetAsync(scope, attached, token))!;
            if (fromParent)
            {
                Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(attached), "attached-park-delete", Contract.Kinds.Delete,
                    Json.Write(new Contract.OperatorCommand(view.Selection)), cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted,
                    "the selected parked Delete reaches the attached child for durable refusal");
                await WaitForAsync(attached, image => image.ParkAt == parkedChild.ParkAt && image.Revision == parkedChild.Revision + 1, successor);
                Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(attached), "attached-park-then-delete", Contract.Kinds.Terminate,
                    Json.Write(new Contract.OperatorCommand(view.Selection, ThenDelete: true, ExpectedTarget: attached)),
                    cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted,
                    "the selected parked compound reaches the attached child for durable refusal");
                await WaitForAsync(attached, image => image.ParkAt == parkedChild.ParkAt && image.Revision == parkedChild.Revision + 2, successor);
                var refusals = await db.Database.SqlQuery<string>($"""
                    SELECT "Entries" AS "Value" FROM "Journal"
                    WHERE "Instance" = {parkedChild.Actor.Id} AND "Revision" > {parkedChild.Revision} ORDER BY "Revision"
                    """).ToArrayAsync(token);
                Check(refusals.Length == 2 && refusals.All(entry => entry.Contains("\"Reason\":\"outstanding\"", StringComparison.Ordinal)),
                    "unsupported parked Delete and compound leave the exact Park held with durable refusal entries");
                Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(parent), lane + "-terminate", ChildFixtureActor.TerminateChild,
                    childId, cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted,
                    "the exact parent receives an authored request to use the foundation Terminate control");
            }
            else
                Check((await nextFoundation.SendAsync(scope, new Contract.Target.Ref(attached), lane + "-terminate", Contract.Kinds.Terminate,
                    Json.Write(new Contract.OperatorCommand(view.Selection)), cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted,
                    "the selected Terminate admits an exact parked attached child");
            await WaitForAsync(attached, image => image is { Status: InstanceStatus.Cancelled, ParkAt: null, Jobs: null }, successor);
            await WaitForAsync(parent, image => image.Children is null, successor);
            Check(true, $"the {lane} Terminate closes Park and the committed final notice removes the child from its parent");
            if (descendant is not null)
            {
                await WaitForAsync(new(descendant), image => image.Status == InstanceStatus.Cancelled, successor);
                Check(true, "terminating the parked middle child also cancels its exact live descendant");
            }
            else
            {
                await partner.Cancelled.Task.WaitAsync(token);
                Check(!await db.Messages.AnyAsync(row => row.Target == parkedChild.Actor.Id && row.InboxScope == EfStore.RootTimedJobScope, token),
                    "terminating the parked attached child releases its pinned timed job and cancels its entered callback");
            }
        }

        Task<string> ReferenceAsync(CreationAddress life) => db.Database.SqlQuery<string>($"""
            SELECT "Reference" AS "Value" FROM "FoundationRootBirths" WHERE "Actor" = {life.Id}
            """).SingleAsync(token);

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
        internal const string ArmFault = "arm-child-fault";
        internal const string Fault = "fault-child";
        internal const string Spawn = "bulk-spawn";
        internal const string TerminateChild = "terminate-child";
        public string Type => workflow.Type;
        public bool Withholds(Contract.ActorHeader header) => workflow.Withholds(header);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => workflow.RestoreWaits(header, state);
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => workflow.ProtectsDeletion(header, state, now);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => workflow.TerminalGrowth(header, state);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => workflow.ExecuteJobAsync(job, token);
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.Inbox.Any(message => message.Kind == Fault)) throw new InvalidOperationException(Fault);
            if (turn.Inbox.Any(message => message.Kind == ArmFault))
            {
                turn.Send(turn.Header.Ref, Fault, dueAt: turn.Now + 50);
                return ValueTask.CompletedTask;
            }
            if (turn.Inbox.FirstOrDefault(message => message.Kind == TerminateChild) is { Payload: { } child })
            {
                turn.Send(new(child), Contract.Kinds.Terminate);
                return ValueTask.CompletedTask;
            }
            if (!turn.Inbox.Any(message => message.Kind == Spawn)) return workflow.TurnAsync(turn, token);
            for (var i = 0; i <= CascadeProtocol.PageLimit; i++)
                turn.Start(WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start("child")));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingPartner : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { if (cancellationToken.IsCancellationRequested) Cancelled.TrySetResult(); }
            throw new InvalidOperationException("The parked child fixture's request should leave only by cancellation.");
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
