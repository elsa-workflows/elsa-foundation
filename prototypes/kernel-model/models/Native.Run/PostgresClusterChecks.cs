using System.Collections.Concurrent;
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

    internal static async Task<int> RunAsync(int partitions, bool peer, bool abrupt = false, bool flowchart = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var stop = timeout.Token;
        if (partitions < 2 || partitions > 1024 || partitions % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(partitions), "Use an even partition count between 2 and 1024.");
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ?? throw new InvalidOperationException("Supply a fresh dedicated preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => flowchart ? KernelDb.ForPostgresRootSelf(options) : new KernelDb(options);
        var store = new EfStore(Open, postgres: true);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        if (!peer)
        {
            await store.CreateAsync(partitions);
            await admission.InstallAsync(stop);
        }
        var name = peer ? Peer : Primary;
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());
        var applied = new ConcurrentDictionary<InboxKey, int>();
        var worker = new WorkerOptions { Partitions = partitions };
        if (flowchart)
        {
            await store.EnableRootSelfSendsAsync(stop);
            registry.Add(PostgresFlowchartPreview.Build());
            int? emitting = null;
            worker = worker with
            {
                OnPreparedCheckpoint = (turn, _, _) =>
                {
                    if (peer && turn.Row is { Revision: 1 } && turn.Sent.Any(m => m.InboxKey.Scope == -1))
                        emitting = new InstanceRef(turn.Instance).Partition;
                },
                OnJournal = written =>
                {
                    // Hold only this fixture child's pass after its emitter commit has landed. The test parent ends it.
                    if (peer && emitting is { } home && written.Contains(home)) stop.WaitHandle.WaitOne();
                },
                OnTurn = (_, messages, _) =>
                {
                    if (!peer)
                        foreach (var m in messages.Where(m => m.InboxKey.Scope == -1))
                            applied.AddOrUpdate(m.InboxKey, 1, (_, count) => count + 1);
                }
            };
        }
        var host = PostgresNativeRootOwner.PrepareNode(store, name, partitions, registry, worker);
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

        var scope = FixedTenantScope.Bind("cluster");
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
        info.ArgumentList.Add(flowchart ? "pg-cluster-flowchart-peer" : "pg-cluster-peer");
        info.ArgumentList.Add(partitions.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var child = Process.Start(info) ?? throw new InvalidOperationException("Could not start second RuntimeNode.");
        try
        {
            await Until(async () =>
            {
                await using var db = Open();
                var split = await db.Partitions.Where(p => p.Owner == Primary || p.Owner == Peer).GroupBy(p => p.Owner)
                    .Select(g => new { Node = g.Key!, Count = g.Count() }).ToDictionaryAsync(g => g.Node, g => g.Count, stop);
                return split.GetValueOrDefault(Primary) == partitions / 2 && split.GetValueOrDefault(Peer) == partitions / 2;
            });
            if (flowchart)
            {
                await FlowchartHandoverAsync(child);
                await node.StopAsync(stop);
                Healthy();
                return 0;
            }
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
            var prefix = await api.TailAsync(scope, target.Ref, cancellationToken: stop);
            Check(prefix.Items.Count(e => e.Revision == 2 && PostgresPublicPreview.Decode(e) == "approved:approve-1") == 1 && prefix.Next is not null,
                "the public journal records the first approval once before peer departure");
            if (abrupt)
                await PostgresClusterExit.ObserveAsync(Open, target.Actor.Partition, Peer, beforeApproval.Epoch, Primary, child, stop);
            else
            {
                await child.StandardInput.WriteLineAsync("stop");
                await child.WaitForExitAsync(stop);
                Check(child.ExitCode == 0, "peer stopped cleanly");
            }
            await Until(async () => await Count(Primary) == partitions);
            var afterDeparture = await Placement(target);
            Check(afterDeparture.Node == Primary && afterDeparture.Epoch > beforeApproval.Epoch, "remaining node assumed the exact root's partition at a later epoch");
            await Send(target, "approve-1", "changed", Contract.AdmissionOutcome.Duplicate);
            Check((await api.TailAsync(scope, target.Ref, prefix.Next, stop)).Items.Count == 0, "replay added no public journal entry");
            await Send(target, "approve-2", "Grace", Contract.AdmissionOutcome.Accepted);
            await Until(async () => await Image(target) is { Status: InstanceStatus.Finished });
            var image = (await Image(target))!;
            var results = Json.Read<NativeState>(image.State!).Results["approve"]!.AsArray().Select(x => x!.GetValue<string>());
            var suffix = await api.TailAsync(scope, target.Ref, prefix.Next, stop);
            Check(suffix.Items.All(e => e.Revision == 3 && PostgresPublicPreview.Decode(e) != "approved:approve-1") &&
                suffix.Items.Count(e => PostgresPublicPreview.Decode(e) == "approved:approve-2") == 1,
                "the pre-exit bookmark returns only the third revision and its single second approval");
            var journal = await api.TailAsync(scope, target.Ref, cancellationToken: stop);
            Check(journal.Items.Count(e => PostgresPublicPreview.Decode(e) == "approved:approve-1") == 1 &&
                journal.Items.Count(e => e.Revision == 3 && PostgresPublicPreview.Decode(e) == "approved:approve-2") == 1,
                "public journal contains both applied approvals once");
            Check(image.Revision == 3 && results.SequenceEqual(["Ada", "Grace"]) &&
                journal.Items.Select(x => x.Revision).Distinct().SequenceEqual([1L, 2L, 3L]), "exactly one approval of each kind landed across handover");
            await PostgresMultiRootClient.CallSecondaryAsync(stop, "missing", target.Ref.Id);
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

        async Task FlowchartHandoverAsync(Process peerProcess)
        {
            Root target;
            Ownership before;
            do
            {
                await Start(purchase: true);
                target = roots[^1];
                before = await Placement(target);
            } while (before.Node != Peer);

            async Task<string?> Pending(KernelDb db) =>
                await db.Database.SqlQuery<string>($"""
                    SELECT m."Id" AS "Value" FROM "Messages" m JOIN "Instances" i
                         ON i."Id" = m."Target" AND i."Partition" = m."Partition" AND i.xmin = m.xmin
                    WHERE i."Id" = {target.Actor.Id} AND i."Revision" = 1 AND m."InboxScope" = {EfStore.RootSelfScope}
                        AND NOT m."Done" AND m."Sender" = i."Id"
                    """).SingleOrDefaultAsync(stop);
            string? persisted = null;
            await Until(async () =>
            {
                await using var db = Open();
                persisted = await Pending(db);
                return persisted is not null;
            });
            var birth = await api.TailAsync(scope, target.Ref, cancellationToken: stop);
            Check(birth.Next is not null && birth.Items.All(j => !PostgresFlowchartPreview.IsJoin(j)), "a pre-join journal bookmark is available");
            await using (var guard = Open())
            await using (var transaction = await guard.Database.BeginTransactionAsync(stop))
            {
                // Hold only the target fence. The child exits at OS level and needs no database cleanup to finish.
                var held = await guard.Database.SqlQuery<Ownership>($"""
                    SELECT "Owner" AS "Node", "Epoch" FROM "Partitions" WHERE "Id" = {target.Actor.Partition} FOR UPDATE
                    """).ToListAsync(stop);
                Check(held.Single() == before && await Pending(guard) == persisted && !peerProcess.HasExited,
                    "the live peer owns the locked fence with its original continuation pending");
                await using (var observer = Open())
                {
                    var available = await observer.Database.SqlQuery<int>($"""
                        SELECT "Id" AS "Value" FROM "Partitions" WHERE "Id" = {target.Actor.Partition} FOR UPDATE SKIP LOCKED
                        """).ToListAsync(stop);
                    Check(available.Count == 0, "an independent ownership update would have to wait");
                }
                peerProcess.Kill();
                await peerProcess.WaitForExitAsync(stop);
                Check(await Pending(guard) == persisted, "the exact continuation is still pending after the peer exits");
                await transaction.CommitAsync(stop);
            }
            await Until(async () => await Count(Primary) == partitions);
            var after = await Placement(target);
            Check(after.Node == Primary && after.Epoch > before.Epoch, "the surviving runtime took the partition at a later epoch");
            await Until(async () => await Image(target) is { } i && PostgresFlowchartPreview.State(i) is { Agenda.Count: 0 } s &&
                s.Frames.Values.Any(f => f.Node == "risk") && s.Frames.Values.Any(f => f.Node == "finance"));
            var state = PostgresFlowchartPreview.State((await Image(target))!);
            var finance = PostgresFlowchartPreview.Frame(state, "finance");
            async Task Signal(string op, string frame, string value, Contract.AdmissionOutcome outcome)
            {
                var input = Json.Write(new CompositeSignal(frame, "approve", value));
                await PostgresMultiRootClient.Send(api, scope, target.Ref, op, input, outcome, stop);
            }
            await Signal("risk-approval", PostgresFlowchartPreview.Frame(state, "risk"), "Risk accepted", Contract.AdmissionOutcome.Accepted);
            await Until(async () => await Image(target) is { } i && PostgresFlowchartPreview.State(i) is { Agenda.Count: 0 } s &&
                s.Results.ContainsKey("risk") && s.Frames.Values.All(f => f.Node != "risk"));
            var waiting = (await Image(target))!;
            await Signal("risk-approval", finance, "changed body", Contract.AdmissionOutcome.Duplicate);
            var afterReplay = await Image(target);
            Check(waiting.Revision == 6 && afterReplay?.Revision == waiting.Revision && afterReplay.State == waiting.State,
                "the changed risk replay preserves the exact waiting checkpoint before finance approval");
            await Signal("finance-approval", finance, "Finance accepted", Contract.AdmissionOutcome.Accepted);
            await Until(async () => await Image(target) is { Status: InstanceStatus.Finished });
            var finished = (await Image(target))!;
            state = PostgresFlowchartPreview.State(finished);
            var suffix = await api.TailAsync(scope, target.Ref, birth.Next, stop);
            Check(finished.Revision == waiting.Revision + 1 &&
                state.Results["risk"]?.GetValue<string>() == "Risk accepted" &&
                state.Results["finance"]?.GetValue<string>() == "Finance accepted" &&
                state.Variables["ready"]?.GetValue<bool>() == true && suffix.Items.Count(PostgresFlowchartPreview.IsJoin) == 1 &&
                applied.GetValueOrDefault(InboxKey.Issued(persisted!)) == 1, "the successor applied the original stored continuation and joined once");
            await Signal("finance-approval", finance, "changed again", Contract.AdmissionOutcome.Duplicate);
            Check((await Image(target))!.Revision == finished.Revision && (await api.TailAsync(scope, target.Ref, birth.Next, stop)).Items.SequenceEqual(suffix.Items) &&
                await Placement(target) == after, "stable public result and successor epoch");
            await PostgresMultiRootClient.CallSecondaryAsync(stop, "missing-durable", target.Ref.Id);
            await using var final = Open();
            var journal = await final.Journal.Where(j => j.Instance == target.Actor.Id).OrderBy(j => j.Position).Select(j => j.Revision).ToListAsync(stop);
            var positions = await final.Journal.Where(j => j.Partition == target.Actor.Partition).OrderBy(j => j.Position).Select(j => j.Position).ToListAsync(stop);
            Check(journal.SequenceEqual([1L, 2L, 3L, 6L, 7L]) && positions.SequenceEqual(Enumerable.Range(1, positions.Count).Select(i => (long)i)) &&
                !await final.Messages.AnyAsync(m => m.Target == target.Actor.Id && m.InboxScope == EfStore.RootSelfScope, stop),
                "silent actor revisions preserve the gapless partition journal and leave no private message");
            Console.WriteLine($"ok PostgreSQL Durable Flowchart Cluster: {partitions} partitions, processes {Environment.ProcessId} and {peerProcess.Id}, partition {target.Actor.Partition}, peer epoch {before.Epoch}, successor epoch {after.Epoch}, original continuation applied once, revisions 1/2/3/6/7, one join.");
        }

        async Task Start(bool purchase = false)
        {
            var spec = purchase
                ? NativeExecutionModel.Start(PostgresFlowchartPreview.Definition, Modes.Durable, Wf.Object(("manualReview", true)))
                : NativeExecutionModel.Start(DirectApprovalWorkflow.Definition);
            var reply = await workflow.StartAsync(scope, "cluster-start-" + roots.Count, spec, cancellationToken: stop);
            Check(reply.Outcome == Contract.AdmissionOutcome.Accepted, "fresh public start");
            var reference = reply.Actor!.Value;
            var address = (await admission.ResolveAsync(scope.Tenant, reference.Id, stop))!;
            roots.Add(new(reference, new(address.Id)));
        }
        async Task Send(Root root, string id, string value, Contract.AdmissionOutcome expected)
        {
            var frame = Json.Read<NativeState>((await Image(root))!.State!).Frames.Single().Key;
            var input = Json.Write(new CompositeSignal(frame, "approve", value));
            await PostgresMultiRootClient.Send(api, scope, root.Ref, id, input, expected, stop);
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
