using System.Security.Cryptography;
using System.Text;
using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// S3 (#4229): one root state family, chosen by capabilities. Two unshipped sets carry timed jobs, termination with a stable
// body, direct held replies and held operator replies: one on top of the S2 Park composition, one without Park in its own
// schema, so that admission decides Faulted roots by the family prefix. Rows are seeded through the guarded checkpoint
// update; admissions, point reads and the terminal lock are the store's own paths. On the Park-less set a public birth and one
// direct input also drive the store's own first-root bind and root turn.
internal static class PostgresRootStateFamilyChecks
{
    private static readonly StoreFormat ParkComposed = StoreFormat.Current with { MessageProtocol = 98 };
    private static readonly StoreFormat ParklessComposed = StoreFormat.Current with { MessageProtocol = 97 };
    private const string FamilyPrefix = "!kernel-state/13:public-root-composed:timed-jobs+stable-body+replies:";
    private const string ParklessSchema = "state_family_parkless";
    private const string WriterSchema = "state_family_writer";
    private const string Tenant = "state-family";
    private const string Model = "composed";
    private const string Definition = "definition-a";
    private const string Node = "family-edge";
    private const string WriterDefinition = "family-approval";

    private sealed record PhysicalRow(long Revision, int Status, string State, bool Wire, bool NoJobs);
    private sealed record Header(string Model, string Definition, string TypeData, KeyValuePair<string, string>[] Labels, bool EscapedState);

    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = timeout.Token;
        var withoutPark = RootProfiles.Formats[StoreFormat.RootTerminalSelection] | RootProfiles.Formats[StoreFormat.RootTimedJobPreview] |
            RootCapabilities.ReplyProtocol | RootCapabilities.OperatorReplies;
        var withPark = withoutPark | RootProfiles.Formats[StoreFormat.RootParkInspection];
        await using (var db = KernelDb.ForUnshippedComposition(options, ParkComposed, withPark))
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, ct), "the state-family fixture uses an empty PostgreSQL database");
        await RunSetAsync(options, ParkComposed, withPark, park: true, owned, inBatch, ct);

        // The Park-less set in its own schema of the same database.
        await using (var db = KernelDb.ForUnshippedComposition(options, ParkComposed, withPark))
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + ParklessSchema, ct);
        var parkless = new DbContextOptionsBuilder<KernelDb>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = ParklessSchema }.ConnectionString).Options;
        await using (var db = KernelDb.ForUnshippedComposition(parkless, ParklessComposed, withoutPark))
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(ct);
        await RunSetAsync(parkless, ParklessComposed, withoutPark, park: false, owned, inBatch, ct);

        // The store's own writers drive the Park-less set in a third schema, apart from the seeded rows.
        await using (var db = KernelDb.ForUnshippedComposition(options, ParkComposed, withPark))
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + WriterSchema, ct);
        var written = new DbContextOptionsBuilder<KernelDb>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = WriterSchema }.ConnectionString).Options;
        KernelDb OpenWritten() => KernelDb.ForUnshippedComposition(written, ParklessComposed, withoutPark);
        await using (var db = OpenWritten())
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(ct);
        await DriveWriterAsync(OpenWritten, owned, inBatch, ct);
        await using (var db = KernelDb.ForUnshippedComposition(parkless, ParklessComposed, withoutPark))
            await BudgetAsync(db, ct);
        return 0;
    }

    private static async Task RunSetAsync(DbContextOptions<KernelDb> options, StoreFormat format, RootCapabilities capabilities, bool park,
        bool owned, bool inBatch, CancellationToken ct)
    {
        KernelDb Open() => KernelDb.ForUnshippedComposition(options, format, capabilities);
        var set = park ? "Park set" : "Park-less set";
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        await using var db = Open();

        // One prefix names every family the set carries and ends both Cancelled and Faulted roots.
        var family = db.RootStateFamily;
        Check(family == new RootStateFamily(FamilyPrefix, true, true),
            $"the {set} writes one family that names timed jobs, stable body and replies: {family.Prefix}");
        var shipped = RootProfiles.Formats.Values.Select(shippedSet => RootStateFamily.Of(shippedSet).Prefix).Distinct().ToArray();
        Check(shipped.Length == 6 && shipped.All(prefix => !family.Prefix.StartsWith(prefix, StringComparison.Ordinal) &&
                !prefix.StartsWith(family.Prefix, StringComparison.Ordinal)),
            $"no shipped family prefix matches the {set} prefix in either direction");

        await store.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admission.InstallAsync(ct);
        await admission.InstallAsync(ct);
        foreach (var other in new[] { KernelDb.ForPostgresRootTimedJobPreview(options), KernelDb.ForPostgresRootOperatorReplies(options),
                     KernelDb.ForPostgresRootReplies(options) })
            await using (other)
            {
                try
                {
                    await EfStore.RequireStoreVersionsAsync(other, ct);
                    throw new InvalidOperationException("A shipped profile attached to the unshipped state family.");
                }
                catch (StoreFormatException) { Check(true, $"shipped protocol {other.RequiredFormat.MessageProtocol} refuses the {set} marker"); }
            }

        // A running timed-job root reads back through the composed prefix with its jobs.
        var jobs = JobRegistry.Write([new("timer/approval", 0)]);
        var timed = await Seed(db, "timed", family.Prefix, """{"step":"timed"}""", jobs, 1, ct);
        Check(await store.ReadFirstRootAsync(Tenant, timed.Reference, ct) is { Status: InstanceStatus.Running, State: """{"step":"timed"}""",
                Revision: 1, NextJobGeneration: 1, TerminalRevision: null } read && read.Jobs == jobs &&
            JobRegistry.Read(read.Jobs).SequenceEqual([new("timer/approval", 0)]) && read.Model == Model && read.Definition == Definition,
            $"a timed-job root of the {set} reads back its state, jobs and next generation under the composed family");
        Check(await store.ReadRootActorAsync(Tenant, timed.Reference, ct) is ({ Status: InstanceStatus.Running, SupportsSelection: true }, > 0),
            $"the timed-job root of the {set} lists as running and selectable");

        // Cancelled and Faulted roots of the composed family are terminal for the worker lock and for admission. Park opening
        // ends every Faulted root at admission, so on the Park set only Cancelled reaches the family prefix there.
        foreach (var status in new[] { InstanceStatus.Cancelled, InstanceStatus.Faulted })
        {
            var name = status.ToString().ToLowerInvariant();
            var root = await Seed(db, name, family.Prefix, """{"step":"ending"}""", null, null, ct);
            Check(await admission.AdmitDirectResultAsync(Tenant, root.Reference, "probe-" + name, "signal") == PostgresRootAdmission.DirectResult.Accepted,
                $"the running {name} root of the {set} admits a direct input and keeps its receipt");
            await End(db, root.Actor, status, ct);
            Check(await EfStore.LockQualifiedTerminalAsync(db, root, ct) is [2], $"the worker's terminal lock finds the {name} root of the {set} at revision 2");
            var path = status == InstanceStatus.Faulted && park ? "through Park opening" : "by the family prefix";
            Check(await admission.AdmitDirectResultAsync(Tenant, root.Reference, "probe-" + name, "signal") == PostgresRootAdmission.DirectResult.Duplicate,
                $"admission treats the {name} root of the {set} as terminal {path} and answers its receipt as a duplicate");
            Check(await admission.AdmitDirectResultAsync(Tenant, root.Reference, "late-" + name, "signal") == PostgresRootAdmission.DirectResult.Refused,
                $"a new input to the {name} root of the {set} is refused");
            Check(await store.ReadFirstRootAsync(Tenant, root.Reference, ct) is { TerminalRevision: 2, TerminalAt: > 0 } ended && ended.Status == status,
                $"the {name} root of the {set} reads back with its terminal mark");
        }

        // Rows under a component's own shipped prefix are not of this family: they are neither terminal nor readable, and
        // admission does not answer their receipts as duplicates. The crossed pairs are what the exclusive selection wrote
        // against what the other status sought; the legacy pairs are the component prefixes the old checks accepted.
        foreach (var (status, prefix, kind) in new[]
                 {
                     (InstanceStatus.Cancelled, RootStateFamily.TimedJobs, "crossed"), (InstanceStatus.Faulted, RootStateFamily.StableBody, "crossed"),
                     (InstanceStatus.Faulted, RootStateFamily.TimedJobs, "legacy"), (InstanceStatus.Cancelled, RootStateFamily.StableBody, "legacy")
                 })
        {
            var name = kind + "-" + status.ToString().ToLowerInvariant();
            var root = await Seed(db, name, prefix, """{"step":"foreign"}""", null, null, ct);
            Check(await admission.AdmitDirectResultAsync(Tenant, root.Reference, "probe-" + name, "signal") == PostgresRootAdmission.DirectResult.Accepted,
                $"the running {kind} {status} root of the {set} admits a direct input");
            await End(db, root.Actor, status, ct);
            Check(await EfStore.LockQualifiedTerminalAsync(db, root, ct) is [],
                $"the worker's terminal lock does not end a {kind} {status} root of the {set} under {prefix}");
            try
            {
                await store.ReadFirstRootAsync(Tenant, root.Reference, ct);
                throw new InvalidOperationException("A foreign state family was read.");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite) { Check(true, $"the point read of the {set} refuses a {kind} {status} root under {prefix}"); }
            if (status == InstanceStatus.Faulted && park) continue;
            Check(await admission.AdmitDirectResultAsync(Tenant, root.Reference, "probe-" + name, "signal") == PostgresRootAdmission.DirectResult.Refused,
                $"admission of the {set} refuses the receipt of a {kind} {status} root under {prefix} and does not answer it as a duplicate");
        }

        // A direct held reply and a held operator reply wait on the same running root.
        var held = await Seed(db, "held", family.Prefix, """{"step":"held"}""", null, null, ct);
        var now = await admission.ReadReplyTimeAsync(ct);
        var requests = new Requests();
        var request = requests.OpenBound("approve", now + 60_000, Tenant, null);
        Check(await admission.AdmitDirectRequestAsync(Tenant, held.Reference, "signal", "\"Ada\"", Node, request, false, ct)
            == PostgresRootAdmission.DirectResult.Accepted, $"the {set} admits a direct held request");
        var reads = new PostgresRootReads(store, await admission.OpenJournalCursorAsync(ct), 8);
        var selection = (await reads.GetAsync(EmbeddedTenant.Bind(Tenant), new Contract.ActorRef(held.Reference), ct))?.Selection;
        var deadline = now + 60_000;
        var route = ReplyTo.Of(Node, "stop", deadline);
        Check(await admission.AdmitRootOperatorAsync(Tenant, held.Reference, "stop", Kinds.Terminate, null, null, ct, selection,
            reply: new(route, deadline)) is { Duplicate: false }, $"the {set} admits a held Terminate on the same root");
        Check(await admission.AdmitDirectRequestAsync(Tenant, held.Reference, "signal", "\"Ada\"", Node, request, false, ct)
            == PostgresRootAdmission.DirectResult.Duplicate, $"the held request of the {set} stays deduplicated beside the held operator");
        var rows = await db.Messages.AsNoTracking().Where(m => m.Target == held.Actor && m.InboxScope == InboxKey.FoundationOccurrenceScope)
            .OrderBy(m => m.Seq).ToListAsync(ct);
        Check(rows is [{ ReplyTo: not null }, { ReplyTo: not null }], $"both occurrences of the {set} carry their reply routes");
        var direct = (await admission.ReadPendingDirectAsync(Tenant, held.Reference, ct)).Single();
        var command = PostgresRootAdmission.DecodeOperator(db, rows[1], held.Reference);
        Check(direct is { Id: "approve", Payload: "\"Ada\"" } && direct.ReplyTo == request.ReplyAddress(Node) &&
            command is { Kind: Kinds.Terminate, Operator.RootHeldReply: true } && command.ReplyTo == route,
            $"the direct request of the {set} decodes with its route and the operator decodes as a held Terminate");
        Check(PostgresRootAdmission.InspectDirectHeader(db, rows[0].Start, held.Reference, rows[0].Id, rows[0].Kind, rows[0].Due, true).Id == "approve" &&
            PostgresRootAdmission.InspectOperatorHeader(db, rows[1].Start, held.Reference, rows[1].Id, rows[1].Kind, rows[1].Due, true).Id == "stop",
            $"inspection reads both held headers under the {set}");
        // Each shipped held-reply profile still refuses the other kind's envelope.
        var unreachable = new DbContextOptionsBuilder<KernelDb>().UseNpgsql("Host=state-family.invalid").Options;
        await using (var replies = KernelDb.ForPostgresRootReplies(unreachable))
            Refused(() => PostgresRootAdmission.DecodeOperator(replies, rows[1], held.Reference), $"the shipped direct-reply profile refuses the held operator of the {set}");
        Refused(() => PostgresRootAdmission.DecodeDirect(rows[0], held.Reference, replies: false), $"a profile without direct replies refuses the held request of the {set}");
    }

    // A public birth and one direct input, written by the store's own first-root bind and root turn under the Park-less set.
    // The set carries the job protocol, so the owner enables jobs before it binds. The workflow issues no job.
    private static async Task DriveWriterAsync(Func<KernelDb> open, bool owned, bool inBatch, CancellationToken ct)
    {
        var store = new EfStore(open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        await store.CreateAsync(1);
        var admission = new PostgresRootAdmission(open, TimeSpan.FromHours(1));
        await admission.InstallAsync(ct);
        var scope = EmbeddedTenant.Bind(Tenant);
        var api = new PostgresPreviewFoundation(admission, new(store, await admission.OpenJournalCursorAsync(ct), 16),
            WorkflowActor.WorkflowActor.ActorType);
        var registry = new WorkflowRegistry().Add(Wf.Workflow(WriterDefinition, Wf.Sequence("flow",
            Wf.Node<PostgresHeldDirectChecks.ApprovalSignal>("approve"))));
        var started = await new WorkflowRuntime(api, api).StartAsync(scope, "writer", NativeExecutionModel.Start(WriterDefinition), cancellationToken: ct);
        Check(started.Outcome == Contract.AdmissionOutcome.Accepted, "the Park-less set admits a public workflow start");
        var reference = started.Actor!.Value.Id;
        await store.EnableRootJobsAsync(ct);
        var (roots, count) = await store.BindWorkerPartitionAsync(0, ct);
        var worker = new Worker("family-writer", 0, store, PostgresNativeRootOwner.Adapter(store, roots, registry), new WorkerOptions { Partitions = count });
        var host = new DurableHost([worker]);
        await host.HeartbeatAsync(worker.Lease, ct);
        Check(await host.ActivateAsync(0, ct) == worker, "the writer owns the Park-less partition");
        await using var db = open();

        var first = await Drive(1);
        Check(first is { Revision: 1, Status: InstanceStatus.Running, TerminalRevision: null, Jobs: null, Model: "workflow" } &&
            first.Labels["definition"] == WriterDefinition,
            "the store's first-root bind writes revision 1 of the public birth under the Park-less set");
        await Physical(1, InstanceStatus.Running, "the first-root bind");
        var frame = Json.Read<NativeState>(first.State!).Frames.Values.Single(f => f.Node == "approve").Id;
        var signal = Json.Write(new CompositeSignal(frame, "approve", "Ada"));
        Check(await admission.AdmitDirectResultAsync(Tenant, reference, "approve", CompositeSignal.Kind, signal) == PostgresRootAdmission.DirectResult.Accepted,
            "the Park-less set admits one direct input to the written root");
        var final = await Drive(2);
        Check(final is { Revision: 2, Status: InstanceStatus.Finished, TerminalRevision: 2, TerminalAt: > 0 },
            "the store's root turn writes the finished revision 2 under the Park-less set");
        await Physical(2, InstanceStatus.Finished, "the root turn");
        Check(await admission.AdmitDirectResultAsync(Tenant, reference, "approve", CompositeSignal.Kind, signal) == PostgresRootAdmission.DirectResult.Duplicate,
            "admission answers the settled input of the finished written root as a duplicate");
        await worker.DisposeAsync();
        return;

        async Task<EfStore.RootImage> Drive(long revision)
        {
            for (var pass = 0; pass < 40; pass++)
            {
                await worker.WokenAsync(null, ct);
                await worker.RunAsync(ct);
                if (!worker.Owns) throw new InvalidOperationException("The writer lost its fence.");
                if (await store.ReadFirstRootAsync(Tenant, reference, ct) is { } image && image.Revision >= revision) return image;
                await Task.Delay(10, ct);
            }
            throw new InvalidOperationException($"The writer did not commit revision {revision}.");
        }

        // The physical row carries the family prefix before the stable-body marker, the body in the wire column and no jobs.
        async Task Physical(long revision, InstanceStatus status, string writer)
        {
            var row = await db.Database.SqlQuery<PhysicalRow>($"""
                SELECT "Revision", "Status", "State", "FoundationStateWire" IS NOT NULL AS "Wire", "Jobs" IS NULL AS "NoJobs"
                FROM "Instances" WHERE "FoundationReference" = {reference}
                """).SingleAsync(ct);
            Check(row == new PhysicalRow(revision, (int)status, FamilyPrefix + "wire", true, true),
                $"{writer} stores {FamilyPrefix}wire with the body in the wire column");
        }
    }

    // The stable-body budget charges jobs as the general measure does: a composed row that its jobs push over the content
    // budget is refused by both measures, and the same row without jobs is accepted by both.
    private static async Task BudgetAsync(KernelDb db, CancellationToken ct)
    {
        var prefix = db.RootStateFamily.Prefix;
        var header = DurableKeyJson.Write(new Header(Model, Definition, "{}", [new("definition", Definition)], false));
        var jobs = JobRegistry.Write([new("timer/approval", 0), new("timer/reminder", 1)])!;
        var jobBytes = Encoding.UTF8.GetByteCount(jobs);
        var body = new string('a', EfStore.RootContentBytes - prefix.Length - "wire".Length - Encoding.UTF8.GetByteCount(header) - jobBytes / 2);
        var wire = RootSelfContent.Encode(body);
        var stableWith = EfStore.StableContentBytes(prefix, "wire", header, wire, jobs, 0, 0);
        var stableWithout = EfStore.StableContentBytes(prefix, "wire", header, wire, null, 0, 0);
        var measureWith = await EfStore.MeasureRootContentAsync(db, prefix.Length, body, header, jobs, ct);
        var measureWithout = await EfStore.MeasureRootContentAsync(db, prefix.Length, body, header, null, ct);
        Check(stableWith > EfStore.RootContentBytes && measureWith > EfStore.RootContentBytes &&
            stableWithout <= EfStore.RootContentBytes && measureWithout <= EfStore.RootContentBytes &&
            stableWith - stableWithout == jobBytes && measureWith - measureWithout == jobBytes,
            $"a composed stable-body row near the budget is refused by both measures with its {jobBytes} job bytes and accepted by both without them");
    }

    // A first checkpoint written as the qualified first-root bind writes it: the family prefix before the stable-body marker,
    // the state in the wire column and the model in the header.
    private static async Task<EfStore.WorkerRoot> Seed(KernelDb db, string name, string prefix, string state, string? jobs, long? nextJobGeneration,
        CancellationToken ct)
    {
        var actor = "0/" + name;
        var reference = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("s3/" + name)));
        var header = DurableKeyJson.Write(new Header(Model, Definition, "{}", [new("definition", Definition)], false));
        db.Instances.Add(new InstanceEntity { Id = actor, Partition = 0, Model = "", Definition = "", Status = InstanceStatus.Running, State = "wire" });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FoundationRootBirths" ("Reference", "Tenant", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
            VALUES ({reference}, {Tenant}, {actor}, 0, 1, {"creator-" + name}, 'seed');
            UPDATE "Instances" SET "FoundationReference" = {reference}, "FoundationTenant" = {Tenant}, "FoundationHeader" = {header},
                "State" = {prefix} || "State", "FoundationStateWire" = {RootSelfContent.Encode(state)}, "Jobs" = {jobs},
                "NextJobGeneration" = {nextJobGeneration}, "Revision" = 1, "FoundationWriteRevision" = 1
            WHERE "Id" = {actor};
            """, ct);
        return new(Tenant, reference, actor, 0, "seed");
    }

    private static Task End(KernelDb db, string actor, InstanceStatus status, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Status" = {(int)status}, "Revision" = 2, "FoundationWriteRevision" = 2,
                "FoundationTerminalRevision" = 2, "FoundationTerminalAt" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            WHERE "Id" = {actor}
            """, ct);

    private static void Refused(Action decode, string label)
    {
        try { decode(); }
        catch (StoreException error) when (error.Failure == Failure.Definite) { Check(true, label); return; }
        throw new InvalidOperationException("Expected a refusal: " + label);
    }

    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        Console.WriteLine("ok " + label);
    }
}
