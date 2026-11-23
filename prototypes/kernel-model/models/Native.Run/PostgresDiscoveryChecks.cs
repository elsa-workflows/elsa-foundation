using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresDiscoveryChecks
{
    private sealed record Sample(Contract.BoundScope Scope, Contract.ActorRef Reference, InstanceRef Actor);

    internal static async Task<int> RunAsync(int partition, bool birth)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stop = timeout.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ?? throw new InvalidOperationException("Supply a dedicated preview database.");
        var meter = new Counting();
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(meter).Options;
        KernelDb Open() => new(options);
        var store = new EfStore(Open, postgres: true);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        if (birth)
        {
            await store.CreateAsync(2);
            await admission.InstallAsync(stop);
        }
        var api = new PostgresPreviewFoundation(admission, store, new(store, await admission.OpenJournalCursorAsync(stop), 64), WorkflowActor.WorkflowActor.ActorType);
        var workflow = new WorkflowRuntime(api, api);
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());
        var turns = new List<(string Actor, string Input)>();
        var prepared = await PostgresNativeRootOwner.PreparePartitionAsync(store, "discover-" + Environment.ProcessId, partition, registry, 1, stop,
            (actor, inputs, _) => turns.Add((actor.Id, inputs.Single().Id)));
        await using var owner = await prepared.TryActivateAsync(stop) ?? throw new InvalidOperationException("Partition already owned.");
        if (birth)
        {
            await store.AddAsync(new Message("unrelated-input", InstanceRef.In(partition, "unregistered"), "work"), stop);
            await Pass();
            Check(turns.Count == 0, "an activated partition without admitted roots runs no turns");
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
            meter.Reset();
            var poll = await store.PollAsync(partition, ((Worker)owner).Fences.Single().Epoch, TimeSpan.Zero, 1, stop);
            Check(poll.Messages.Count == 1 && meter.Commands == 1, "one PostgreSQL poll command returns one qualified creator with its binding");
            while (turns.Count < locals.Count) await Pass();
        }
        var first = locals[0];
        var earlier = locals.Count;
        do { await SampleAsync(); } while (locals.Count == earlier);
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
            var history = await api.TailAsync(first.Scope, first.Reference, cancellationToken: stop);
            Check(history.Items.Select(e => e.Revision).Distinct().SequenceEqual([1L, 2L, 3L]), "one journal revision per turn");
            var replay = await api.SendAsync(first.Scope, new Contract.Target.Ref(first.Reference), "approval-2", CompositeSignal.Kind, "changed", cancellationToken: stop);
            Check(replay.Outcome == Contract.AdmissionOutcome.Duplicate, "public replay recognized after restart");
            await Pass();
            Check(turns.Count == 1 && (await Image(later)).Revision == 1, "replay generated no turn and the later root retained its own checkpoint");
            Check(await api.GetAsync(new("another-tenant"), first.Reference, stop) is null, "reads stay tenant bound");
        }
        foreach (var other in samples.Where(s => s.Actor.Partition != partition))
        {
            Check(await api.GetAsync(other.Scope, other.Reference, stop) is null &&
                await admission.ReadPendingCreatorAsync(other.Scope.Tenant, other.Reference.Id, stop) is not null, "other partition retains its unprocessed creator");
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

        async Task SampleAsync()
        {
            stop.ThrowIfCancellationRequested();
            var scope = new Contract.BoundScope(i % 2 == 0 ? "preview-a" : "preview-b");
            var started = await workflow.StartAsync(scope, $"discover-{i++}", NativeExecutionModel.Start(DirectApprovalWorkflow.Definition), cancellationToken: stop);
            Check(started.Outcome == (birth ? Contract.AdmissionOutcome.Accepted : Contract.AdmissionOutcome.Duplicate), "observer resolved the original start receipt");
            var reference = started.Actor!.Value;
            var address = await admission.ResolveAsync(scope.Tenant, reference.Id, stop) ?? throw new InvalidOperationException("Missing birth.");
            var sample = new Sample(scope, reference, new(address.Id));
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
        async Task<EfStore.RootImage> Image(Sample sample) => await store.ReadFirstRootAsync(sample.Scope.Tenant, sample.Reference.Id, stop)
            ?? throw new InvalidOperationException("Missing public root checkpoint.");
        async Task Send(Sample sample, string id, string value, Contract.AdmissionOutcome expected)
        {
            var frame = Json.Read<NativeState>((await Image(sample)).State!).Frames.Single().Key;
            var sent = await api.SendAsync(sample.Scope, new Contract.Target.Ref(sample.Reference), id, CompositeSignal.Kind,
                Json.Write(new CompositeSignal(frame, "approve", value)), cancellationToken: stop);
            Check(sent.Outcome == expected, "direct input admitted");
        }
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}
