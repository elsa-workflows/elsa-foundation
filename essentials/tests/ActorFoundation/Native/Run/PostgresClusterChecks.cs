using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Elsa.Workflows.Runtime.Statecharts.OrderFixture;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresClusterChecks
{
    private const string Primary = "preview-primary", Peer = "preview-peer";
    private sealed record Root(Contract.ActorRef Ref, InstanceRef Actor);
    private sealed record Ownership(string? Node, long Epoch);

    internal static async Task<int> RunAsync(int partitions, bool peer, bool abrupt = false, bool flowchart = false, bool statechart = false)
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
        var specialized = flowchart || statechart;
        if (!peer)
        {
            await store.CreateAsync(partitions);
            if (specialized) await admission.InstallAsync(stop);
            else await store.InstallPreviewRootSchemaAsync(stop);
        }
        var name = peer ? Peer : Primary;
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());
        var applied = new ConcurrentDictionary<InboxKey, int>();
        var worker = new WorkerOptions { Partitions = partitions };
        if (flowchart)
        {
            await store.EnableRootSelfSendsAsync(stop);
            registry.Add(PostgresFlowchartPreview.Build());
        }
        if (statechart) registry.Add(OrderStatechart.Build());
        if (flowchart || statechart)
        {
            int? emitting = null;
            worker = worker with
            {
                OnPreparedCheckpoint = (turn, _, _) =>
                {
                    if (!peer) return;
                    if ((flowchart && turn.Row is { Revision: 1 } && turn.Sent.Any(m => m.InboxKey.Scope == -1)) ||
                        (statechart && turn.Row is { Revision: 2 }))
                        emitting = new InstanceRef(turn.Instance).Partition;
                },
                OnJournal = written =>
                {
                    // Pause the fixture peer after its selected commit lands. The driver stops the peer process.
                    if (peer && emitting is { } home && written.Contains(home)) stop.WaitHandle.WaitOne();
                },
                OnTurn = (_, messages, _) =>
                {
                    if (!peer)
                        foreach (var m in messages.Where(m => statechart ? m.Id == PostgresStatechartPreview.Shipment : m.InboxKey.Scope == -1))
                            applied.AddOrUpdate(m.InboxKey, 1, (_, count) => count + 1);
                }
            };
        }
        var loopOptions = new LoopOptions
            { BalanceEvery = TimeSpan.FromMilliseconds(150), Window = TimeSpan.FromSeconds(2), PollInterval = TimeSpan.FromMilliseconds(150) };
        if (peer)
        {
            await using var peerNode = await StartNodeAsync();
            await Console.In.ReadLineAsync(stop);
            await peerNode.StopAsync(stop);
            Healthy(peerNode);
            return 0;
        }

        var scope = EmbeddedTenant.Bind("cluster");
        Contract.IIngress ingress;
        Contract.IActorReads reads;
        if (specialized)
        {
            var api = new PostgresPreviewFoundation(admission, new(store, await admission.OpenJournalCursorAsync(stop), 64),
                WorkflowActor.WorkflowActor.ActorType);
            ingress = api;
            reads = api;
        }
        else
        {
            ingress = await store.OpenPreviewRootIngressAsync(WorkflowActor.WorkflowActor.ActorType, TimeSpan.FromHours(1), stop);
            reads = await store.OpenPreviewRootReadsAsync(64, stop);
        }
        var workflow = new WorkflowRuntime(ingress, reads);
        var roots = new List<Root>();
        do { await Start(); } while (roots.Select(r => r.Actor.Partition).Distinct().Count() < 2);
        await using var node = await StartNodeAsync();
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
        info.ArgumentList.Add(statechart ? "pg-cluster-statechart-peer" : flowchart ? "pg-cluster-flowchart-peer" : "pg-cluster-peer");
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
            if (flowchart || statechart)
            {
                if (statechart) await StatechartHandoverAsync(child);
                else await FlowchartHandoverAsync(child);
                await node.StopAsync(stop);
                Healthy(node);
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
            var prefix = await reads.TailAsync(scope, target.Ref, cancellationToken: stop);
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
            Check((await reads.TailAsync(scope, target.Ref, prefix.Next, stop)).Items.Count == 0, "replay added no public journal entry");
            await Send(target, "approve-2", "Grace", Contract.AdmissionOutcome.Accepted);
            await Until(async () => await Image(target) is { Status: InstanceStatus.Finished });
            var image = (await Image(target))!;
            var results = Json.Read<NativeState>(image.State!).Results["approve"]!.AsArray().Select(x => x!.GetValue<string>());
            var suffix = await reads.TailAsync(scope, target.Ref, prefix.Next, stop);
            Check(suffix.Items.All(e => e.Revision == 3 && PostgresPublicPreview.Decode(e) != "approved:approve-1") &&
                suffix.Items.Count(e => PostgresPublicPreview.Decode(e) == "approved:approve-2") == 1,
                "the pre-exit bookmark returns only the third revision and its single second approval");
            var journal = await reads.TailAsync(scope, target.Ref, cancellationToken: stop);
            Check(journal.Items.Count(e => PostgresPublicPreview.Decode(e) == "approved:approve-1") == 1 &&
                journal.Items.Count(e => e.Revision == 3 && PostgresPublicPreview.Decode(e) == "approved:approve-2") == 1,
                "public journal contains both applied approvals once");
            Check(image.Revision == 3 && results.SequenceEqual(["Ada", "Grace"]) &&
                journal.Items.Select(x => x.Revision).Distinct().SequenceEqual([1L, 2L, 3L]), "exactly one approval of each kind landed across handover");
            await PostgresMultiRootClient.CallSecondaryAsync(stop, "missing", target.Ref.Id);
            await node.StopAsync(stop);
            Healthy(node);
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
                await Start(NativeExecutionModel.Start(PostgresFlowchartPreview.Definition, Modes.Durable, Wf.Object(("manualReview", true))));
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
            var birth = await reads.TailAsync(scope, target.Ref, cancellationToken: stop);
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
                await PostgresMultiRootClient.Send(ingress, scope, target.Ref, op, input, outcome, stop);
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
            var suffix = await reads.TailAsync(scope, target.Ref, birth.Next, stop);
            Check(finished.Revision == waiting.Revision + 1 &&
                state.Results["risk"]?.GetValue<string>() == "Risk accepted" &&
                state.Results["finance"]?.GetValue<string>() == "Finance accepted" &&
                state.Variables["ready"]?.GetValue<bool>() == true && suffix.Items.Count(PostgresFlowchartPreview.IsJoin) == 1 &&
                applied.GetValueOrDefault(InboxKey.Issued(persisted!)) == 1, "the successor applied the original stored continuation and joined once");
            await Signal("finance-approval", finance, "changed again", Contract.AdmissionOutcome.Duplicate);
            Check((await Image(target))!.Revision == finished.Revision && (await reads.TailAsync(scope, target.Ref, birth.Next, stop)).Items.SequenceEqual(suffix.Items) &&
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

        async Task StatechartHandoverAsync(Process peerProcess)
        {
            Root target;
            Ownership before;
            do
            {
                await Start(NativeExecutionModel.Start(OrderStatechart.Definition));
                target = roots[^1];
                before = await Placement(target);
            } while (before.Node != Peer);

            await Until(async () => await Image(target) is { Revision: 1 });
            var (frame, _) = PostgresStatechartPreview.Configuration((await Image(target))!, "AwaitingPayment");
            await Signal(PostgresStatechartPreview.Payment, "declined", Contract.AdmissionOutcome.Accepted);
            await Until(async () => await Image(target) is { Revision: 2 });
            var waiting = (await Image(target))!;
            var (waitingFrame, shipmentVisit) = PostgresStatechartPreview.Configuration(waiting, "ReleasingShipment");
            var prefix = await reads.TailAsync(scope, target.Ref, cancellationToken: stop);
            Check(waitingFrame == frame && await Placement(target) == before && prefix.Next is not null &&
                PostgresStatechartPreview.ChartEntries(prefix.Items).Where(e => e[2] == "entry").Select(e => e[3])
                    .SequenceEqual(["AwaitingPayment", "ReleasingShipment"]),
                "the peer committed the declined payment and a bookmark before the shipment acknowledgement");

            var shipment = PostgresStatechartPreview.Shipment;
            var body = Body("shipment-released");
            await Signal(shipment, "shipment-released", Contract.AdmissionOutcome.Accepted);
            var pending = (await admission.ReadPendingDirectAsync(scope.Tenant, target.Ref.Id, stop)).Single();
            Check(pending.Id == shipment && pending.Kind == CompositeSignal.Kind && pending.Payload == body,
                "the exact frame-addressed shipment acknowledgement is pending on the paused peer");

            await using (var guard = Open())
            await using (var transaction = await guard.Database.BeginTransactionAsync(stop))
            {
                var held = await guard.Database.SqlQuery<Ownership>($"""
                    SELECT "Owner" AS "Node", "Epoch" FROM "Partitions" WHERE "Id" = {target.Actor.Partition} FOR UPDATE
                    """).SingleAsync(stop);
                Check(held == before && !peerProcess.HasExited, "the live peer still owns the locked target fence");
                peerProcess.Kill();
                await peerProcess.WaitForExitAsync(stop);
                Check(SameCheckpoint(await Image(target), waiting) &&
                    (await admission.ReadPendingDirectAsync(scope.Tenant, target.Ref.Id, stop)).SequenceEqual([pending]),
                    "the peer exited with the same committed checkpoint and original acknowledgement still present");
                await transaction.CommitAsync(stop);
            }
            await Until(async () => await Count(Primary) == partitions && await Image(target) is { Revision: 3 });
            var successor = await Placement(target);
            var advanced = (await Image(target))!;
            var (currentFrame, stockVisit) = PostgresStatechartPreview.Configuration(advanced, "ReleasingStock");
            var suffix = await reads.TailAsync(scope, target.Ref, prefix.Next, stop);
            Check(successor.Node == Primary && successor.Epoch > before.Epoch && currentFrame == frame && stockVisit != shipmentVisit &&
                suffix.Next is not null && suffix.Items.All(e => e.Revision == 3) &&
                PostgresStatechartPreview.ChartEntries(suffix.Items).Select(e => (e[0], e[1], e[2], e[3])).SequenceEqual(
                [
                    (frame, shipment, "evaluated", "shipment-released"),
                    (frame, shipment, "exit", "ReleasingShipment"),
                    (frame, shipment, "transition", "ReleasingShipment"),
                    (frame, shipment, "entry", "ReleasingStock"),
                    (frame, shipment, "handled", "")
                ]) && applied.GetValueOrDefault(pending.InboxKey) == 1,
                "the surviving owner applied the original shipment acknowledgement once and recorded one transition");

            await Signal(shipment, "stock-released", Contract.AdmissionOutcome.Duplicate);
            Check(SameCheckpoint(await Image(target), advanced) && await Placement(target) == successor &&
                (await admission.ReadPendingDirectAsync(scope.Tenant, target.Ref.Id, stop)).Count == 0 &&
                (await reads.TailAsync(scope, target.Ref, suffix.Next, stop)).Items.Count == 0 &&
                (await reads.TailAsync(scope, target.Ref, prefix.Next, stop)).Items.SequenceEqual(suffix.Items) &&
                applied.GetValueOrDefault(pending.InboxKey) == 1,
                "a changed-payload replay preserved the checkpoint and journal and queued no direct input");
            Console.WriteLine($"ok PostgreSQL Balanced StateMachine Cluster: {partitions} partitions, processes {Environment.ProcessId} and {peerProcess.Id}, partition {target.Actor.Partition}, peer epoch {before.Epoch}, successor epoch {successor.Epoch}, ReleasingShipment -> ReleasingStock once, replay Duplicate.");

            string Body(string trigger) => JsonSerializer.Serialize(new CompositeSignal(frame, trigger));
            Task Signal(string operation, string trigger, Contract.AdmissionOutcome expected) =>
                PostgresMultiRootClient.Send(ingress, scope, target.Ref, operation, Body(trigger), expected, stop);
        }

        async Task Start(Models.WorkflowStart? spec = null)
        {
            spec ??= NativeExecutionModel.Start(DirectApprovalWorkflow.Definition);
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
            await PostgresMultiRootClient.Send(ingress, scope, root.Ref, id, input, expected, stop);
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
            while (!await condition()) { Healthy(node); await Task.Delay(40, stop); }
        }
        async Task<RunningNode> StartNodeAsync()
        {
            if (!specialized)
            {
                var builder = Host.CreateEmptyApplicationBuilder(new());
                builder.Logging.ClearProviders();
                var service = new PostgresPreviewHostedService(Open, name,
                    new WorkflowActor.WorkflowActor([new NativeExecutionModel(registry)]), Environment.FailFast, loopOptions);
                builder.Services.AddHostedService(_ => service);
                var application = builder.Build();
                try
                {
                    await application.StartAsync(stop);
                    return new(() => service.LastError, application.StopAsync, () => DisposeHostAsync(application));
                }
                catch
                {
                    await DisposeHostAsync(application);
                    throw;
                }
            }

            var host = PostgresNativeRootOwner.PrepareNode(store, name, partitions, registry, worker);
            var runtime = new RuntimeNode(name, store, partitions, loopOptions);
            try
            {
                await runtime.StartAsync(host, stop);
                return new(() => runtime.LastError, token => runtime.StopAsync(token).AsTask(), runtime.DisposeAsync);
            }
            catch
            {
                await runtime.KillAsync();
                throw;
            }
        }

        void Healthy(RunningNode running)
        {
            if (running.LastError is { } ex) throw new InvalidOperationException(name + " runtime error", ex);
        }
    }

    private static bool SameCheckpoint(EfStore.RootImage? actual, EfStore.RootImage expected) =>
        actual is not null && (actual with { Labels = expected.Labels, AsOf = expected.AsOf }) == expected &&
        actual.Labels.Count == expected.Labels.Count &&
        actual.Labels.All(label => expected.Labels.TryGetValue(label.Key, out var value) && label.Value == value);

    private static void Check(bool pass, string detail)
    {
        if (!pass) throw new InvalidOperationException(detail);
    }
    private sealed class RunningNode(Func<Exception?> readError, Func<CancellationToken, Task> stop, Func<ValueTask> dispose)
        : IAsyncDisposable
    {
        internal Exception? LastError => readError();

        internal Task StopAsync(CancellationToken cancellationToken) => stop(cancellationToken);

        public ValueTask DisposeAsync() => dispose();
    }

    private static ValueTask DisposeHostAsync(IHost app)
    {
        if (app is IAsyncDisposable asyncHost) return asyncHost.DisposeAsync();
        app.Dispose();
        return ValueTask.CompletedTask;
    }

}
