using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Native;
using Kernel.Runtime.Hosting;
using Kernel.Statecharts.Native;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresStatechartPreview
{
    internal const string Payment = "payment-decision";
    internal const string Shipment = "shipment-release-confirmed";

    internal sealed record Restart(string Reference, string Bookmark, long Epoch, long Revision, string StateHash, string Occurrence);

    internal static async Task<int> RunAsync(Restart? restart)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stop = deadline.Token;
        var open = PostgresMultiRootClient.Database();
        var store = new EfStore(open, postgres: true);
        var admission = new PostgresRootAdmission(open, TimeSpan.FromHours(1));
        if (restart is null)
        {
            await store.CreateAsync(1);
            await admission.InstallAsync(stop);
        }

        var scope = FixedTenantScope.Bind("orders");
        var api = new PostgresPreviewFoundation(admission, store, new(store, await admission.OpenJournalCursorAsync(stop), 4),
            WorkflowActor.WorkflowActor.ActorType);
        var registry = new WorkflowRegistry().Add(OrderStatechart.Build());
        var host = await PostgresNativeRootOwner.PreparePartitionAsync(store, $"order-{Environment.ProcessId}", 0, registry, 1, stop);
        await using var owner = await host.TryActivateAsync(stop)
            ?? throw new InvalidOperationException("The Embedded owner could not take its partition.");
        var worker = (Worker)owner;
        var epoch = worker.Fences.Single().Epoch;
        var actor = restart is null ? await StartAsync() : new Contract.ActorRef(restart.Reference);
        using var activityOutput = new StringWriter();
        var previousOutput = WriteLine.Out;
        WriteLine.Out = activityOutput;
        try
        {
            if (restart is null)
            {
                var initial = await DriveAsync(1);
                var (frame, _) = Configuration(initial, "AwaitingPayment");
                await SignalAsync(frame, Payment, "declined", Contract.AdmissionOutcome.Accepted);
                var waiting = await DriveAsync(initial.Revision + 1);
                Check(Configuration(waiting, "ReleasingShipment").Frame == frame,
                    "The declined payment must advance the same machine.");
                Check(ActivityOutput().SequenceEqual(["reserve stock", "book shipment", "release shipment"]),
                    "Stock reservation, shipment booking and shipment release must have run in order.");
                var (history, end) = await HistoryAsync(null);
                Check(end is not null && ChartEntries(history).Where(e => e[2] == "entry").Select(e => e[3])
                    .SequenceEqual(["AwaitingPayment", "ReleasingShipment"]), "The public history must show the order waiting for shipment release.");

                await SignalAsync(frame, Shipment, "shipment-released", Contract.AdmissionOutcome.Accepted);
                var occurrence = await PendingShipmentAsync(frame);
                Check((await ImageAsync() is { } afterAdmission && afterAdmission.Revision == waiting.Revision &&
                    afterAdmission.State == waiting.State) && (await HistoryAsync(end)).Entries.Count == 0,
                    "The owner must stop with the shipment acknowledgement admitted and the chart still waiting.");
                Console.WriteLine($"waiting {Environment.ProcessId} {actor.Id} {end!.Value.Value} {epoch} {waiting.Revision} {Hash(waiting.State!)} {occurrence}");
                return 0;
            }

            Check(epoch > restart.Epoch, "The cold owner must take the partition at a higher epoch.");
            var before = await ImageAsync() ?? throw new InvalidOperationException("The cold owner could not read the order checkpoint.");
            Check(before.Revision == restart.Revision && before.State is not null && Hash(before.State) == restart.StateHash,
                "The cold owner must read the same committed Native checkpoint before running.");
            var (restoredFrame, shipmentVisit) = Configuration(before, "ReleasingShipment");
            var bookmark = new Contract.PageToken(restart.Bookmark);
            Check(await PendingShipmentAsync(restoredFrame) == restart.Occurrence && (await HistoryAsync(bookmark)).Entries.Count == 0,
                "The exact shipment acknowledgement must still be pending at cold start.");

            var after = await DriveAsync(before.Revision + 1);
            var (sameFrame, stockVisit) = Configuration(after, "ReleasingStock");
            var (tail, next) = await HistoryAsync(bookmark);
            Check(after.Revision == before.Revision + 1 && after.Actor == before.Actor && sameFrame == restoredFrame && stockVisit != shipmentVisit &&
                (await api.GetAsync(scope, actor, stop)) is { Header.Status: Contract.ActorStatus.Running },
                "The same order must settle exactly one new revision and wait for stock release.");
            Check(await NoPendingDirectAsync() && ActivityOutput().SequenceEqual(["release stock"]),
                "The cold owner must consume the admitted acknowledgement and run stock release once.");
            var entries = ChartEntries(tail);
            Check(next is not null && tail.All(row => row.Revision == after.Revision) &&
                entries.All(e => e is [var frame, Shipment, _, _] && frame == restoredFrame) &&
                entries.Select(e => (e[2], e[3])).SequenceEqual(
                [
                    ("evaluated", "shipment-released"),
                    ("exit", "ReleasingShipment"),
                    ("transition", "ReleasingShipment"),
                    ("entry", "ReleasingStock"),
                    ("handled", "")
                ]),
                "The public journal must contain one evaluated, ordered and handled shipment transition.");

            var target = new InstanceRef(after.Actor.Id);
            var inbox = await InboxSnapshotAsync(target);
            await SignalAsync(restoredFrame, Shipment, "stock-released", Contract.AdmissionOutcome.Duplicate);
            Check((await InboxSnapshotAsync(target)).SequenceEqual(inbox),
                "A duplicate acknowledgement must write no inbox row, including a settled row.");
            await PassAsync();
            Check(await ImageAsync() is { } replayed && replayed.Revision == after.Revision && replayed.State == after.State &&
                (await InboxSnapshotAsync(target)).SequenceEqual(inbox) && (await HistoryAsync(next)).Entries.Count == 0 &&
                (await HistoryAsync(bookmark)).Entries.SequenceEqual(tail) && ActivityOutput().SequenceEqual(["release stock"]),
                "A changed-payload replay must leave the checkpoint, inbox, journal and activity output unchanged.");
            Console.WriteLine($"advanced {Environment.ProcessId} {actor.Id} revision {after.Revision} epoch {epoch} ReleasingShipment -> ReleasingStock transitions 1 replay Duplicate");
            return 0;
        }
        finally
        {
            WriteLine.Out = previousOutput;
        }

        string[] ActivityOutput() => activityOutput.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        async Task<Contract.ActorRef> StartAsync()
        {
            var runtime = new WorkflowRuntime(api, api);
            var started = await runtime.StartAsync(scope, "order-start", NativeExecutionModel.Start(OrderStatechart.Definition),
                cancellationToken: stop);
            return started is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } accepted }
                ? accepted : throw new InvalidOperationException("Use a fresh preview database.");
        }

        Task<EfStore.RootImage?> ImageAsync() => store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop);

        async Task<EfStore.RootImage> DriveAsync(long revision)
        {
            while (true)
            {
                if (await ImageAsync() is { } image && image.Revision >= revision) return image;
                await PassAsync();
                await Task.Delay(10, stop);
            }
        }

        async Task PassAsync()
        {
            await owner.WokenAsync(null, stop);
            await owner.RunAsync(stop);
            Check(worker.Owns, "The owner must retain its fence while running the order.");
        }

        Task SignalAsync(string frame, string operation, string trigger, Contract.AdmissionOutcome outcome) =>
            PostgresMultiRootClient.Send(api, scope, actor, operation, JsonSerializer.Serialize(new CompositeSignal(frame, trigger)), outcome, stop);

        async Task<string> PendingShipmentAsync(string frame)
        {
            var pending = await admission.ReadPendingDirectAsync(scope.Tenant, actor.Id, stop);
            Check(pending is [{ Id: Shipment, Kind: CompositeSignal.Kind, Payload: { } payload }] &&
                JsonSerializer.Deserialize<CompositeSignal>(payload) == new CompositeSignal(frame, "shipment-released"),
                "The committed inbox must contain exactly the frame-addressed shipment acknowledgement.");
            return pending[0].InboxKey.Id;
        }

        async Task<bool> NoPendingDirectAsync() => (await admission.ReadPendingDirectAsync(scope.Tenant, actor.Id, stop)).Count == 0;

        async Task<List<string>> InboxSnapshotAsync(InstanceRef target)
        {
            await using var db = open();
            return await db.Database.SqlQuery<string>($"""
                SELECT to_jsonb(m)::text AS "Value" FROM "Messages" m
                WHERE m."Target" = {target.Id} AND m."Partition" = {target.Partition}
                ORDER BY m."Seq", m."InboxScope", m."Id"
                """).ToListAsync(stop);
        }

        async Task<(List<Contract.JournalEntry> Entries, Contract.PageToken? End)> HistoryAsync(Contract.PageToken? from)
        {
            var entries = new List<Contract.JournalEntry>();
            while (true)
            {
                var page = await api.TailAsync(scope, actor, from, stop);
                entries.AddRange(page.Items);
                if (page.Next is not { } next) return (entries, from);
                Check(next != from, "The public journal cursor must advance.");
                from = next;
            }
        }
    }

    internal static (string Frame, string Visit) Configuration(EfStore.RootImage image, string expected)
    {
        var native = Json.Read<NativeState>(image.State!);
        var frame = native.Frames.Values.Single(f => f.Node == OrderStatechart.Node);
        var chart = frame.State?.Deserialize<MachineState>();
        Check(image is { Status: InstanceStatus.Running, History: History.Full } && native.Mode == Modes.Balanced && native.Agenda.Count == 0 &&
            chart is { Active: [var state], Pending: null, Visits: [var visit] } && state == expected &&
            visit.State == expected && visit.Occurrence.StartsWith(frame.Id + "/state/", StringComparison.Ordinal),
            $"Expected the persisted order machine to be settled in {expected}.");
        return (frame.Id, chart!.Visits![0].Occurrence);
    }

    internal static string?[][] ChartEntries(IEnumerable<Contract.JournalEntry> entries)
    {
        const string prefix = "statechart:";
        var chart = new List<string?[]>();
        foreach (var entry in entries)
        {
            using var json = JsonDocument.Parse(entry.Entry);
            if (json.RootElement.ValueKind == JsonValueKind.String &&
                json.RootElement.GetString() is { } text && text.StartsWith(prefix, StringComparison.Ordinal))
                chart.Add(JsonSerializer.Deserialize<string?[]>(text[prefix.Length..])!);
        }
        return chart.ToArray();
    }

    private static string Hash(string state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
