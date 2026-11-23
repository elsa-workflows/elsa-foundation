using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresPublicPreview
{
    internal static async Task<int> RunAsync(string phase, string? suppliedReference = null, string? suppliedBookmark = null)
    {
        var birth = phase == "birth";
        if (!birth && (phase is not ("restart" or "terminal") || suppliedReference is null || suppliedBookmark is null))
            throw new ArgumentException("Supply birth or restart with its reference and bookmark.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stop = timeout.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
            throw new InvalidOperationException("Set KERNEL_PREVIEW_POSTGRES to a dedicated preview database.");
        var counter = new Counting();
        var data = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        Wire? wire = null;
        var measuredConnection = connection;
        if (Environment.GetEnvironmentVariable("KERNEL_PREVIEW_MEASURE_WIRE") == "1")
        {
            var settings = new Npgsql.NpgsqlConnectionStringBuilder(connection);
            if (settings.Host != "127.0.0.1" || settings.SslMode != Npgsql.SslMode.Disable)
                throw new ArgumentException("The wire meter requires a dedicated plaintext loopback PostgreSQL connection.");
            if (settings.GssEncryptionMode == Npgsql.GssEncryptionMode.Require)
                throw new ArgumentException("The wire meter cannot measure a connection that requires GSS encryption.");
            settings.GssEncryptionMode = Npgsql.GssEncryptionMode.Disable;
            wire = Wire.Start("postgres", $"Host=127.0.0.1;Port={settings.Port}");
            settings.Port = wire.LocalPort;
            measuredConnection = settings.ConnectionString;
        }
        var measured = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(measuredConnection)
            .AddInterceptors(counter, counter.Transactions).Options;
        long startTrips = 0, startGroups = 0, startConnections = 0;
        var store = new EfStore(() => new(measured), postgres: true);
        var probe = new EfStore(() => new(data), postgres: true);
        var admission = new PostgresRootAdmission(() => new(measured), TimeSpan.FromHours(1));
        var scope = new Contract.BoundScope("preview");

        if (birth)
        {
            await store.CreateAsync();
            await admission.InstallAsync(stop);
        }
        var cursor = await admission.OpenJournalCursorAsync(stop);
        var tail = new PostgresRootJournalReader(store, cursor, 1);
        var foundation = new PostgresPreviewFoundation(admission, store, tail, PublicActor.ActorType);
        var workflow = new WorkflowRuntime(foundation, foundation);
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());
        Report("host setup");

        Contract.ActorRef actor;
        Reset();
        if (birth)
        {
            var created = await workflow.StartAsync(scope, Guid.NewGuid().ToString("N"),
                NativeExecutionModel.Start(DirectApprovalWorkflow.Definition), cancellationToken: stop);
            Check(created is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "public start admitted a root");
            actor = created.Actor!.Value;
            Report("start admission");
        }
        else
        {
            actor = new(suppliedReference!);
            Check(PostgresRootAdmission.IsIssuedReferenceShape(actor.Id), "restart received a root reference");
        }

        Reset();
        await using var owner = await PostgresNativeRootOwner.ActivateAsync(store, $"preview-{Environment.ProcessId}",
            scope.Tenant, actor.Id, registry, stop);
        Console.WriteLine($"owner {Environment.ProcessId}");
        Report("ownership");

        if (birth)
        {
            Reset();
            await Drive(1);
            Report("start turn");

            Reset();
            var frame = await ReadFrame();
            Check((await foundation.GetAsync(scope, actor, stop)) is { Header.Status: Contract.ActorStatus.Running, AsOf: > 0 },
                "public actor read sees the running checkpoint");
            Report("inspect first wait");

            Reset();
            Check(await Approve("approval-1", "Ada", frame) == Contract.AdmissionOutcome.Accepted, "first approval admitted");
            var checkpoint = await Drive(2);
            Check(Json.Read<NativeState>(checkpoint.State!).Frames[frame].State!.AsArray().Single()!.GetValue<string>() == "Ada",
                "first approval is present in the persisted wait state");
            Report("first approval including admission");

            Reset();
            var (entries, bookmark) = await ReadJournal();
            Check(entries.Count(e => e.Revision == 2 && Decode(e) == "approved:approval-1") == 1 && bookmark is not null,
                "the public journal records the first approval once");
            Report("read journal through revision 2");
            Console.WriteLine($"reference {actor.Id}");
            Console.WriteLine($"bookmark {bookmark!.Value.Value}");
        }
        else
        {
            var bookmark = new Contract.PageToken(suppliedBookmark!);
            Reset();
            var before = await ReadCheckpoint();
            Check(before is { Revision: 2, Status: InstanceStatus.Running }, "cold process loaded the second wait");
            Check((await foundation.TailAsync(scope, actor, bookmark, stop)) is { Items.Count: 0, AsOf: > 0 },
                "the previous process's bookmark resumes at the end of revision 2");
            var frame = await ReadFrame();
            Report("inspect after restart");

            Reset();
            Check(await Approve("approval-1", "alternative", frame) == Contract.AdmissionOutcome.Duplicate,
                "public ingress recognized the replay with changed payload");
            await owner.WokenAsync(null, stop);
            await owner.RunAsync(stop);
            Report("replay including admission");
            var same = await ReadCheckpoint();
            Check(same.Revision == before.Revision && same.State == before.State, "replay preserved the serialized workflow state and checkpoint revision");
            Check((await foundation.TailAsync(scope, actor, bookmark, stop)).Items.Count == 0, "replay added no journal entries");
            if (phase == "terminal")
            {
                foreach (var invalidKind in new[] { new string('x', 257), "input\ud800" })
                {
                    var rejected = await foundation.SendAsync(scope, new Contract.Target.Ref(actor), "invalid-kind", invalidKind,
                        cancellationToken: stop);
                    Check(rejected is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null, Reason: null },
                        "invalid ordinary kind metadata on the live root returned a generic refusal");
                }
                var normalStart = new Contract.StartSpec(PublicActor.ActorType, "definition");
                foreach (var (operation, spec) in new[]
                {
                    ("~reserved", normalStart), ("extra-data", normalStart with { TypeData = new string('x', 16 * 1024 + 1) }),
                    ("expanded-start", normalStart with { Input = new string('\0', 3 * 1024 * 1024) })
                })
                {
                    var rejected = await foundation.SendAsync(scope, new Contract.Target.Start(spec), operation, Contract.Kinds.Start,
                        cancellationToken: stop);
                    Check(rejected is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null, Reason: null },
                        "invalid metadata or oversized storage framing on a supported start returned a generic refusal");
                }
                var overflow = await foundation.SendAsync(scope, new Contract.Target.Ref(actor), "oversized", "notice",
                    new string('\0', 3 * 1024 * 1024), cancellationToken: stop);
                var missing = await foundation.SendAsync(new Contract.BoundScope("preview-other"), new Contract.Target.Ref(actor),
                    "missing", "notice", cancellationToken: stop);
                Check(overflow is { Outcome: Contract.AdmissionOutcome.Refused, Reason: null, Actor: null } &&
                    missing is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch, Actor: null },
                    "a valid root's physical size refusal and an unavailable tenant-bound target have distinct receipts");
            }

            Reset();
            Check(await Approve("approval-2", "Grace", frame) == Contract.AdmissionOutcome.Accepted, "second approval admitted");
            Message? queued = null;
            if (phase == "terminal")
            {
                var extra = await foundation.SendAsync(scope, new Contract.Target.Ref(actor), "already-queued", CompositeSignal.Kind,
                    Json.Write(new CompositeSignal(frame, "approve", "third")), cancellationToken: stop);
                Check(extra.Outcome == Contract.AdmissionOutcome.Accepted && extra.Actor == actor, "third input was admitted while the actor was still running");
                var diagnosticAdmission = new PostgresRootAdmission(() => new(data), TimeSpan.Zero);
                var pending = await diagnosticAdmission.ReadPendingDirectAsync(scope.Tenant, actor.Id, stop);
                queued = pending.Single(input => input.Id == "already-queued");
                var currentFence = ((Worker)owner).Fences.Single();
                var leading = pending.Single(input => input.Id == "approval-2");
                var invalid = new TurnCommit(leading.Target.Id, null, null, [], [leading.InboxKey], [], [], [], null);
                await probe.BindWorkerRootAsync(scope.Tenant, actor.Id, stop);
                Check(!(await probe.CommitAsync(new(currentFence.Partition, currentFence.Epoch, currentFence.Position,
                    TimeSpan.FromSeconds(10), false, [invalid], [], []), stop)).Landed,
                    "a rowless terminal disposition cannot consume the running root's approval");
            }
            var finished = await Drive(3);
            Check(finished is { Status: InstanceStatus.Finished, TerminalRevision: 3, TerminalAt: not null } &&
                Json.Read<NativeState>(finished.State!).Results["approve"]!.AsArray()
                    .Select(n => n!.GetValue<string>()).SequenceEqual(["Ada", "Grace"]),
                "persisted Native result is Ada, Grace exactly once");
            Report(queued is null ? "second approval including admission" : "finish with two queued inputs including admission");

            Reset();
            var (entries, _) = await ReadJournal(bookmark);
            Check(entries.Count(e => e.Revision == 3 && Decode(e) == "approved:approval-2") == 1 &&
                entries.All(e => e.Revision == 3), "the saved bookmark shows only revision 3");
            Check((await foundation.GetAsync(scope, actor, stop)) is { Header.Status: Contract.ActorStatus.Finished, Header.EndedAt: > 0 },
                "public actor read sees the terminal checkpoint");
            Report("inspect final checkpoint and journal");

            Reset();
            var other = new Contract.BoundScope("preview-other");
            Check(await foundation.GetAsync(other, actor, stop) is null &&
                (await foundation.TailAsync(other, actor, cancellationToken: stop)).Items.Count == 0,
                "public reads stay within the bound tenant");
            Report("verify read scoping");
            if (queued is not null)
            {
                var lastBookmark = (await ReadJournal()).Last!.Value;
                var home = new InstanceRef(finished.Actor.Id).Partition;
                await using var db = new KernelDb(data);
                var position = await db.Partitions.Where(p => p.Id == home).Select(p => p.Position).SingleAsync(stop);
                Reset();
                Check(await Approve("after-finish", "late", frame) == Contract.AdmissionOutcome.Refused &&
                    await Approve("approval-2", "changed", frame) == Contract.AdmissionOutcome.Duplicate,
                    "finished root refuses a new send and recognizes its earlier admission");
                Check(counter.Transactions is { Begun: 2, Ended: 2 }, "terminal refusal and duplicate both end their measured transactions");
                Check((await admission.ReadPendingDirectAsync(scope.Tenant, actor.Id, stop)).Single().InboxKey == queued.InboxKey,
                    "the new send installed no inbox occurrence and the terminal suffix is still pending");
                await owner.WokenAsync(null, stop);
                for (var pass = 0; pass < 8 && (await admission.ReadPendingDirectAsync(scope.Tenant, actor.Id, stop)).Count != 0; pass++)
                    await owner.RunAsync(stop);
                var receipt = await db.Messages.AsNoTracking().SingleAsync(m => m.Id == queued.InboxKey.Id &&
                    m.InboxScope == InboxKey.FoundationOccurrenceScope, stop);
                var after = await ReadCheckpoint();
                Check(receipt is { Done: true, Payload: null, Start: null } && after.Revision == finished.Revision &&
                    after.State == finished.State && after.TerminalAt == finished.TerminalAt && after.TerminalRevision == finished.TerminalRevision &&
                    (await foundation.TailAsync(scope, actor, lastBookmark, stop)).Items.Count == 0 &&
                    await db.Partitions.Where(p => p.Id == home).Select(p => p.Position).SingleAsync(stop) == position,
                    "fenced terminal cleanup settled the queued occurrence without changing state, mark, journal or position");
                Check(await Approve("already-queued", "replayed", frame) == Contract.AdmissionOutcome.Duplicate &&
                    (await admission.ReadPendingDirectAsync(scope.Tenant, actor.Id, stop)).Count == 0,
                    "replay of the settled third input remains a duplicate");
            }
            Console.WriteLine("completed Ada, Grace once");
        }
        return 0;

        async Task<Contract.AdmissionOutcome> Approve(string id, string name, string frame)
        {
            var receipt = await foundation.SendAsync(scope, new Contract.Target.Ref(actor), id, CompositeSignal.Kind,
                Json.Write(new CompositeSignal(frame, "approve", name)), cancellationToken: stop);
            if (receipt.Outcome is Contract.AdmissionOutcome.Accepted or Contract.AdmissionOutcome.Duplicate)
                Check(receipt.Actor == actor, $"the {id} receipt names the original workflow");
            return receipt.Outcome;
        }

        async Task<string> ReadFrame()
        {
            var (entries, _) = await ReadJournal();
            var waiting = entries.Select(Decode).Single(entry => entry.StartsWith(DirectApprovalWorkflow.WaitEntry, StringComparison.Ordinal));
            return waiting[DirectApprovalWorkflow.WaitEntry.Length..];
        }

        async Task<(IReadOnlyList<Contract.JournalEntry> Entries, Contract.PageToken? Last)> ReadJournal(Contract.PageToken? from = null)
        {
            var entries = new List<Contract.JournalEntry>();
            for (var page = 0; page < 32; page++)
            {
                var read = await foundation.TailAsync(scope, actor, from, stop);
                if (read.Items.Count == 0) return (entries, from);
                entries.AddRange(read.Items);
                from = read.Next ?? throw new InvalidOperationException("A nonempty journal page must have a bookmark.");
            }
            throw new InvalidOperationException("The preview exceeded its journal page budget.");
        }

        async Task<EfStore.RootImage> ReadCheckpoint() => await probe.ReadFirstRootAsync(scope.Tenant, actor.Id, stop)
            ?? throw new InvalidOperationException("Root checkpoint was missing.");

        async Task<EfStore.RootImage> Drive(long revision)
        {
            await owner.WokenAsync(null, stop);
            for (var pass = 0; pass < 8; pass++)
            {
                await owner.RunAsync(stop);
                if (await probe.ReadFirstRootAsync(scope.Tenant, actor.Id, stop) is { } image && image.Revision == revision) return image;
                await Task.Yield();
            }
            throw new InvalidOperationException($"The worker did not commit revision {revision}.");
        }

        void Reset()
        {
            counter.Reset();
            startConnections = wire?.Connections ?? 0;
            startTrips = wire?.Trips ?? 0;
            startGroups = wire?.ProtocolRequests ?? 0;
        }

        void Report(string operation)
        {
            if (wire is not null)
                Console.WriteLine($"wire {operation}: {wire.Trips - startTrips} TCP direction turns, {wire.ProtocolRequests - startGroups} PostgreSQL Sync/Query groups, {wire.Connections - startConnections} new TCP connections");
            Console.WriteLine($"{operation}: {counter.Commands} commands, {counter.Transactions.Ended} transaction completions, EF round-trip estimate {counter.Commands + counter.Transactions.Ended}");
        }
    }

    private static string Decode(Contract.JournalEntry entry)
    {
        using var document = JsonDocument.Parse(entry.Entry);
        return document.RootElement.ValueKind == JsonValueKind.String ? document.RootElement.GetString()! : "";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
