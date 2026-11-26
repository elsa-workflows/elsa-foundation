using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

// S2 (#4229): the one actor-list projector follows the root capabilities. An unshipped profile carries terminal selection
// and Park inspection together, installs workflow history beside them, and projects both marks and history readiness.
internal static class PostgresRootCompositionChecks
{
    private static readonly StoreFormat Composed = StoreFormat.Current with { MessageProtocol = 99 };
    private const string Tenant = "compose";
    private const string Model = "composed";
    private const string Definition = "definition-a";
    private const string Ended = "ref-ended";
    private const string Parked = "ref-parked";

    // The shape of the store's root header, so that the store decodes the seeded rows as its own.
    private sealed record Header(string Model, string Definition, string TypeData, KeyValuePair<string, string>[] Labels, bool EscapedState);

    internal static async Task<int> RunAsync(bool owned, bool inBatch, bool shippedSet = false)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var capabilities = shippedSet ? RootProfiles.Shipped
            : RootProfiles.Formats[StoreFormat.RootTerminalSelection] | RootProfiles.Formats[StoreFormat.RootParkInspection];
        KernelDb Open() => KernelDb.ForUnshippedComposition(options, Composed, capabilities);
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, ct), "the composition fixture uses an empty PostgreSQL database");
        try
        {
            KernelDb.ForUnshippedComposition(options, StoreFormat.RootTerminalSelection, capabilities).Dispose();
            throw new InvalidOperationException("A shipped format accepted another capability set.");
        }
        catch (ArgumentOutOfRangeException) { Check(true, "a shipped format cannot carry an unshipped capability set"); }

        await store.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admission.InstallAsync(ct);
        await admission.InstallAsync(ct);
        await store.InstallRootWorkflowHistoryAsync(ct);
        await store.InstallRootWorkflowHistoryAsync(ct);
        await using (var shipped = KernelDb.ForPostgresRootTerminalSelection(options))
        {
            try
            {
                await EfStore.RequireStoreVersionsAsync(shipped, ct);
                throw new InvalidOperationException("A shipped profile attached to the unshipped composition.");
            }
            catch (StoreFormatException) { Check(true, "the shipped terminal-selection profile refuses the composed marker"); }
        }
        var installed = await db.Database.SqlQueryRaw<string>("""
            SELECT tgname::text AS "Value" FROM pg_catalog.pg_trigger
            WHERE tgrelid = '"Instances"'::regclass AND NOT tgisinternal AND tgenabled IN ('O', 'A') AND tgname::text LIKE 'kernel_%'
            """).ToListAsync(ct);
        Check(new[] { "kernel_require_root_checkpoint_writer", "kernel_require_root_actor_list_v13", "kernel_project_root_actor_list_v13",
                "kernel_project_root_park_metadata", "kernel_project_root_workflow_history_v1" }.All(installed.Contains) &&
            !installed.Contains("kernel_project_root_actor_list"),
            "one install carries the qualified projector, the Park metadata projector and workflow history, and no v1 projector");

        // Seed two first checkpoints through the qualified update path the store guards, then end one and park the other.
        var header = DurableKeyJson.Write(new Header(Model, Definition, "{}", [new("definition", Definition)], false));
        db.Instances.AddRange(
            new InstanceEntity { Id = "0/ended", Partition = 0, Model = Model, Definition = Definition, Status = InstanceStatus.Running, State = "{}" },
            new InstanceEntity { Id = "0/parked", Partition = 0, Model = Model, Definition = Definition, Status = InstanceStatus.Running, State = "{}" });
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FoundationRootBirths" ("Reference", "Tenant", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
            VALUES ({Ended}, {Tenant}, '0/ended', 0, 1, 'creator-ended', 'seed'), ({Parked}, {Tenant}, '0/parked', 0, 1, 'creator-parked', 'seed');
            UPDATE "Instances" i SET "FoundationReference" = b."Reference", "FoundationTenant" = b."Tenant", "FoundationHeader" = {header},
                "Revision" = 1, "FoundationWriteRevision" = 1
            FROM "FoundationRootBirths" b WHERE b."Actor" = i."Id";
            """, ct);
        var born = await List(store, parked: null, ct);
        Check(born is [{ Status: InstanceStatus.Running, TerminalRevision: null, ParkAt: null, SupportsSelection: true },
            { Status: InstanceStatus.Running, TerminalRevision: null, ParkAt: null }], "both first checkpoints project as running and unmarked");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Instances" SET "Status" = {(int)InstanceStatus.Finished}, "Revision" = 2, "FoundationWriteRevision" = 2,
                "FoundationTerminalRevision" = 2, "FoundationTerminalAt" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            WHERE "Id" = '0/ended';
            UPDATE "Instances" SET "Status" = {(int)InstanceStatus.Faulted}, "Fault" = {DurableKeyJson.Write("parked on purpose")},
                "Revision" = 2, "FoundationWriteRevision" = 2, "FoundationParkAt" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint
            WHERE "Id" = '0/parked';
            """, ct);

        var page = await List(store, parked: null, ct);
        Check(page is [{ Reference: Ended, Status: InstanceStatus.Finished, TerminalRevision: 2, EndedAt: > 0, ParkAt: null, ParkFault: null },
                { Reference: Parked, Status: InstanceStatus.Faulted, TerminalRevision: null, EndedAt: null, ParkAt: > 0, ParkFault: "parked on purpose" }] &&
            page.All(item => item.SupportsSelection),
            "one list read projects a terminal mark and a Park mark together");
        Check(await List(store, parked: true, ct) is [{ Reference: Parked }] && await List(store, parked: false, ct) is [{ Reference: Ended }],
            "the parked filter separates the parked root from the ended one");
        Check(await store.ReadRootActorAsync(Tenant, Parked, ct) is ({ ParkAt: > 0, TerminalRevision: null }, > 0) &&
            await store.ReadRootActorAsync(Tenant, Ended, ct) is ({ TerminalRevision: 2, ParkAt: null }, > 0),
            "point reads carry the same marks");
        var history = await History(store, ct);
        Check(history.Items is [_, _] && history.Items.Select(item => item.Reference).Order(StringComparer.Ordinal).SequenceEqual([Ended, Parked]) &&
            history.Items.Single(item => item.Reference == Ended).UpdatedAt == page[0].EndedAt &&
            history.Items.All(item => item.Definition == Definition),
            "workflow history is ready on the qualified projector and records both roots");

        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" DISABLE TRIGGER kernel_project_root_park_metadata""", ct);
        await Refused(() => List(store, parked: null, ct), "the list requires the Park metadata projector");
        Check((await History(store, ct)).Items.Count == 2, "workflow history does not require the Park metadata projector");
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Instances" ENABLE TRIGGER kernel_project_root_park_metadata;
            ALTER TABLE "Instances" DISABLE TRIGGER kernel_project_root_actor_list_v13;
            """, ct);
        await Refused(() => History(store, ct), "workflow history requires the qualified projector");
        await Refused(() => List(store, parked: null, ct), "the list requires the qualified projector");
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" ENABLE TRIGGER kernel_project_root_actor_list_v13""", ct);
        Check((await List(store, parked: null, ct)).Count == 2 && (await History(store, ct)).Items.Count == 2,
            "both reads recover once the projectors are enabled again");

        // A second install over the seeded rows resyncs the projector and the Park backfill without losing either mark.
        await admission.InstallAsync(ct);
        await store.InstallRootWorkflowHistoryAsync(ct);
        Check(await List(store, parked: null, ct) is [{ Reference: Ended, TerminalRevision: 2, ParkAt: null },
                { Reference: Parked, TerminalRevision: null, ParkAt: > 0, ParkFault: "parked on purpose" }] &&
            (await History(store, ct)).Items.Count == 2,
            "reinstalling over existing rows keeps the terminal mark, the Park mark and history");

        // The shipped qualified formats keep refusing history before any read: 13 has no pinned namespace and 14 retires roots.
        // The connection never opens, so only the capability rule can refuse, and the message names it.
        var unreachable = new DbContextOptionsBuilder<KernelDb>().UseNpgsql("Host=history-refusal.invalid").Options;
        await RefusedByProfile(() => History(new EfStore(() => KernelDb.ForPostgresRootTerminalSelection(unreachable), postgres: true), ct),
            "the shipped terminal-selection profile still refuses workflow history before any read");
        await RefusedByProfile(() => History(new EfStore(() => KernelDb.ForPostgresRootRetirement(unreachable), postgres: true), ct),
            "the shipped retirement profile still refuses workflow history before any read");
        return 0;
    }

    private static async Task<IReadOnlyList<EfStore.RootListItem>> List(EfStore store, bool? parked, CancellationToken ct) =>
        (await store.ReadRootActorListAsync(Tenant, "", null, null, [], null, parked, 10, ct)).Items;

    private static Task<EfStore.RootWorkflowHistoryPage> History(EfStore store, CancellationToken ct) =>
        store.ReadRootWorkflowHistoryAsync(Tenant, Model, Definition, null, null, null, null, null, 10, ct);

    private static async Task Refused<T>(Func<Task<T>> read, string label)
    {
        try { await read(); }
        catch (StoreFormatException) { Check(true, label); return; }
        throw new InvalidOperationException("Expected a refusal: " + label);
    }

    private static async Task RefusedByProfile<T>(Func<Task<T>> read, string label)
    {
        try { await read(); }
        catch (StoreFormatException error) when (error.Message == "This store profile does not carry workflow history.")
        {
            Check(true, label);
            return;
        }
        throw new InvalidOperationException("Expected the profile refusal: " + label);
    }

    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        Console.WriteLine("ok " + label);
    }
}
