using System.Security.Cryptography;
using System.Text;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// S3 (#4229): one root state family, chosen by capabilities. An unshipped set carries timed jobs, termination with a stable
// body, direct held replies and held operator replies on top of the S2 composition. Rows are seeded through the guarded
// checkpoint update; admissions, point reads and the terminal lock are the store's own paths.
internal static class PostgresRootStateFamilyChecks
{
    private static readonly StoreFormat Composed = StoreFormat.Current with { MessageProtocol = 98 };
    private const string Tenant = "state-family";
    private const string Model = "composed";
    private const string Definition = "definition-a";
    private const string Node = "family-edge";

    private sealed record Header(string Model, string Definition, string TypeData, KeyValuePair<string, string>[] Labels, bool EscapedState);

    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var capabilities = RootProfiles.Formats[StoreFormat.RootTerminalSelection] | RootProfiles.Formats[StoreFormat.RootParkInspection] |
            RootProfiles.Formats[StoreFormat.RootTimedJobPreview] | RootCapabilities.ReplyProtocol | RootCapabilities.OperatorReplies;
        KernelDb Open() => KernelDb.ForUnshippedComposition(options, Composed, capabilities);
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, ct), "the state-family fixture uses an empty PostgreSQL database");

        // One prefix names every family the set carries and ends both Cancelled and Faulted roots.
        var family = db.RootStateFamily;
        Check(family == new RootStateFamily("!kernel-state/13:public-root-composed:timed-jobs+stable-body+replies:", true, true),
            $"the composed set writes one family that names timed jobs, stable body and replies: {family.Prefix}");
        var shipped = RootProfiles.Formats.Values.Select(set => RootStateFamily.Of(set).Prefix).Distinct().ToArray();
        Check(shipped.Length == 6 && shipped.All(prefix => !family.Prefix.StartsWith(prefix, StringComparison.Ordinal) &&
                !prefix.StartsWith(family.Prefix, StringComparison.Ordinal)),
            "no shipped family prefix matches the composed prefix in either direction");

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
                catch (StoreFormatException) { Check(true, $"shipped protocol {other.RequiredFormat.MessageProtocol} refuses the composed marker"); }
            }

        // A running timed-job root reads back through the composed prefix with its jobs.
        var jobs = JobRegistry.Write([new("timer/approval", 0)]);
        var timed = await Seed(db, "timed", family.Prefix, """{"step":"timed"}""", jobs, 1, ct);
        Check(await store.ReadFirstRootAsync(Tenant, timed.Reference, ct) is { Status: InstanceStatus.Running, State: """{"step":"timed"}""",
                Revision: 1, NextJobGeneration: 1, TerminalRevision: null } read && read.Jobs == jobs &&
            JobRegistry.Read(read.Jobs).SequenceEqual([new("timer/approval", 0)]) && read.Model == Model && read.Definition == Definition,
            "a timed-job root reads back its state, jobs and next generation under the composed family");
        Check(await store.ReadRootActorAsync(Tenant, timed.Reference, ct) is ({ Status: InstanceStatus.Running, SupportsSelection: true }, > 0),
            "the timed-job root lists as running and selectable");

        // Cancelled and Faulted roots of the composed family are terminal for the worker lock and for admission.
        foreach (var status in new[] { InstanceStatus.Cancelled, InstanceStatus.Faulted })
        {
            var name = status.ToString().ToLowerInvariant();
            var root = await Seed(db, name, family.Prefix, """{"step":"ending"}""", null, null, ct);
            Check(await admission.AdmitDirectResultAsync(Tenant, root.Reference, "probe-" + name, "signal") == PostgresRootAdmission.DirectResult.Accepted,
                $"the running {name} root admits a direct input and keeps its receipt");
            await End(db, root.Actor, status, ct);
            Check(await EfStore.LockQualifiedTerminalAsync(db, root, ct) is [2], $"the worker's terminal lock finds the {name} root at revision 2");
            Check(await admission.AdmitDirectResultAsync(Tenant, root.Reference, "probe-" + name, "signal") == PostgresRootAdmission.DirectResult.Duplicate,
                $"admission treats the {name} root as terminal and answers its receipt as a duplicate");
            Check(await admission.AdmitDirectResultAsync(Tenant, root.Reference, "late-" + name, "signal") == PostgresRootAdmission.DirectResult.Refused,
                $"a new input to the {name} root is refused");
            Check(await store.ReadFirstRootAsync(Tenant, root.Reference, ct) is { TerminalRevision: 2, TerminalAt: > 0 } ended && ended.Status == status,
                $"the {name} root reads back with its terminal mark");
        }

        // Rows under a component's own shipped prefix are not of this family: they are neither terminal nor readable.
        // The timed prefix is what the exclusive selection wrote for this set; the stable-body prefix is what its Cancelled check sought.
        foreach (var (status, prefix) in new[] { (InstanceStatus.Cancelled, RootStateFamily.TimedJobs), (InstanceStatus.Faulted, RootStateFamily.StableBody) })
        {
            var name = "foreign-" + status.ToString().ToLowerInvariant();
            var root = await Seed(db, name, prefix, """{"step":"foreign"}""", null, null, ct);
            await End(db, root.Actor, status, ct);
            Check(await EfStore.LockQualifiedTerminalAsync(db, root, ct) is [], $"the worker's terminal lock does not end a {status} root under {prefix}");
            try
            {
                await store.ReadFirstRootAsync(Tenant, root.Reference, ct);
                throw new InvalidOperationException("A foreign state family was read.");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite) { Check(true, $"the point read refuses a {status} root under {prefix}"); }
        }

        // A direct held reply and a held operator reply wait on the same running root.
        var held = await Seed(db, "held", family.Prefix, """{"step":"held"}""", null, null, ct);
        var now = await admission.ReadReplyTimeAsync(ct);
        var requests = new Requests();
        var request = requests.OpenBound("approve", now + 60_000, Tenant, null);
        Check(await admission.AdmitDirectRequestAsync(Tenant, held.Reference, "signal", "\"Ada\"", Node, request, false, ct)
            == PostgresRootAdmission.DirectResult.Accepted, "the composed set admits a direct held request");
        var reads = new PostgresRootReads(store, await admission.OpenJournalCursorAsync(ct), 8);
        var selection = (await reads.GetAsync(EmbeddedTenant.Bind(Tenant), new Contract.ActorRef(held.Reference), ct))?.Selection;
        var deadline = now + 60_000;
        var route = ReplyTo.Of(Node, "stop", deadline);
        Check(await admission.AdmitRootOperatorAsync(Tenant, held.Reference, "stop", Kinds.Terminate, null, null, ct, selection,
            reply: new(route, deadline)) is { Duplicate: false }, "the same set admits a held Terminate on the same root");
        Check(await admission.AdmitDirectRequestAsync(Tenant, held.Reference, "signal", "\"Ada\"", Node, request, false, ct)
            == PostgresRootAdmission.DirectResult.Duplicate, "the held request stays deduplicated beside the held operator");
        var rows = await db.Messages.AsNoTracking().Where(m => m.Target == held.Actor && m.InboxScope == InboxKey.FoundationOccurrenceScope)
            .OrderBy(m => m.Seq).ToListAsync(ct);
        Check(rows is [{ ReplyTo: not null }, { ReplyTo: not null }], "both occurrences carry their reply routes");
        var direct = (await admission.ReadPendingDirectAsync(Tenant, held.Reference, ct)).Single();
        var command = PostgresRootAdmission.DecodeOperator(db, rows[1], held.Reference);
        Check(direct is { Id: "approve", Payload: "\"Ada\"" } && direct.ReplyTo == request.ReplyAddress(Node) &&
            command is { Kind: Kinds.Terminate, Operator.RootHeldReply: true } && command.ReplyTo == route,
            "the direct request decodes with its route and the operator decodes as a held Terminate");
        Check(PostgresRootAdmission.InspectDirectHeader(db, rows[0].Start, held.Reference, rows[0].Id, rows[0].Kind, rows[0].Due, true).Id == "approve" &&
            PostgresRootAdmission.InspectOperatorHeader(db, rows[1].Start, held.Reference, rows[1].Id, rows[1].Kind, rows[1].Due, true).Id == "stop",
            "inspection reads both held headers under one capability set");
        // Each shipped held-reply profile still refuses the other kind's envelope.
        var unreachable = new DbContextOptionsBuilder<KernelDb>().UseNpgsql("Host=state-family.invalid").Options;
        await using (var replies = KernelDb.ForPostgresRootReplies(unreachable))
            Refused(() => PostgresRootAdmission.DecodeOperator(replies, rows[1], held.Reference), "the shipped direct-reply profile refuses the held operator");
        Refused(() => PostgresRootAdmission.DecodeDirect(rows[0], held.Reference, replies: false), "a profile without direct replies refuses the held request");
        return 0;
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
