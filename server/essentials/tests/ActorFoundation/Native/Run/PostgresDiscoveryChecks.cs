using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Runtime.Hosting;
using static Kernel.Native.Run.PostgresMultiRootClient;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresDiscoveryChecks
{
    private sealed record Sample(string Tenant, Contract.ActorRef Reference, InstanceRef Actor);

    internal static async Task<int> RunAsync(int partition, bool birth, CommitSql? writer = null, bool? inBatch = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stop = timeout.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ?? throw new InvalidOperationException("Supply a dedicated preview database.");
        var meter = new Counting();
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(meter).Options;
        KernelDb Open() => new(options);
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = writer };
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        if (birth)
        {
            await store.CreateAsync(2);
            await admission.InstallAsync(stop);
        }
        var api = new PostgresPreviewFoundation(admission, new(store, await admission.OpenJournalCursorAsync(stop), 64), WorkflowActor.WorkflowActor.ActorType);
        var scope = EmbeddedTenant.Bind(Primary);
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());
        var turns = new List<(string Actor, string Input)>();
        var prepared = await PostgresNativeRootOwner.PreparePartitionAsync(store, "discover-" + Environment.ProcessId, partition, registry, 1, stop,
            (actor, inputs, _) => turns.Add((actor.Id, inputs.Single().Id)));
        await using var owner = await prepared.TryActivateAsync(stop) ?? throw new InvalidOperationException("Partition already owned.");
        Check(!store.SupportsJobTrialClear, "discovery retains the qualified writer capabilities");
        Sample? future = null;
        if (birth)
        {
            await store.AddAsync(new Message("unrelated-input", InstanceRef.In(partition, "unregistered"), "work"), stop);
            await Pass();
            Check(turns.Count == 0, "an activated partition without admitted roots runs no turns");
            // A direct input may be accepted before the durable root is first materialized.
            for (var index = 0; future is null; index++)
            {
                var spec = WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start(DirectApprovalWorkflow.Definition));
                var admitted = await api.SendAsync(scope, new Contract.Target.Start(spec), $"future-{index}", Contract.Kinds.Start,
                    options: new(DueAt: long.MaxValue), cancellationToken: stop);
                var reference = admitted.Actor!.Value;
                var address = (await admission.ResolveAsync(scope.Tenant, reference.Id, stop))!;
                var actor = new InstanceRef(address.Id);
                if (actor.Partition == partition) future = new(Primary, reference, actor);
            }
            var early = await api.SendAsync(scope, new Contract.Target.Ref(future.Reference), "early", CompositeSignal.Kind,
                cancellationToken: stop);
            Check(early.Outcome == Contract.AdmissionOutcome.Accepted && early.Actor == future.Reference, "direct input accepted while its creator is pending");
            var epoch = ((Worker)owner).Fences.Single().Epoch;
            var empty = await store.PollAsync(partition, epoch, TimeSpan.Zero, 1, stop);
            Check(empty.Messages.Count == 0 && empty.Now > 0, "the pending creator's input leaves a clock-only poll");
            await Pass();
            Check(turns.Count == 0, "premature direct input never entered a model turn");
        }
        else
        {
            // Reopen and finish the pending approval before the observer resolves any actor reference.
            while (turns.Count == 0) await Pass();
            Check(turns is [(_, "approval-2")], "cold partition owner ran exactly the previously admitted final approval");
        }
        var samples = new List<Sample>();
        var locals = new List<Sample>();
        var i = 0;
        while (locals.Count == 0 || samples.All(s => s.Actor.Partition == partition)) await SampleAsync();
        if (birth)
        {
            var fixedClient = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = writer };
            await fixedClient.BindWorkerRootsAsync([(future!.Tenant, future.Reference.Id),
                (locals[0].Tenant, locals[0].Reference.Id)], stop);
            var fixedPage = await fixedClient.PollAsync(partition, ((Worker)owner).Fences.Single().Epoch, TimeSpan.Zero, 1, stop);
            Check(fixedPage.Messages is [var ready] && ready.Message.Target == locals[0].Actor,
                "a fixed owner can poll the independent ready creator ahead of the pending root's direct input");
            meter.Reset();
            var poll = await store.PollAsync(partition, ((Worker)owner).Fences.Single().Epoch, TimeSpan.Zero, 1, stop);
            Check(poll.Messages.Count == 1 && meter.Commands == 1, "one PostgreSQL poll command returns one qualified creator with its binding");
            while (turns.Count < locals.Count) await Pass();
        }
        if (future is not null)
        {
            Check(await api.GetAsync(scope, future.Reference, stop) is null &&
                (await admission.ReadPendingDirectAsync(future.Tenant, future.Reference.Id, stop)).Single().Id == "early",
                "a ready workflow committed and the pending root retained its direct input");
        }
        var first = locals[0];
        var earlier = locals.Count;
        do { await SampleAsync(secondary: true); } while (locals.Count == earlier);
        var later = locals[^1];
        if (birth)
        {
            while (turns.Count < locals.Count) await Pass();
            Check(turns.Select(t => t.Actor).SequenceEqual(locals.Select(s => s.Actor.Id)), "late public start discovered on the live partition in admission order");
            await Send(first, "approval-1", "Ada", Contract.AdmissionOutcome.Accepted);
            while ((await Image(first)).Revision < 2) await Pass();
            await Send(first, "approval-2", "Grace", Contract.AdmissionOutcome.Accepted);
        }
        else
        {
            Check(turns.Single().Actor == first.Actor.Id, "cold owner finished the intended root");
            var finished = await Image(first);
            Check(finished is { Revision: 3, Status: InstanceStatus.Finished } &&
                Json.Read<NativeState>(finished.State!).Results["approve"]!.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(["Ada", "Grace"]),
                "the two approvals applied once");
            var history = await api.TailAsync(scope, first.Reference, cancellationToken: stop);
            Check(history.Items.Select(e => e.Revision).Distinct().SequenceEqual([1L, 2L, 3L]), "one journal revision per turn");
            var replay = await api.SendAsync(scope, new Contract.Target.Ref(first.Reference), "approval-2", CompositeSignal.Kind, "changed", cancellationToken: stop);
            Check(replay.Outcome == Contract.AdmissionOutcome.Duplicate && replay.Actor == first.Reference, "public replay recognized after restart");
            await Pass();
            Check(turns.Count == 1 && (await Image(later)).Revision == 1, "replay generated no turn and the later root retained its own checkpoint");
            await CallSecondaryAsync(stop, "missing", first.Reference.Id);
            var foreign = await api.TailAsync(scope, later.Reference, cancellationToken: stop);
            Check(await api.GetAsync(scope, later.Reference, stop) is null && foreign.Items.Count == 0 && foreign.Next is null,
                "the two configured tenant clients see neither the other actor nor its journal");
        }
        foreach (var other in samples.Where(s => s.Actor.Partition != partition))
        {
            if (other.Tenant == Secondary) await CallSecondaryAsync(stop, "missing", other.Reference.Id);
            else Check(await api.GetAsync(scope, other.Reference, stop) is null, "the other partition has no checkpoint");
            Check(await admission.ReadPendingCreatorAsync(other.Tenant, other.Reference.Id, stop) is not null, "other partition retains its unprocessed creator");
            var outsideRefused = false;
            try { await store.LoadAsync([other.Actor.Id], stop); }
            catch (NotSupportedException) { outsideRefused = true; }
            Check(outsideRefused, "the owner's scoped load refuses an actor on the other partition");
        }
        await using (var db = Open())
            Check(await db.Messages.AsNoTracking().AnyAsync(m => m.Id == "unrelated-input" && m.Partition == partition && m.Seen == 0 && !m.Done, stop),
                "an ordinary unregistered actor in the same partition remains untouched");
        Console.WriteLine($"ok process {Environment.ProcessId} partition {partition} epoch {((Worker)owner).Fences.Single().Epoch}, {(birth ? "late start" : "cold replay")}, first {first.Reference.Id}, later {later.Reference.Id}");
        return 0;

        async Task SampleAsync(bool secondary = false)
        {
            stop.ThrowIfCancellationRequested();
            var tenant = secondary ? Secondary : Primary;
            var expected = birth ? Contract.AdmissionOutcome.Accepted : Contract.AdmissionOutcome.Duplicate;
            var operation = $"discover-{i++}";
            var reference = secondary
                ? new Contract.ActorRef(await CallSecondaryAsync(stop, "start-receipt", operation, expected.ToString()))
                : await Start(api, scope, operation, stop, expected);
            var address = await admission.ResolveAsync(tenant, reference.Id, stop) ?? throw new InvalidOperationException("Missing birth.");
            var sample = new Sample(tenant, reference, new(address.Id));
            samples.Add(sample);
            if (sample.Actor.Partition == partition) locals.Add(sample);
        }
        async Task Pass()
        {
            stop.ThrowIfCancellationRequested();
            var before = turns.Count;
            await owner.WokenAsync(null, stop);
            await owner.RunAsync(stop);
            Check(turns.Count - before <= 1, "poll limit one preserved");
        }
        async Task<EfStore.RootImage> Image(Sample sample) => await store.ReadFirstRootAsync(sample.Tenant, sample.Reference.Id, stop)
            ?? throw new InvalidOperationException("Missing public root checkpoint.");
        async Task Send(Sample sample, string id, string value, Contract.AdmissionOutcome expected)
        {
            var frame = Json.Read<NativeState>((await Image(sample)).State!).Frames.Single().Key;
            var sent = await api.SendAsync(scope, new Contract.Target.Ref(sample.Reference), id, CompositeSignal.Kind,
                Json.Write(new CompositeSignal(frame, "approve", value)), cancellationToken: stop);
            Check(sent.Outcome == expected && sent.Actor == sample.Reference, "direct input admitted");
        }
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}
