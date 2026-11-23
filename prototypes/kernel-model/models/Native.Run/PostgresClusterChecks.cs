using System.Diagnostics;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresClusterChecks
{
    private const string Primary = "preview-primary", Peer = "preview-peer";
    private sealed record Root(Contract.ActorRef Ref, InstanceRef Actor);
    private sealed record Ownership(string? Node, long Epoch);

    internal static async Task<int> RunAsync(int partitions, bool peer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var stop = timeout.Token;
        if (partitions < 2 || partitions > 1024 || partitions % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(partitions), "Use an even partition count between 2 and 1024.");
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ?? throw new InvalidOperationException("Supply a fresh dedicated preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => new(options);
        var store = new EfStore(Open, postgres: true);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        if (!peer)
        {
            await store.CreateAsync(partitions);
            await admission.InstallAsync(stop);
        }
        var name = peer ? Peer : Primary;
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());
        var host = PostgresNativeRootOwner.PrepareNode(store, name, partitions, registry, new WorkerOptions { Partitions = partitions });
        await using var node = new RuntimeNode(name, store, partitions, new LoopOptions
            { BalanceEvery = TimeSpan.FromMilliseconds(150), Window = TimeSpan.FromSeconds(2), PollInterval = TimeSpan.FromMilliseconds(150) });
        if (peer)
        {
            await node.StartAsync(host, stop);
            await Console.In.ReadLineAsync(stop);
            await node.StopAsync(stop);
            Healthy();
            return 0;
        }

        var scope = new Contract.BoundScope("cluster");
        var api = new PostgresPreviewFoundation(admission, store, new(store, await admission.OpenJournalCursorAsync(stop), 64), WorkflowActor.WorkflowActor.ActorType);
        var workflow = new WorkflowRuntime(api, api);
        var roots = new List<Root>();
        do { await Start(); } while (roots.Select(r => r.Actor.Partition).Distinct().Count() < 2);
        await node.StartAsync(host, stop);
        await Until(async () => await Count(Primary) == partitions && await Image(roots[^1]) is { Revision: 1 });
        await using (var db = Open())
        {
            var born = roots.Select(r => r.Actor.Id).ToArray();
            var grouped = await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Journal" WHERE "Revision" = 1 AND "Instance" = ANY({born}) GROUP BY xmin::text
                    HAVING COUNT(DISTINCT "Partition") >= 2) AS "Value"
                """).SingleAsync(stop);
            Check(grouped, "one fenced PostgreSQL transaction checkpointed generated roots on distinct partitions");
        }

        var info = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, UseShellExecute = false };
        info.ArgumentList.Add(typeof(PostgresClusterChecks).Assembly.Location);
        info.ArgumentList.Add("pg-cluster-peer");
        info.ArgumentList.Add(partitions.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var child = Process.Start(info) ?? throw new InvalidOperationException("Could not start second RuntimeNode.");
        try
        {
            await Until(async () => await Count(Primary) == partitions / 2 && await Count(Peer) == partitions / 2);
            Root? selected = null;
            while (selected is null)
            {
                foreach (var r in roots)
                    if ((await Placement(r)).Node == Peer) { selected = r; break; }
                if (selected is null) await Start();
            }
            var target = selected;
            await Until(async () => await Image(target) is { Revision: 1 });
            var beforeApproval = await Placement(target);
            Check(beforeApproval.Node == Peer, "peer owns the target immediately before approval admission");
            await Send(target, "approve-1", "Ada", Contract.AdmissionOutcome.Accepted);
            await Until(async () => await Image(target) is { Revision: 2 });
            Check(await Placement(target) == beforeApproval, "the same peer epoch spans approval admission and the second checkpoint");
            await child.StandardInput.WriteLineAsync("stop");
            await child.WaitForExitAsync(stop);
            Check(child.ExitCode == 0, "peer stopped cleanly");
            await Until(async () => await Count(Primary) == partitions);
            var afterDeparture = await Placement(target);
            Check(afterDeparture.Node == Primary && afterDeparture.Epoch > beforeApproval.Epoch, "remaining node assumed the exact root's partition at a later epoch");
            await Send(target, "approve-1", "changed", Contract.AdmissionOutcome.Duplicate);
            await Send(target, "approve-2", "Grace", Contract.AdmissionOutcome.Accepted);
            await Until(async () => await Image(target) is { Status: InstanceStatus.Finished });
            var image = (await Image(target))!;
            var results = Json.Read<NativeState>(image.State!).Results["approve"]!.AsArray().Select(x => x!.GetValue<string>());
            var journal = await api.TailAsync(scope, target.Ref, cancellationToken: stop);
            Check(image.Revision == 3 && results.SequenceEqual(["Ada", "Grace"]) &&
                journal.Items.Select(x => x.Revision).Distinct().SequenceEqual([1L, 2L, 3L]), "exactly one approval of each kind landed across handover");
            Check(await api.GetAsync(new("other"), target.Ref, stop) is null, "public read stayed tenant bound");
            await node.StopAsync(stop);
            Healthy();
            Console.WriteLine($"ok PostgreSQL public Cluster: {partitions} partitions, processes {Environment.ProcessId} and {child.Id}, target {target.Actor.Partition}, epochs {beforeApproval.Epoch}->{afterDeparture.Epoch}, shared journal transaction, 3 revisions.");
            return 0;
        }
        finally
        {
            if (!child.HasExited)
            {
                child.StandardInput.Close();
                try { await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) { child.Kill(); await child.WaitForExitAsync(); }
            }
        }

        async Task Start()
        {
            var reply = await workflow.StartAsync(scope, "cluster-start-" + roots.Count, NativeExecutionModel.Start(DirectApprovalWorkflow.Definition), cancellationToken: stop);
            Check(reply.Outcome == Contract.AdmissionOutcome.Accepted, "fresh public start");
            var reference = reply.Actor!.Value;
            var address = (await admission.ResolveAsync(scope.Tenant, reference.Id, stop))!;
            roots.Add(new(reference, new(address.Id)));
        }
        async Task Send(Root root, string id, string value, Contract.AdmissionOutcome expected)
        {
            var frame = Json.Read<NativeState>((await Image(root))!.State!).Frames.Single().Key;
            var input = Json.Write(new CompositeSignal(frame, "approve", value));
            Check((await api.SendAsync(scope, new Contract.Target.Ref(root.Ref), id, CompositeSignal.Kind, input, cancellationToken: stop)).Outcome == expected, "direct admission result");
        }
        Task<EfStore.RootImage?> Image(Root root) => store.ReadFirstRootAsync(scope.Tenant, root.Ref.Id, stop);
        async Task<int> Count(string owner) { await using var db = Open(); return await db.Partitions.CountAsync(p => p.Owner == owner, stop); }
        async Task<Ownership> Placement(Root root)
        {
            await using var db = Open();
            return await db.Partitions.Where(p => p.Id == root.Actor.Partition).Select(p => new Ownership(p.Owner, p.Epoch)).SingleAsync(stop);
        }
        async Task Until(Func<Task<bool>> condition)
        {
            while (!await condition()) { Healthy(); await Task.Delay(40, stop); }
        }
        void Healthy() { if (node.LastError is { } ex) throw new InvalidOperationException(name + " runtime error", ex); }
    }

    private static void Check(bool pass, string detail)
    {
        if (!pass) throw new InvalidOperationException(detail);
    }
}
