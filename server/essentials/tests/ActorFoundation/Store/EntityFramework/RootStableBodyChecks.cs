using System.Text;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class RootStableBodyChecks
{
    internal static async Task Run(Func<KernelDb> create, EfStore store, EfStore cold)
    {
        const int cap = 16 * 1024 * 1024 - 24 * 1024;
        const string tenant = "stable-body";
        const string suffix = "\0\ud800🌐\udc00";
        var lease = TimeSpan.FromMinutes(2);
        var ingress = new PostgresRootAdmission(create, lease);
        await ingress.InstallAsync();
        await store.BindWorkerPartitionAsync(0, default);
        var taken = (await store.TakeAsync(0, "stable-body", lease))!;
        var fence = new PartitionFence(0, taken.Epoch, taken.Position);
        var small = new Dictionary<string, string>();
        var maxType = new string('\u0001', 16 * 1024);
        var maxLabels = Enumerable.Range(1, 4).ToDictionary(index => new string((char)index, 1024), _ => new string('\u0001', 1024));

        var calibration = await Admit("calibration");
        Check((await Birth(calibration, null)).Landed && (await Bytes(calibration)).WireBytes is null &&
              (await Read(calibration)).State is null, "absent state remains absent through the binary layout");
        Advance();
        var mark = await Add(calibration, "empty");
        Check((await Write(calibration, mark, "", ended: false)).Landed &&
              (await Bytes(calibration)).WireBytes == 0 && (await Read(calibration)).State == "",
            "an empty state is a present zero-length wire value");
        Advance();
        var end = await Add(calibration, "end");
        Check((await Write(calibration, end, suffix, ended: true)).Landed, "the maximal header and opaque terminal state calibrate");
        Advance();
        var baseWire = await Bytes(calibration);
        Check(baseWire.WireBytes == Encoding.UTF8.GetByteCount(suffix) && (await Read(calibration)).State == suffix,
            "NUL, isolated UTF-16 units and astral text retain both bytes and meaning");
        var pad = new string('\u0001', cap - baseWire.Total);
        const int reservedGrowth = 64;
        var live = pad[..^reservedGrowth];
        var target = await Admit("candidate");
        // Reserve the exact final body's byte growth plus the largest allowed encoded header.
        var final = live + suffix + new string('x', reservedGrowth);
        var startBody = live + new string('x', Encoding.UTF8.GetByteCount(suffix));

        await RequireWorker(() => cold.CommitFirstRootAsync(tenant, target.Reference, target.Creator, fence, lease,
            "state", 1, "", small, """["birth"]"""));
        await Reject(() => Birth(target, startBody + "x", reservedGrowth));
        Check(await cold.ReadFirstRootAsync(tenant, target.Reference) is null &&
              await ingress.ReadPendingCreatorAsync(tenant, target.Reference) == target.Creator &&
              (await cold.ReadJournalOfAsync(0, target.Creator.Target.Id, 0)).Count == 0 && await Position() == fence.Position,
            "one byte above projected birth preserves its creator and leaves the fence unchanged");
        await Reject(() => Birth(target, startBody, reservedGrowth + 1));
        Check((await Birth(target, startBody, reservedGrowth)).Landed, "live birth reserves terminal body growth at the exact projection");
        Advance();

        var update = await Add(target, "update");
        var saved = await Read(target);
        await RequireWorker(() => cold.CommitRootTurnAsync(tenant, target.Reference, saved, [update], fence, lease,
            InstanceStatus.Running, "state", 1, "", small, """["advance"]"""));
        await Reject(() => Write(target, update, startBody, ended: false, growth: reservedGrowth + 1));
        Check((await Read(target)).State == saved.State && (await Read(target)).Revision == saved.Revision &&
              await Position() == fence.Position && (await ingress.ReadPendingDirectAsync(tenant, target.Reference)).Single() == update,
            "one byte above projected update leaves the pending input and original checkpoint intact");
        Check((await Write(target, update, startBody, ended: false, growth: reservedGrowth)).Landed,
            "the exactly reserved live edit settles its input");
        Advance();

        var suspend = await AdmitControl(target.Reference, "pause", Kinds.Suspend);
        await Reject(() => SetSuspended(target.Creator.Target.Id, suspend, true, reservedGrowth + 1));
        Check((await Read(target)) is { Revision: 2, Suspended: false } && await Position() == fence.Position &&
              await ingress.ReadDueRootOperatorAsync(tenant, target.Reference, default) == suspend,
            "one byte above the declared suspension reserve preserves the row and command");
        Check((await SetSuspended(target.Creator.Target.Id, suspend, true, reservedGrowth)).Landed && await Position() == fence.Position,
            "the exact suspended checkpoint lands without advancing the journal position");
        var resume = await AdmitControl(target.Reference, "resume", Kinds.Resume);
        Check((await SetSuspended(target.Creator.Target.Id, resume, false, reservedGrowth)).Landed && await Position() == fence.Position,
            "the exact resumed checkpoint lands without advancing the journal position");

        var last = await Add(target, "finish");
        Check((await Write(target, last, final, ended: true)).Landed, "reshaping the saved text spends the reserved growth");
        var stored = await Bytes(target);
        var terminal = await Read(target);
        Check(stored.Total == cap && stored.WireBytes == Encoding.UTF8.GetByteCount(final) &&
              terminal is { TerminalRevision: 5, Status: InstanceStatus.Finished } && terminal.State == final &&
              terminal.TypeData == maxType && terminal.Labels.OrderBy(pair => pair.Key).SequenceEqual(maxLabels.OrderBy(pair => pair.Key)),
            "the largest header and changed opaque body land at the exact physical cap");
        Console.WriteLine($"stable wire {stored.WireBytes} bytes, checkpoint {stored.Total} bytes");

        async Task<(string Reference, Message Creator)> Admit(string token)
        {
            var receipt = await ingress.AdmitAsync(tenant, token, new("root", "shape"));
            return (receipt.Reference, (await ingress.ReadPendingCreatorAsync(tenant, receipt.Reference))!);
        }

        ValueTask<Committed> Birth((string Reference, Message Creator) root, string? state, long growth = 0)
        {
            var start = root.Creator.Start!;
            var header = new FoundationHeaderChange("", small);
            var row = new InstanceRow(root.Creator.Target.Id, start.Model, start.Definition, InstanceStatus.Running, "null", 1, null,
                History: start.History, StateVersion: 1, Creation: 1,
                StartIntent: CreationProtocol.Fingerprint(start, root.Creator.Kind, root.Creator.Payload)).WithFoundationState(state) with
            {
                FoundationDispatchHeader = new(tenant, root.Reference, header, growth)
            };
            return Submit(new(row.Id, row, null, [root.Creator.InboxKey], [], [], [], [], """["birth"]""") { FoundationHeader = header });
        }

        async Task<Message> Add((string Reference, Message Creator) root, string operation)
        {
            await ingress.AdmitDirectAsync(tenant, root.Reference, operation, "advance");
            return (await ingress.ReadPendingDirectAsync(tenant, root.Reference)).Single();
        }

        async ValueTask<Committed> Write((string Reference, Message Creator) root, Message input, string state, bool ended, long growth = 0)
        {
            var previous = (await store.LoadAsync([root.Creator.Target.Id])).Single();
            var header = new FoundationHeaderChange(ended ? maxType : "", ended ? maxLabels : small);
            var row = previous.WithFoundationState(state) with
            {
                Revision = previous.Revision + 1,
                Status = ended ? InstanceStatus.Finished : InstanceStatus.Running,
                FoundationDispatchHeader = new(tenant, root.Reference, header, growth)
            };
            return await Submit(new(row.Id, row, previous.Revision, [], [input.InboxKey], [], [], [],
                ended ? """["end"]""" : """["advance"]""", Unsubscribe: ended) { FoundationHeader = header });
        }

        async Task<Message> AdmitControl(string reference, string operation, string kind)
        {
            await ingress.AdmitRootOperatorAsync(tenant, reference, operation, kind, null, null, default);
            return (await ingress.ReadDueRootOperatorAsync(tenant, reference, default))!;
        }

        async ValueTask<Committed> SetSuspended(string actor, Message input, bool suspended, long growth)
        {
            var previous = (await store.LoadAsync([actor])).Single();
            var header = previous.FoundationDispatchHeader! with { TerminalGrowth = growth };
            var row = previous with { Revision = previous.Revision + 1, Suspended = suspended, FoundationDispatchHeader = header };
            return await Submit(new(row.Id, row, previous.Revision, [], [input.InboxKey], [], [], [], null) { FoundationRootOperator = input });
        }

        ValueTask<Committed> Submit(TurnCommit turn) => store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, lease,
            false, [turn], [], []));

        async Task<EfStore.RootImage> Read((string Reference, Message Creator) root) =>
            (await cold.ReadFirstRootAsync(tenant, root.Reference))!;

        async Task<Physical> Bytes((string Reference, Message Creator) root)
        {
            await using var db = create();
            return await db.Database.SqlQuery<Physical>($"""
                SELECT octet_length("FoundationStateWire") AS "WireBytes",
                    octet_length("State") + octet_length("FoundationHeader") + coalesce(octet_length("FoundationStateWire"), 0) AS "Total"
                FROM "Instances" WHERE "Id" = {root.Creator.Target.Id}
                """).SingleAsync();
        }

        async Task<long> Position()
        {
            await using var db = create();
            return await db.Partitions.Select(row => row.Position).SingleAsync();
        }

        void Advance() => fence = fence with { Position = fence.Position + 1 };
    }

    private sealed record Physical(int? WireBytes, int Total);

    private static async Task Reject(Func<ValueTask<Committed>> commit)
    {
        try { await commit(); }
        catch (StoreException error) when (error is
            { Failure: Failure.Definite, Message: "The encoded PostgreSQL checkpoint exceeds its storage budget." })
        {
            Console.WriteLine("ok the provider refuses the excess projected byte");
            return;
        }
        throw new InvalidOperationException("The provider accepted growth beyond the stable-body reserve.");
    }

    private static async Task RequireWorker(Func<ValueTask<Committed>> write)
    {
        try { await write(); }
        catch (NotSupportedException error) when (
            error.Message == "The PostgreSQL stable-body profile requires the qualified Worker checkpoint writer.")
        {
            Console.WriteLine("ok the format-12 manual write requires the qualified Worker");
            return;
        }
        throw new InvalidOperationException("An unqualified checkpoint writer accepted format 12.");
    }

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
