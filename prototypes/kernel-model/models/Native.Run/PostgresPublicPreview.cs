using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresPublicPreview
{
    internal static async Task<int> RunAsync(string phase, string? suppliedReference = null, string? suppliedBookmark = null,
        long priorEpoch = 0, int priorPid = 0, long originalUntil = 0, bool flowchart = false)
    {
        var birth = phase is "birth" or "hold-owner";
        var tenantProbe = phase == "tenant-probe";
        var formatProbe = phase == "format-probe";
        if ((phase, suppliedReference, suppliedBookmark, priorEpoch, priorPid, originalUntil) is not
            (("birth" or "hold-owner", null, null, 0, 0, 0) or
             ("tenant-probe" or "format-probe", not null, null, 0, 0, 0) or
             ("restart" or "terminal", not null, not null, 0, 0, 0) or
             ("standby", not null, not null, > 0, > 0, > 0)))
            throw new ArgumentException("Invalid PostgreSQL Core Preview phase arguments.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stop = timeout.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
            throw new InvalidOperationException("Set KERNEL_PREVIEW_POSTGRES to a dedicated preview database.");
        var counter = new Counting();
        var data = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        Wire? wire = null;
        var measuredConnection = connection;
        if (!tenantProbe && Environment.GetEnvironmentVariable("KERNEL_PREVIEW_MEASURE_WIRE") == "1")
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
        var measured = tenantProbe ? data : new DbContextOptionsBuilder<KernelDb>().UseNpgsql(measuredConnection)
            .AddInterceptors(counter, counter.Transactions).Options;
        long startTrips = 0, startGroups = 0, startConnections = 0;
        var store = new EfStore(() => new(measured), postgres: true);
        var probe = new EfStore(() => new(data), postgres: true);
        var admission = new PostgresRootAdmission(() => new(measured), TimeSpan.FromHours(1));
        var scope = FixedTenantScope.Bind(tenantProbe ? "preview-other" : "preview");

        if (birth)
        {
            await store.CreateAsync();
            await admission.InstallAsync(stop);
        }
        var ingress = await store.OpenPreviewRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1), stop);
        var reads = await store.OpenPreviewRootReadsAsync(1, stop);
        if (tenantProbe)
        {
            var otherActor = new Contract.ActorRef(suppliedReference!);
            Check(PostgresRootAdmission.IsIssuedReferenceShape(otherActor.Id), "foreign host received a valid reference shape");
            var unavailable = await ingress.SendAsync(scope, new Contract.Target.Ref(otherActor), "missing", "notice",
                cancellationToken: stop);
            Check(unavailable is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch, Actor: null },
                "the foreign host cannot send to the existing actor");
            Check(await reads.GetAsync(scope, otherActor, stop) is null &&
                (await reads.TailAsync(scope, otherActor, cancellationToken: stop)).Items.Count == 0,
                "the foreign host cannot read the existing actor or journal");
            return 0;
        }
        if (formatProbe)
        {
            await CheckIngressFormatAsync(data, ingress, scope, new(suppliedReference!), stop);
            return 0;
        }
        var workflow = new WorkflowRuntime(ingress, reads);
        var definition = flowchart ? DirectApprovalWorkflow.BuildFlowchart() : DirectApprovalWorkflow.Build();
        var registry = new WorkflowRegistry().Add(definition);
        Report("host setup");

        Contract.ActorRef actor;
        Reset();
        if (birth)
        {
            var created = await workflow.StartAsync(scope, Guid.NewGuid().ToString("N"),
                NativeExecutionModel.Start(definition.Id), cancellationToken: stop);
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
        var node = $"preview-{Environment.ProcessId}";
        List<string[]>? resumedTurns = phase == "standby" || flowchart && !birth ? [] : null;
        var prepared = await PostgresNativeRootOwner.PrepareAsync(store, node, scope.Tenant, actor.Id, registry, stop,
            resumedTurns is null ? null : (_, inputs, _) => resumedTurns.Add(inputs.Select(input => input.Id).ToArray()));
        await using var owner = phase == "standby" ?
            await PostgresPreviewFailover.TakeAfterExitAsync(prepared, store, data, node, priorEpoch, priorPid, originalUntil, stop) :
            await prepared.TryActivateAsync(stop) ?? throw new InvalidOperationException("The partition is already owned.");
        Console.WriteLine($"owner {Environment.ProcessId}");
        Report("ownership");

        if (birth)
        {
            Reset();
            await Drive(1);
            Report("start turn");

            Reset();
            var frame = await ReadFrame();
            Check((await reads.GetAsync(scope, actor, stop)) is { Header.Status: Contract.ActorStatus.Running, AsOf: > 0 },
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
            if (phase == "hold-owner")
                await PostgresPreviewFailover.HoldAsync(prepared, (Worker)owner, data, node, stop);
        }
        else
        {
            var bookmark = new Contract.PageToken(suppliedBookmark!);
            Reset();
            var before = await ReadCheckpoint();
            Check(before is { Revision: 2, Status: InstanceStatus.Running }, "cold process loaded the second wait");
            if (flowchart)
            {
                var waiting = Json.Read<NativeState>(before.State!);
                Check(waiting.Variables["joinCount"]!.GetValue<int>() == 0 && !waiting.Variables["automaticPath"]!.GetValue<bool>(),
                    "the approval path still waits and the other path has not run");
            }
            Check((await reads.TailAsync(scope, actor, bookmark, stop)) is { Items.Count: 0, AsOf: > 0 },
                "the previous process's bookmark resumes at the end of revision 2");
            var frame = await ReadFrame();
            Report("inspect after restart");

            Reset();
            Check(await Approve("approval-1", "alternative", frame) == Contract.AdmissionOutcome.Duplicate,
                "public ingress recognized the replay with changed payload");
            await owner.WokenAsync(null, stop);
            await owner.RunAsync(stop);
            if (resumedTurns is not null)
                Check(resumedTurns.Count == 0, "the duplicate started zero public actor turns on the resumed owner");
            Report("replay including admission");
            var same = await ReadCheckpoint();
            Check(same.Revision == before.Revision && same.State == before.State, "replay preserved the serialized workflow state and checkpoint revision");
            Check((await reads.TailAsync(scope, actor, bookmark, stop)).Items.Count == 0, "replay added no journal entries");
            if (phase == "terminal")
            {
                foreach (var command in new[] { Contract.Kinds.Suspend, Contract.Kinds.Terminate, Contract.Kinds.Delete })
                {
                    var unauthorized = await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "restricted", command,
                        cancellationToken: stop);
                    Check(unauthorized is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null, Reason: null },
                        $"ordinary provider ingress refuses {command} on a live root");
                }
                foreach (var invalidKind in new[] { new string('x', 257), "input\ud800" })
                {
                    var rejected = await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "invalid-kind", invalidKind,
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
                    var rejected = await ingress.SendAsync(scope, new Contract.Target.Start(spec), operation, Contract.Kinds.Start,
                        cancellationToken: stop);
                    Check(rejected is { Outcome: Contract.AdmissionOutcome.Refused, Actor: null, Reason: null },
                        "invalid metadata or oversized storage framing on a supported start returned a generic refusal");
                }
                var overflow = await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "oversized", "notice",
                    new string('\0', 3 * 1024 * 1024), cancellationToken: stop);
                Check(overflow is { Outcome: Contract.AdmissionOutcome.Refused, Reason: null, Actor: null },
                    "a valid root's physical size refusal has no target-mismatch reason");
            }

            Reset();
            Check(await Approve("approval-2", "Grace", frame) == Contract.AdmissionOutcome.Accepted, "second approval admitted");
            Message? queued = null;
            if (phase == "terminal")
            {
                var extra = await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "already-queued", CompositeSignal.Kind,
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
            if (resumedTurns is not null)
                Check(resumedTurns is [["approval-2"]], "the resumed owner ran one public actor turn for approval-2");
            Check(finished is { Status: InstanceStatus.Finished, TerminalRevision: 3, TerminalAt: not null } &&
                Json.Read<NativeState>(finished.State!).Results["approve"]!.AsArray()
                    .Select(n => n!.GetValue<string>()).SequenceEqual(["Ada", "Grace"]),
                "persisted Native result is Ada, Grace exactly once");
            if (flowchart)
            {
                var state = Json.Read<NativeState>(finished.State!);
                Check(state.Variables["joinCount"]!.GetValue<int>() == 1 && !state.Variables["automaticPath"]!.GetValue<bool>(),
                    "the join fired once after the selected path finished");
            }
            Report(queued is null ? "second approval including admission" : "finish with two queued inputs including admission");

            Reset();
            var (entries, _) = await ReadJournal(bookmark);
            Check(entries.Count(e => e.Revision == 3 && Decode(e) == "approved:approval-2") == 1 &&
                entries.All(e => e.Revision == 3), "the saved bookmark shows only revision 3");
            if (flowchart)
                Check(entries.Count(e => Decode(e) == "end") == 1 && Decode(entries[^1]) == "end",
                    "the public Flowchart tail ends with one lifecycle entry");
            Check((await reads.GetAsync(scope, actor, stop)) is { Header.Status: Contract.ActorStatus.Finished, Header.EndedAt: > 0 },
                "public actor read sees the terminal checkpoint");
            Report("inspect final checkpoint and journal");

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
                    (await reads.TailAsync(scope, actor, lastBookmark, stop)).Items.Count == 0 &&
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
            var receipt = await ingress.SendAsync(scope, new Contract.Target.Ref(actor), id, CompositeSignal.Kind,
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
                var read = await reads.TailAsync(scope, actor, from, stop);
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

    private static async Task CheckIngressFormatAsync(DbContextOptions<KernelDb> options, Contract.IIngress ingress,
        Contract.BoundScope scope, Contract.ActorRef actor, CancellationToken token)
    {
        // Run against a disposable database immediately after birth has exited.
        var start = new Contract.Target.Start(new(PublicActor.ActorType, "definition"));
        var direct = new Contract.Target.Ref(actor);
        await using var db = new KernelDb(options);
        var original = db.RequiredFormat.StoreSchema;
        var changed = original + 1;
        try
        {
            Check(await SetSchemaAsync(db, original, changed, token) == 1, "the fixture advances only its expected marker");
            await RefusesFormat(start, "format-new-start", Contract.Kinds.Start);
            await RefusesFormat(direct, "format-direct", "notice");
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var restoration = new KernelDb(options);
            var restored = await SetSchemaAsync(restoration, changed, original, cleanup.Token);
            Check(restored == 1 || await restoration.StoreVersions.AnyAsync(
                row => row.Id == StoreVersionEntity.MarkerId && row.StoreSchema == original, cleanup.Token),
                "cleanup restores or observes the expected marker");
        }
        Check((await ingress.SendAsync(scope, start, "format-new-start", Contract.Kinds.Start, cancellationToken: token)).Outcome ==
            Contract.AdmissionOutcome.Accepted, "the refused start did not claim its receipt");
        Check((await ingress.SendAsync(scope, direct, "format-direct", "notice", cancellationToken: token)).Outcome ==
            Contract.AdmissionOutcome.Accepted, "the refused direct input did not claim its receipt");

        static Task<int> SetSchemaAsync(KernelDb database, int from, int to, CancellationToken cancellationToken) =>
            database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "KernelStoreVersions" SET "StoreSchema" = {to}
                WHERE "Id" = {StoreVersionEntity.MarkerId} AND "StoreSchema" = {from}
                """, cancellationToken);

        async Task RefusesFormat(Contract.Target target, string id, string kind)
        {
            try
            {
                await ingress.SendAsync(scope, target, id, kind, cancellationToken: token);
                throw new InvalidOperationException($"Accepted {kind} after the store format changed.");
            }
            catch (StoreFormatException error) when (error.Message.Contains($"Stored schema={changed},", StringComparison.Ordinal))
            {
                Console.WriteLine($"ok: open ingress refuses {kind} after the store format changes");
            }
        }
    }

    internal static string Decode(Contract.JournalEntry entry)
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
