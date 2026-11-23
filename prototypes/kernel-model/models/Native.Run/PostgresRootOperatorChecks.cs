using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresRootOperatorChecks
{
    internal const string SuspendId = "pause/\0once";

    internal static async Task<int> RunAsync(string[]? previous, bool owned = true, bool inBatch = false)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use a fresh dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => new(options);
        EfStore NewStore() => new(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        var store = NewStore();
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        if (previous is null)
        {
            await store.CreateAsync(1);
            await admission.InstallAsync(ct);
        }
        var journal = new PostgresRootJournalReader(store, await admission.OpenJournalCursorAsync(ct), 8);
        var blue = FixedTenantScope.Bind("blue");
        var basic = new PostgresPreviewFoundation(admission, store, journal, WorkflowActor.WorkflowActor.ActorType);
        var operatorApi = new PostgresPreviewFoundation(admission, store, journal, WorkflowActor.WorkflowActor.ActorType,
            (scope, kind) => ReferenceEquals(scope, blue) && kind is Contract.Kinds.Suspend or Contract.Kinds.Resume);
        var runtime = new WorkflowRuntime(basic, basic);
        if (previous is null)
        {
            var a = await Start(blue, "alpha");
            var b = await Start(blue, "bravo");
            var c = new Contract.ActorRef(await PostgresMultiRootClient.CallSecondaryAsync(ct, "operator-start"));
            await using var clock = Open();
            var now = await clock.Database.SqlQueryRaw<long>("""SELECT floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Value" """).SingleAsync(ct);
            Check((await Send(blue, b, "pending-root", Contract.Kinds.Suspend, new(DueAt: now + 3_600_000))).Outcome == Contract.AdmissionOutcome.Accepted &&
                await admission.ReadDueRootOperatorAsync("blue", b.Id, ct) is null, "a pending creator admits the command and a due read excludes its future deadline");
            var completed = (await runtime.StartAsync(blue, "completed", NativeExecutionModel.Start("terminal"), cancellationToken: ct)).Actor!.Value;
            var host = await PostgresNativeRootOwner.PreparePartitionAsync(store, "operator-preview", 0,
                new WorkflowRegistry().Add(DirectApprovalWorkflow.Build()).Add(Wf.Workflow("terminal", Wf.Node<PostgresFlowchartPreview.Approval>("approval"))), 8, ct);
            await using (var owner = await host.TryActivateAsync(ct) ?? throw new InvalidOperationException("Expected a new partition owner."))
            {
                while (await basic.GetAsync(blue, a, ct) is null || await basic.GetAsync(blue, b, ct) is null || await store.ReadFirstRootAsync(PostgresMultiRootClient.Secondary, c.Id, ct) is null ||
                    await basic.GetAsync(blue, completed, ct) is null) await RunPass();
                var ending = (await store.ReadFirstRootAsync("blue", completed.Id, ct))!;
                var frame = Json.Read<NativeState>(ending.State!).Frames.Values.Single().Id;
                await basic.SendAsync(blue, new Contract.Target.Ref(completed), "finish", CompositeSignal.Kind, Json.Write(new CompositeSignal(frame, "approve", "done")), cancellationToken: ct);
                while ((await basic.GetAsync(blue, completed, ct))?.Header.Status != Contract.ActorStatus.Finished) await RunPass();

                async Task RunPass()
                {
                    await owner.WokenAsync(null, ct);
                    await owner.RunAsync(ct);
                    if (!((Worker)owner).Owns) throw new InvalidOperationException("The workflow owner lost its fence.");
                    await Task.Delay(10, ct);
                }
            }
            Check((await Send(blue, completed, "terminal-op", Contract.Kinds.Suspend)).Outcome == Contract.AdmissionOutcome.Refused,
                "a new suspension refuses a finished workflow");
            Check((await basic.SendAsync(blue, new Contract.Target.Ref(a), SuspendId, Contract.Kinds.Suspend, cancellationToken: ct)).Outcome ==
                Contract.AdmissionOutcome.Refused, "the ordinary host has no operator grant");
            var suspendOnly = new PostgresPreviewFoundation(admission, store, journal, WorkflowActor.WorkflowActor.ActorType,
                (scope, kind) => scope.Tenant == blue.Tenant && kind == Contract.Kinds.Suspend);
            Check((await suspendOnly.SendAsync(blue, new Contract.Target.Ref(a), "only-suspend", Contract.Kinds.Resume, cancellationToken: ct)).Outcome ==
                Contract.AdmissionOutcome.Refused, "a suspension grant cannot authorize Resume");
            Check((await Send(blue, c, SuspendId, Contract.Kinds.Suspend)).Outcome == Contract.AdmissionOutcome.Refused,
                "a grant resolves references only within the trusted tenant");
            Check((await operatorApi.SendAsync(blue, new Contract.Target.Ref(a), SuspendId, Contract.Kinds.Suspend, "body", cancellationToken: ct)).Outcome ==
                Contract.AdmissionOutcome.Refused, "a one-way suspension has no selection payload");
            Check((await basic.SendAsync(blue, new Contract.Target.Ref(a), "before-command", "input", "first", cancellationToken: ct)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "an ordinary input precedes the due suspension");
            Check((await Send(blue, a, SuspendId, Contract.Kinds.Suspend, new(DueAt: 0))).Outcome == Contract.AdmissionOutcome.Accepted,
                "an authorized exact-ref suspension is admitted");
            Check((await basic.SendAsync(blue, new Contract.Target.Ref(a), SuspendId, Contract.Kinds.Suspend, cancellationToken: ct)).Outcome ==
                Contract.AdmissionOutcome.Refused, "replay checks the current host grant");
            Check((await basic.SendAsync(blue, new Contract.Target.Ref(a), "after-command", "input", "pending", cancellationToken: ct)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "an ordinary input can be queued behind the command");
            Check((await Send(blue, b, SuspendId, Contract.Kinds.Resume)) is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var replay } && replay == a,
                "changed target and kind replay the original bound receipt");
            Check((await basic.SendAsync(blue, new Contract.Target.Ref(b), "independent", "input", "next", cancellationToken: ct)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "an ordinary input on another root follows its own future command");
            await PostgresMultiRootClient.CallSecondaryAsync(ct, "operator-resume", c.Id);
            Check((await Send(blue, b, "resume", Contract.Kinds.Resume)).Outcome == Contract.AdmissionOutcome.Accepted,
                "an independent resume and a second tenant's operation are admitted");
            Check((await Send(blue, a, "future", Contract.Kinds.Resume, new(DueAt: long.MaxValue, KeepFor: TimeSpan.Zero))).Outcome ==
                Contract.AdmissionOutcome.Accepted, "a future command may outlive its receipt");
            while (await admission.SweepOperatorReceiptsAsync(ct) == 0) await Task.Delay(5, ct);
            Check((await Send(blue, b, "future", Contract.Kinds.Suspend)).Outcome == Contract.AdmissionOutcome.Accepted,
                "an expired operation can be reused on another root");
            var race = await Task.WhenAll(Send(blue, a, "race", Contract.Kinds.Suspend).AsTask(), Send(blue, b, "race", Contract.Kinds.Resume).AsTask());
            Check(race.Count(r => r.Outcome == Contract.AdmissionOutcome.Accepted) == 1 &&
                race.Count(r => r.Outcome == Contract.AdmissionOutcome.Duplicate) == 1 && race[0].Actor == race[1].Actor,
                "concurrent admissions return the same original target");
            Console.WriteLine($"admitted {Environment.ProcessId} {a.Id} {b.Id} {c.Id}");
            return 0;
        }

        var refs = previous.Select(id => new Contract.ActorRef(id)).ToArray();
        var first = await admission.ReadDueRootOperatorAsync("blue", refs[0].Id, ct);
        var second = await admission.ReadDueRootOperatorAsync("blue", refs[1].Id, ct);
        var third = await admission.ReadDueRootOperatorAsync(PostgresMultiRootClient.Secondary, refs[2].Id, ct);
        var image = (await store.ReadFirstRootAsync("blue", refs[0].Id, ct))!;
        Check(first is { Id: SuspendId, Kind: Contract.Kinds.Suspend, Due: 0, Sender: null, Operator.Selection: null, TargetCreation: 1 } &&
            first.Operator is not null && first.Target.Id == image.Actor.Id && first.InboxKey.Scope == InboxKey.FoundationOccurrenceScope &&
            second is { Id: "resume", Kind: Contract.Kinds.Resume, Due: null } && second.Operator is not null &&
            third is { Id: SuspendId, Kind: Contract.Kinds.Resume } && third.Operator is not null, "cold candidate read restores the exact envelopes");
        var ordered = await admission.ReadPendingDirectAsync("blue", refs[0].Id, ct);
        var independent = (await admission.ReadPendingDirectAsync("blue", refs[1].Id, ct)).Single();
        Check(await admission.ReadDueRootOperatorAsync("blue", refs[2].Id, ct) is null &&
            ordered is [{ Id: "before-command", Payload: "first", Operator: null }, { Id: "after-command", Payload: "pending", Operator: null }], "operator occurrences stay in their tenant and outside ordinary reads");
        await using (var db = Open())
        {
            Check(await db.Messages.CountAsync(m => m.InboxScope == InboxKey.FoundationOccurrenceScope && m.Id.StartsWith("operator/") && !m.Done, ct) == 7 &&
                await db.Messages.AnyAsync(m => m.Target == image.Actor.Id && m.Due == long.MaxValue && !m.Done, ct), "sweeping a receipt preserves its pending occurrence");
            var operation = FoundationRootCreator.OperationDigest(SuspendId);
            Check(await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "FoundationOperatorReceipts" r JOIN "Messages" m ON m.xmin = r.xmin
                    WHERE r."Tenant" = 'blue' AND r."Operation" = {operation} AND r."Reference" = {refs[0].Id} AND m."Id" = {first!.InboxKey.Id}) AS "Value"
                """).SingleAsync(ct), "the receipt and exact command were inserted by one PostgreSQL transaction");
        }
        await using (var db = Open())
        {
            var exact = db.Messages.Where(m => m.Target == image.Actor.Id && m.InboxScope == InboxKey.FoundationOccurrenceScope &&
                m.Id == first!.InboxKey.Id);
            Check(await exact.ExecuteUpdateAsync(change => change.SetProperty(m => m.JobGeneration, 1L), ct) == 1,
                "the fixture sets one mismatched generation");
            try
            {
                _ = await admission.ReadDueRootOperatorAsync("blue", refs[0].Id, ct);
                throw new InvalidOperationException("The cold reader accepted the mismatched generation.");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite &&
                error.Message == "Unexpected stored one-way operator envelope.")
            {
                Console.WriteLine("ok the cold reader refuses a mismatched generation");
            }
            finally
            {
                await exact.ExecuteUpdateAsync(change => change.SetProperty(m => m.JobGeneration, 0L), ct);
            }
        }
        await store.BindWorkerPartitionAsync(0, ct);
        var duration = TimeSpan.FromSeconds(30);
        var lease = (await store.TakeAsync(0, "operator-cold", duration, ct))!;
        var horizon = TimeSpan.FromHours(2);
        var polled = await store.PollAsync(0, lease.Epoch, horizon, 8, ct);
        Check(polled.Messages.Select(m => m.Message).SequenceEqual([ordered[0], independent]) &&
            (await store.PollAsync(0, lease.Epoch, TimeSpan.Zero, 1, ct)).Messages.Single().Message == ordered[0],
            "discovery offers the ordinary prefix and independent root before applying the page limit");
        await using (var db = Open())
        {
            Check(await db.Messages.AnyAsync(m => m.Target == independent.Target.Id && m.Id.StartsWith("operator/") &&
                m.Due > polled.Now && m.Due <= polled.Now + (long)horizon.TotalMilliseconds && !m.Done, ct),
                "the other root's earlier command is inside the poll horizon and still in the future");
        }
        var fixedStore = NewStore();
        await fixedStore.BindWorkerRootsAsync([("blue", refs[0].Id), ("blue", refs[1].Id)], ct);
        Check((await fixedStore.PollAsync(0, lease.Epoch, horizon, 8, ct)).Messages.Select(m => m.Message).SequenceEqual([ordered[0], independent]),
            "fixed root bindings return the same eligible prefixes");
        await store.ReleaseAsync(0, lease.Epoch, ct);
        var current = (await store.TakeAsync(0, "replacement", duration, ct))!;
        Check(current.Epoch > lease.Epoch && current.Position == lease.Position, "the ownership epoch changes at the same journal position");
        Check(!await Apply(refs[0], image, ordered[0], lease.For(0)) &&
            (await store.ReadFirstRootAsync("blue", refs[0].Id, ct))?.Revision == image.Revision &&
            (await admission.ReadPendingDirectAsync("blue", refs[0].Id, ct)).SequenceEqual(ordered),
            "a stale fence preserves the eligible prefix and checkpoint");
        Check(await Apply(refs[0], image, ordered[0], current.For(0)), "the current fence commits the identical eligible prefix");
        var after = (await store.ReadFirstRootAsync("blue", refs[0].Id, ct))!;
        var next = current.For(0) with { Position = current.Position + 1 };
        Check(after.Revision == image.Revision + 1 && !await Apply(refs[0], after, ordered[1], next) &&
            (await store.ReadFirstRootAsync("blue", refs[0].Id, ct))?.Revision == after.Revision &&
            (await admission.ReadPendingDirectAsync("blue", refs[0].Id, ct)).Single() == ordered[1] &&
            await admission.ReadDueRootOperatorAsync("blue", refs[0].Id, ct) == first,
            "a due command preserves the waiting checkpoint and prevents a later ordinary commit at the current fence");
        var other = (await store.ReadFirstRootAsync("blue", refs[1].Id, ct))!;
        Check((await store.PollAsync(0, current.Epoch, horizon, 1, ct)).Messages.Single().Message == independent &&
            await Apply(refs[1], other, independent, next) &&
            (await store.ReadFirstRootAsync("blue", refs[1].Id, ct))?.Revision == other.Revision + 1 &&
            (await admission.ReadPendingDirectAsync("blue", refs[1].Id, ct)).Count == 0 &&
            await admission.ReadDueRootOperatorAsync("blue", refs[1].Id, ct) == second,
            "the next bounded poll and commit advance the independent root while both due commands stay pending");

        async Task<bool> Apply(Contract.ActorRef root, EfStore.RootImage before, Message input, PartitionFence fence)
        {
            try
            {
                return (await store.CommitRootTurnAsync("blue", root.Id, before, [input], fence, duration, InstanceStatus.Running,
                    before.State, before.StateVersion, before.TypeData, before.Labels, """["application"]""", ct)).Landed;
            }
            catch (StoreException e) when (e.Failure == Failure.Definite) { return false; }
        }
        Console.WriteLine($"read {Environment.ProcessId} {refs[0].Id} {refs[1].Id} {refs[2].Id}");
        return 0;

        ValueTask<Contract.Admission> Send(Contract.BoundScope scope, Contract.ActorRef actor, string id, string kind, Contract.AddOptions? add = null) =>
            operatorApi.SendAsync(scope, new Contract.Target.Ref(actor), id, kind, options: add, cancellationToken: ct);
        async Task<Contract.ActorRef> Start(Contract.BoundScope scope, string op) =>
            (await runtime.StartAsync(scope, op, NativeExecutionModel.Start(DirectApprovalWorkflow.Definition), cancellationToken: ct)).Actor!.Value;
    }

    private static void Check(bool holds, string name)
    {
        if (!holds) throw new InvalidOperationException(name);
        Console.WriteLine("ok " + name);
    }
}
