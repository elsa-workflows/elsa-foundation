using System.Runtime.CompilerServices;
using System.Text.Json;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// S5a (#4229): operator commands reach a parked root in a composed set. An unshipped closed set carries Park opening and
// inspection with the format 17 operator chain (terminal selection, termination, retirement and held replies); a second
// carries Park arrivals with terminal selection and held replies; a third carries Park inspection with jobs. A Terminate
// admitted while the root ran keeps its FIFO place while the Park opens and is refused there as "parked"; a held caller is
// answered "Parked". A Park keeps the root's Suspended flag, as the in-memory reference does, and every set with Park
// recovery refuses Suspend. S5b-1: the parked root's view seals its Park selection (revision and time), which admission
// requires for a Terminate, held or not, on a parked root. The owner ends the root in one commit when its kept rows fit
// one page: Cancelled and unsuspended with its terminal mark, every kept row cleared and its Park mark gone, journalled
// "terminated". A stale Park is refused as "selection" and a larger one as "page". Shipped formats carry none of these paths.
internal static class PostgresRootParkedOperatorChecks
{
    private static readonly StoreFormat Composed = StoreFormat.Current with { MessageProtocol = 95 };
    private static readonly StoreFormat ComposedJobs = StoreFormat.Current with { MessageProtocol = 94 };
    private static readonly StoreFormat ComposedArrivals = StoreFormat.Current with { MessageProtocol = 93 };
    private static readonly StoreFormat ComposedRecovery = StoreFormat.Current with { MessageProtocol = 92 };
    private const string JobsSchema = "parked_jobs";
    private const string ArrivalsSchema = "parked_arrivals";
    private const string Fault = "deliberate actor failure";
    private const string ProbeRefusal = """{"Outcome":"refused","Operation":"probe","Reason":"parked"}""";
    private const string ProbeTerminated = """{"Outcome":"terminated","Operation":"probe","Cleared":0}""";

    // The checkpoint, journal and refusal-outcome triggers. A probe that fails any other way is the probe's own error.
    private static readonly string[] TriggerMessages =
    [
        "Qualified actor checkpoint requires a qualified update",
        "Qualified actor journal requires the next running checkpoint revision",
        "An operator refusal entry requires the checkpoint write at its revision"
    ];

    private sealed record InputRow(long Seq, string Id, string Kind, bool Done, short? KeptBy, bool Content);

    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var capabilities = RootProfiles.Formats[StoreFormat.RootOperatorReplies] | RootProfiles.Formats[StoreFormat.RootParkInspection];
        KernelDb Open() => KernelDb.ForUnshippedComposition(options, Composed, capabilities);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(240));
        var stop = timeout.Token;
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the parked-operator fixture uses an empty PostgreSQL database");
        // The ingress turns the owner's refusal into the caller's error; a held caller of a parked root reads "parked".
        Check(PostgresRootIngress.RefusalError("Parked") == "parked" && PostgresRootIngress.RefusalError("Protected") == "protected" &&
                PostgresRootIngress.RefusalError("ProtectionUnavailable") == "protection unavailable" &&
                PostgresRootIngress.RefusalError("StaleSelection") == "stale selection" &&
                PostgresRootIngress.RefusalError("TargetMismatch") == "target" && PostgresRootIngress.RefusalError("Ended") == "missing",
            "the ingress reports a held command refused on a parked root as \"parked\"");
        Check(RootProfiles.MissingPrerequisites(capabilities) == RootCapabilities.None && db.UsesRootParkedOperators &&
                !db.UsesRootParkArrivals && db.UsesRootTerminalSelection && db.UsesRootOperatorReplies && db.UsesRootTerminateThenDelete,
            "the closed composed set carries Park opening and inspection with the format 17 operator chain");
        ShippedFormatsUnchanged();

        var tenant = EmbeddedTenant.Bind("parked");
        var main = new Set("parked", Open, owned, inBatch, tenant, stop);
        await main.InstallAsync(createTables: false);
        await using var mainOwner = await main.OwnAsync();
        var probe = main.Probe;
        var early = await main.StartAsync("early");
        var late = await main.StartAsync("late");
        var lone = await main.StartAsync("lone");
        var held = await main.StartAsync("held");
        var suspendedTerminate = await main.StartAsync("suspended-terminate");
        var suspendedResume = await main.StartAsync("suspended-resume");
        var running = await main.StartAsync("running");
        probe.FailsOn.Add((lone.Id, Contract.Kinds.Terminate));
        probe.FailsOn.Add((suspendedTerminate.Id, Contract.Kinds.Terminate));
        probe.FailsOn.Add((suspendedResume.Id, Contract.Kinds.Resume));

        // A Terminate admitted behind the failing input keeps its row and FIFO place while the Park opens, then is refused.
        Check(await main.SendAsync(early, "fail-early", "fail") && await main.TerminateAsync(early, "terminate-early"),
            "the failing input and a selected Terminate behind it are admitted while the root runs");
        var stored = await main.StoredOperatorAsync(early);
        await main.DriveAsync(early, image => image is { Revision: 3, Status: InstanceStatus.Faulted });
        await main.SettledAsync(early, "terminate-early");
        Check(await main.InputsAsync(early) is [{ Kind: "fail", Done: true, KeptBy: (short)ParkKeepReason.Consumed } fail,
                { Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null } command] && fail.Seq < command.Seq &&
                !probe.Saw(early, Contract.Kinds.Terminate),
            "the Park kept only the failing input; the Terminate kept its place and never reached the actor");

        // S5b: a parked root's view seals its Park selection. Admission refuses the running selection, another root's Park
        // and a stale Park, and admits the root's own Park, which passes a withheld ordinary input and ends the root in one
        // commit: Cancelled with its terminal mark, its kept fault input cleared and its Park mark gone.
        var due = (await main.ImageAsync(late)).AsOf + 1_500;
        Check(await main.SendAsync(late, "withheld", "ordinary", due) && await main.SendAsync(late, "fail-late", "fail"),
            "a future input and a failing one are admitted");
        var before = await main.DriveAsync(late, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
        while ((await main.ImageAsync(late)).AsOf <= due) await Task.Delay(100, stop);
        await ReceiptsPinned();
        var parkLate = await main.ParkSelectionAsync(late, before.Revision);
        Check(!await main.TerminateAsync(late, "running-late") &&
                !await main.TerminateAsync(early, "terminate-other", parkLate) &&
                !await main.TerminateAsync(late, "terminate-stale", main.SealPark(late, before.Revision + 1, (await main.ParkAtAsync(before))!.Value)) &&
                !await main.OperateAsync(late, "then-delete-parked", Contract.Kinds.Terminate, parkLate, thenDelete: true),
            "admission refuses a parked root's running selection, another root's Park, a stale Park and Terminate and delete");
        Check(await main.TerminateAsync(late, "terminate-late", parkLate), "a Terminate with the root's Park selection is admitted");
        var inbox = (await main.Cold.ReadRootInboxAsync(tenant.Tenant, late.Id, 0, "", 10, stop)).Rows;
        Check(inbox is [{ Id: "withheld", KeptBy: null }, { Id: "fail-late", KeptBy: ParkKeepReason.Consumed }, { Id: "terminate-late", KeptBy: null }],
            "inspection lists the withheld input, the kept fault input and the pending Terminate in FIFO order");
        await main.DriveAsync(late, image => image is { Revision: 3, Status: InstanceStatus.Cancelled });
        await main.TerminatedAsync(late, "terminate-late", cleared: 1);
        await main.SettledInputsAsync(late);
        Check(await main.InputsAsync(late) is [{ Id: var withheld, Done: true }, { Kind: "fail", Done: true, KeptBy: null, Content: false },
                { Kind: Contract.Kinds.Terminate, Done: true }] && withheld.StartsWith(late.Id, StringComparison.Ordinal) &&
                !probe.Saw(late, "ordinary") && !probe.Saw(late, Contract.Kinds.Terminate) &&
                !await main.TerminateAsync(late, "terminate-ended", parkLate),
            "the Terminate cleared the kept input, never reached the actor, the ended root refused the withheld input, and the Park selection is spent");

        // A Terminate whose own turn fails opens the Park without it; the owner polls it again and refuses it.
        Check(await main.TerminateAsync(lone, "terminate-lone"), "a Terminate is admitted for the root whose terminate turn fails");
        await main.DriveAsync(lone, image => image is { Revision: 3, Status: InstanceStatus.Faulted });
        await main.SettledAsync(lone, "terminate-lone");
        Check(await main.InputsAsync(lone) is [{ Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null }] && probe.Saw(lone, Contract.Kinds.Terminate),
            "the failed Terminate opened the Park outside the kept set and settled on the parked root");

        // A held command admitted while the root runs, behind the input that parks it, is refused on the parked root with a
        // journalled reason, and its caller is answered "Parked". A Terminate with the Park selection behind it ends the root.
        Check(await main.SendAsync(held, "fail-held", "fail") &&
                await main.OperateAsync(held, "held-terminate", Contract.Kinds.Terminate, main.Selections[held], reply: true),
            "a failing input and a held Terminate behind it are admitted while the root runs");
        var heldPark = await main.DriveAsync(held, image => image is { Revision: >= 2, Status: InstanceStatus.Faulted });
        Check(await main.TerminateAsync(held, "after-held", await main.ParkSelectionAsync(held, 2)),
            "a Terminate with the Park selection is admitted behind the held one on the parked root");
        await main.DriveAsync(held, image => image is { Revision: 4 });
        var heldJournal = await main.Cold.ReadJournalOfAsync(0, heldPark.Actor.Id, 0, stop);
        Check(heldJournal is [.., { Entries: """["parked"]""" } park, var heldRefusal, _] && park.Revision == 2 &&
                heldRefusal is { Revision: 3, Entries: """{"Outcome":"refused","Operation":"held-terminate","Reason":"parked"}""" } &&
                await main.InputsAsync(held) is [{ Kind: "fail", Done: true, KeptBy: null },
                    { Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null }, { Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null }] &&
                !probe.Saw(held, Contract.Kinds.Terminate),
            "the owner refuses the held Terminate on the parked root, then ends the root with the Terminate behind it");
        await main.TerminatedAsync(held, "after-held", cleared: 1);
        main.Answered("held-terminate", "Parked");

        // Admission on a parked root: a Suspend, a Resume, a held running selection and a held Terminate and delete are refused.
        var parkEarly = await main.ParkSelectionAsync(early, 2);
        Check(!await main.OperateAsync(early, "suspend-parked", Contract.Kinds.Suspend) &&
                !await main.OperateAsync(early, "resume-parked", Contract.Kinds.Resume) &&
                !await main.OperateAsync(early, "held-parked", Contract.Kinds.Terminate, main.Selections[early], reply: true) &&
                !await main.OperateAsync(early, "held-then-delete-parked", Contract.Kinds.Terminate, parkEarly, reply: true, thenDelete: true),
            "admission refuses a Suspend, a Resume, a held running selection and a held Terminate and delete for a parked root");

        // A Park selection the owner finds stale is refused there with a recorded reason, the Park stays and a held caller
        // is answered with the stale selection.
        Check(await main.OperateAsync(early, "terminate-moved", Contract.Kinds.Terminate, parkEarly, reply: true),
            "a held Terminate with the Park selection is admitted");
        var earlyImage = await main.ImageAsync(early);
        var earlyAt = await main.ParkAtAsync(earlyImage);
        await main.BypassAsync(earlyImage, $""" "FoundationParkAt" = {earlyAt + 1} """);
        try { await main.DriveAsync(early, image => image is { Revision: 4 }); }
        finally { await main.BypassAsync(earlyImage, $""" "FoundationParkAt" = {earlyAt} """); }
        await main.SettledAsync(early, "terminate-moved", reason: "selection");
        main.Answered("terminate-moved", "StaleSelection");

        // Two Terminates with the Park selection: the first ends the root; the second settles on the ended root unjournalled.
        Check(!await main.OperateAsync(lone, "pair-then-delete", Contract.Kinds.Terminate, main.Selections[lone], thenDelete: true),
            "admission refuses Terminate and delete with the running selection for the parked root");
        var parkLone = await main.ParkSelectionAsync(lone, 2);
        Check(await main.TerminateAsync(lone, "pair-first", parkLone) && await main.TerminateAsync(lone, "pair-second", parkLone),
            "two Terminates with the Park selection are admitted for the parked root");
        await main.DriveAsync(lone, image => image is { Revision: 4, Status: InstanceStatus.Cancelled });
        await main.SettledInputsAsync(lone);
        await main.TerminatedAsync(lone, "pair-first", cleared: 0);
        Check((await main.Cold.ReadJournalOfAsync(0, (await main.ImageAsync(lone)).Actor.Id, 0, stop)).Count(entry => entry.Entries.Contains("\"terminated\"")) == 1 &&
                await main.InputsAsync(lone) is [{ Done: true }, { Done: true }, { Done: true }],
            "the first Terminate ends the root; the second settles on the ended root with no journal entry");

        // A Terminate whose turn fails on a suspended root opens a Park that keeps Suspended. The owner refuses it there.
        Check(await main.OperateAsync(suspendedTerminate, "suspend-first", Contract.Kinds.Suspend), "a Suspend is admitted for a running root");
        var suspended = await main.DriveAsync(suspendedTerminate, image => image is { Revision: 2, Status: InstanceStatus.Running, Suspended: true });
        Check(await main.TerminateAsync(suspendedTerminate, "terminate-suspended"), "a Terminate is admitted for the suspended root");
        await main.DriveAsync(suspendedTerminate, image => image is { Revision: 4, Status: InstanceStatus.Faulted });
        await main.SettledAsync(suspendedTerminate, "terminate-suspended", suspended.State, suspended: true);
        Check(await main.InputsAsync(suspendedTerminate) is [{ Kind: Contract.Kinds.Suspend, Done: true }, { Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null }] &&
                probe.Saw(suspendedTerminate, Contract.Kinds.Terminate),
            "the failed Terminate on the suspended root parked it suspended, outside the kept set, and settled there");

        // A Resume whose turn fails parks the root suspended. A held Terminate with the Park selection ends it, no longer
        // suspended, as the in-memory drain's Complete does, and its caller is answered once.
        Check(await main.OperateAsync(suspendedResume, "suspend-second", Contract.Kinds.Suspend), "a Suspend is admitted for the second root");
        suspended = await main.DriveAsync(suspendedResume, image => image is { Revision: 2, Suspended: true });
        Check(await main.OperateAsync(suspendedResume, "resume-fails", Contract.Kinds.Resume), "a Resume is admitted for the suspended root");
        await main.DriveAsync(suspendedResume, image => image is { Revision: 4, Status: InstanceStatus.Faulted });
        await main.SettledAsync(suspendedResume, "resume-fails", suspended.State, suspended: true);
        Check(!await main.OperateAsync(suspendedResume, "resume-parked", Contract.Kinds.Resume) &&
                !await main.TerminateAsync(suspendedResume, "running-suspended-park") &&
                await main.OperateAsync(suspendedResume, "terminate-suspended-park", Contract.Kinds.Terminate,
                    await main.ParkSelectionAsync(suspendedResume, 3), reply: true),
            "admission refuses a Resume and the running selection, and admits a held Terminate with the Park selection, for the suspended Park");
        await main.DriveAsync(suspendedResume, image => image is { Revision: 5, Status: InstanceStatus.Cancelled });
        await main.TerminatedAsync(suspendedResume, "terminate-suspended-park", cleared: 0, suspended.State);
        main.Answered("terminate-suspended-park", "terminated");

        await RefusalTriggers();
        await ParkedRootPredicates();

        // Format 22 still refuses Terminate at admission and at decode.
        var unreachable = new DbContextOptionsBuilder<KernelDb>().UseNpgsql("Host=parked-refusal.invalid").Options;
        Check(await new PostgresRootAdmission(() => KernelDb.ForPostgresRootParkRecovery(unreachable), TimeSpan.FromHours(1))
                .AdmitRootOperatorAsync(tenant.Tenant, early.Id, "format-22", Contract.Kinds.Terminate, null, null, stop, main.Selections[early]) is null,
            "format 22 refuses a Terminate at admission before it opens a connection");
        try
        {
            PostgresRootAdmission.DecodeOperator(KernelDb.ForPostgresRootParkRecovery(unreachable), stored, early.Id);
            throw new InvalidOperationException("Format 22 decoded a stored Terminate.");
        }
        catch (StoreException error) when (error.Message == "Unexpected stored one-way operator envelope.")
        {
            Check(true, "format 22 refuses a stored Terminate at decode");
        }
        await RecoverNeverMeetsSuspendedPark();
        await ArrivalsSet();
        await JobsInspection();
        return 0;

        // A Park can be suspended only where Suspend is admitted. Every set with Park recovery, shipped or composed with
        // termination, admits Recover and no other operator command, so Recover never reaches a suspended Park.
        async Task RecoverNeverMeetsSuspendedPark()
        {
            var recoveryTerminations = RootProfiles.Formats[StoreFormat.RootParkRecovery] | RootProfiles.Formats[StoreFormat.RootTerminalSelection];
            Check(RootProfiles.MissingPrerequisites(recoveryTerminations) == RootCapabilities.None, "Park recovery composes with termination in a closed set");
            foreach (var (label, recovery) in new (string, Func<KernelDb>)[]
            {
                ("format 22", () => KernelDb.ForPostgresRootParkRecovery(unreachable)),
                ("the composed recovery set", () => KernelDb.ForUnshippedComposition(unreachable, ComposedRecovery, recoveryTerminations))
            })
            {
                var refusing = new PostgresRootAdmission(recovery, TimeSpan.FromHours(1));
                foreach (var kind in new[] { Contract.Kinds.Suspend, Contract.Kinds.Resume })
                {
                    bool refused;
                    try { refused = await refusing.AdmitRootOperatorAsync(tenant.Tenant, early.Id, "suspend-recovery", kind, null, null, stop) is null; }
                    catch (Exception) { refused = false; }     // reaching the unreachable host means admission went on
                    Check(refused, $"{label} refuses a {kind} at admission before it opens a connection, so no Park there is suspended");
                }
            }
        }

        void ShippedFormatsUnchanged()
        {
            Check(RootProfiles.Formats.Values.All(set => (set & RootCapabilities.ParkOpening) == 0 || (set & RootCapabilities.Termination) == 0),
                "no shipped format carries Park with termination, so formats 19-22 and 5, 11-17 keep their paths");
            foreach (var shipped in new Func<DbContextOptions<KernelDb>, KernelDb>[] { KernelDb.ForPostgresRootParkOpening,
                KernelDb.ForPostgresRootParkInspection, KernelDb.ForPostgresRootParkArrivals, KernelDb.ForPostgresRootParkRecovery,
                KernelDb.ForPostgresRootOperators, KernelDb.ForPostgresRootTerminations, KernelDb.ForPostgresRootOperatorReplies })
                if (shipped(options) is var profile && (profile.UsesRootParkedOperators ||
                    profile.RecordsRootParkRevision != profile.UsesRootParkArrivals || profile.ProjectsRootParkRevision != profile.UsesRootParkRecovery))
                    throw new InvalidOperationException("A shipped profile changed its parked-operator or Park revision paths.");
            Check(true, "the shipped Park and operator profiles do not use the parked-operator paths and keep their Park revision columns");
        }

        // The Park pin covers the operator receipts: a receipts view in the marker schema fails the pin before admission.
        async Task ReceiptsPinned()
        {
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "FoundationOperatorReceipts" RENAME TO fixture_receipts;
                CREATE VIEW "FoundationOperatorReceipts" AS SELECT * FROM fixture_receipts;
                """, stop);
            try
            {
                await main.Admission.AdmitRootOperatorAsync(tenant.Tenant, late.Id, "pinned", Contract.Kinds.Terminate, null, null, stop, main.Selections[late]);
                throw new InvalidOperationException("A Terminate was admitted beside an unpinned receipts relation.");
            }
            catch (StoreFormatException error) when (error.Message == "Park requires the source tables in the marker schema.")
            {
                Check(true, "the Park pin refuses a Terminate whose receipts relation is not a marker-schema table");
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("""
                    DROP VIEW "FoundationOperatorReceipts"; ALTER TABLE fixture_receipts RENAME TO "FoundationOperatorReceipts";
                    """, stop);
            }
        }

        // Raw writes against the checkpoint, journal and refusal-outcome triggers, each rolled back.
        async Task RefusalTriggers()
        {
            var parked = await main.ImageAsync(early);
            var live = await main.ImageAsync(running);
            var suspendedPark = await main.ImageAsync(suspendedTerminate);
            Check(!await main.TriggerProbeAsync(parked, null, Continue(parked)),
                "the checkpoint trigger refuses a parked revision move with no refused entry");
            Check(await main.TriggerProbeAsync(parked, ProbeRefusal, Continue(parked)) &&
                    await main.TriggerProbeAsync(suspendedPark, ProbeRefusal, Continue(suspendedPark)),
                "a refused entry with only the revision moved passes the triggers on a parked root, suspended or not (rolled back)");
            // Every column of the row: the change alone passes on a Running root, beside a refused entry it is refused on both.
            foreach (var (column, change) in RowChanges(wire: true, parkRevision: true))
            {
                Check(await main.TriggerProbeAsync(live, null, Change(live, change)) &&
                        !await main.TriggerProbeAsync(live, ProbeRefusal, Change(live, change)) &&
                        !await main.TriggerProbeAsync(parked, ProbeRefusal, Change(parked, change)),
                    $"a refused entry is refused when {column} changes, on a Running and on a parked root");
            }
            // A lone refused entry passes the insert trigger but is refused at commit, so the next write at that revision still lands.
            Check(!await main.LoneEntryCommitsAsync(parked, ProbeRefusal) && !await main.LoneEntryCommitsAsync(live, ProbeRefusal),
                "a lone refused entry with no checkpoint write is refused at commit, on a parked and on a Running root");
            Check(await main.TriggerProbeAsync(parked, ProbeRefusal, Continue(parked)) &&
                    (await main.Cold.ReadJournalOfAsync(0, parked.Actor.Id, 0, stop)).All(entry => entry.Revision <= parked.Revision) &&
                    (await main.Cold.ReadJournalOfAsync(0, live.Actor.Id, 0, stop)).All(entry => entry.Revision <= live.Revision),
                "after a refused lone entry the parked and Running roots stay writable at the next revision");
            // S5b: a parked root with no kept row ends beside a terminated entry (rolled back). Without that entry, beside a
            // refused one, with its Park mark kept, with its state changed or with a kept row left, the write is refused.
            Check(await main.TriggerProbeAsync(suspendedPark, ProbeTerminated, End(suspendedPark)) &&
                    !await main.TriggerProbeAsync(suspendedPark, null, End(suspendedPark)) &&
                    !await main.TriggerProbeAsync(suspendedPark, ProbeRefusal, End(suspendedPark)) &&
                    !await main.TriggerProbeAsync(suspendedPark, ProbeTerminated, End(suspendedPark, parkAt: "\"FoundationParkAt\"")) &&
                    !await main.TriggerProbeAsync(suspendedPark, ProbeTerminated, End(suspendedPark, state: "\"State\" || ' '")) &&
                    !await main.TriggerProbeAsync(parked, ProbeTerminated, End(parked)),
                "a terminated entry passes only with the parked root ended, its Park mark cleared, its state kept and no kept row left");
            Check(!await main.LoneEntryCommitsAsync(suspendedPark, ProbeTerminated) && !await main.LoneEntryCommitsAsync(live, ProbeTerminated),
                "a lone terminated entry with no checkpoint write is refused at commit, on a parked and on a Running root");
        }

        // The parked-root admission predicates, each broken alone through a bypassed checkpoint write and then restored.
        async Task ParkedRootPredicates()
        {
            var image = await main.ImageAsync(early);
            var parkAt = await main.ParkAtAsync(image);
            foreach (var (predicate, broken, restored) in new (string, string, string)[]
            {
                ("lacks a Park mark", """ "FoundationParkAt" = NULL """, $""" "FoundationParkAt" = {parkAt} """),
                ("has a terminal revision", $""" "FoundationTerminalRevision" = {image.Revision} """, """ "FoundationTerminalRevision" = NULL """)
            })
            {
                await main.BypassAsync(image, broken);
                bool admitted;
                try { admitted = await main.TerminateAsync(early, "predicate-" + predicate.Length, parkEarly); }
                finally { await main.BypassAsync(image, restored); }
                Check(!admitted, $"admission refuses a Park selection for a Faulted root that {predicate}");
            }
            Check(await main.TerminateAsync(early, "predicate-control", parkEarly), "the restored parked root admits its Park selection again");
        }

        // A composed set with Park arrivals and termination journals the refusal through the arrivals branch and keeps
        // arrivals on a parked root, suspended or not. A held command is refused there and the arrival behind it is kept.
        async Task ArrivalsSet()
        {
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + ArrivalsSchema, stop);
            var arrivalOptions = new DbContextOptionsBuilder<KernelDb>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = ArrivalsSchema }.ConnectionString).Options;
            var arrivalCapabilities = RootProfiles.Formats[StoreFormat.RootParkArrivals] | RootProfiles.Formats[StoreFormat.RootTerminalSelection] |
                RootCapabilities.OperatorReplies;
            KernelDb OpenArrivals() => KernelDb.ForUnshippedComposition(arrivalOptions, ComposedArrivals, arrivalCapabilities);
            await using (var check = OpenArrivals())
                Check(RootProfiles.MissingPrerequisites(arrivalCapabilities) == RootCapabilities.None && check.UsesRootParkedOperators &&
                        check.UsesRootParkArrivals && !check.UsesRootParkRecovery && check.UsesRootStableBody,
                    "the closed arrivals set carries Park arrivals with terminal selection, termination and held replies");
            var set = new Set("parked-arrivals", OpenArrivals, owned, inBatch, tenant, stop);
            await set.InstallAsync(createTables: true);
            await using var owner = await set.OwnAsync();
            var plain = await set.StartAsync("arrivals-plain");
            var asleep = await set.StartAsync("arrivals-suspended");
            var holding = await set.StartAsync("arrivals-held");
            var awake = await set.StartAsync("arrivals-running");
            var large = await set.StartAsync("arrivals-large");
            set.Probe.FailsOn.Add((asleep.Id, Contract.Kinds.Resume));

            // A Terminate with the Park selection clears the kept fault input and the kept arrival behind it, and ends the root.
            Check(await set.SendAsync(plain, "arrivals-fail", "fail"), "the arrivals set admits a failing input");
            await set.DriveAsync(plain, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(await set.SendAsync(plain, "arrivals-after", "ordinary"), "the arrivals set admits an arrival for the parked root");
            await set.DriveAsync(plain, image => image is { Revision: 3 });
            Check(!await set.TerminateAsync(plain, "arrivals-running") &&
                    await set.TerminateAsync(plain, "arrivals-terminate", await set.ParkSelectionAsync(plain, 2)),
                "the arrivals set refuses the running selection and admits the Park selection for the parked root");
            await set.DriveAsync(plain, image => image is { Revision: 4, Status: InstanceStatus.Cancelled });
            await set.TerminatedAsync(plain, "arrivals-terminate", cleared: 2);
            Check(await set.InputsAsync(plain) is [{ Kind: "fail", Done: true, KeptBy: null, Content: false },
                    { Kind: "ordinary", Done: true, KeptBy: null, Content: false }, { Kind: Contract.Kinds.Terminate, Done: true }],
                "the Terminate cleared the kept fault input and the kept arrival");

            Check(await set.OperateAsync(asleep, "arrivals-suspend", Contract.Kinds.Suspend), "the arrivals set admits a Suspend");
            var sleeping = await set.DriveAsync(asleep, image => image is { Revision: 2, Suspended: true });
            Check(await set.OperateAsync(asleep, "arrivals-resume", Contract.Kinds.Resume), "the arrivals set admits a Resume for the suspended root");
            await set.DriveAsync(asleep, image => image is { Revision: 4, Status: InstanceStatus.Faulted });
            await set.SettledAsync(asleep, "arrivals-resume", sleeping.State, suspended: true);
            Check(await set.SendAsync(asleep, "arrivals-asleep", "ordinary"), "the arrivals set admits an arrival for the root parked while suspended");
            var kept = await set.DriveAsync(asleep, image => image is { Revision: 5 });
            Check(kept is { Status: InstanceStatus.Faulted, Suspended: true } && await set.InputsAsync(asleep) is
                    [_, { Kind: Contract.Kinds.Resume, Done: true }, { Kind: "ordinary", Done: true, KeptBy: (short)ParkKeepReason.Arrived }],
                "the Park keeps an arrival on the root parked while suspended, and the root stays suspended");
            Check(!await set.TerminateAsync(asleep, "arrivals-running-asleep") &&
                    await set.TerminateAsync(asleep, "arrivals-terminate-asleep", await set.ParkSelectionAsync(asleep, 3)),
                "the arrivals set admits only the Park selection for the root parked while suspended");
            await set.DriveAsync(asleep, image => image is { Revision: 6, Status: InstanceStatus.Cancelled });
            await set.TerminatedAsync(asleep, "arrivals-terminate-asleep", cleared: 1, sleeping.State);

            // A Park larger than one page is refused with a recorded reason, and keeps every row.
            Check(await set.SendAsync(large, "arrivals-large-fail", "fail"), "the arrivals set admits a failing input for the large Park");
            await set.DriveAsync(large, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            for (var index = 0; index < InMemoryStore.ParkPageLimit; index++)
                if (!await set.SendAsync(large, $"arrivals-large-{index}", "ordinary"))
                    throw new InvalidOperationException($"The arrivals set refused arrival {index} for the large Park.");
            await set.SettledInputsAsync(large);
            Check(await set.TerminateAsync(large, "arrivals-large-terminate", await set.ParkSelectionAsync(large, 2)),
                $"the arrivals set admits a Terminate with the Park selection for a Park of {InMemoryStore.ParkPageLimit + 1} kept rows");
            var largeImage = await set.ImageAsync(large);
            await set.DriveAsync(large, image => image!.Revision == largeImage.Revision + 1);
            await set.SettledAsync(large, "arrivals-large-terminate", reason: "page");
            Check((await set.InputsAsync(large)).Count(row => row.KeptBy is not null) == InMemoryStore.ParkPageLimit + 1,
                "the refused Terminate leaves every kept row of the large Park kept");

            Check(await set.SendAsync(holding, "arrivals-held-fail", "fail") &&
                    await set.OperateAsync(holding, "arrivals-held-terminate", Contract.Kinds.Terminate, set.Selections[holding], reply: true),
                "the arrivals set admits a failing input and a held Terminate behind it");
            await set.DriveAsync(holding, image => image is { Revision: >= 2, Status: InstanceStatus.Faulted });
            Check(await set.SendAsync(holding, "arrivals-behind-held", "ordinary") &&
                    !await set.OperateAsync(holding, "arrivals-held-parked", Contract.Kinds.Terminate, set.Selections[holding], reply: true),
                "the arrivals set admits an arrival and refuses a held Terminate for the parked root");
            await set.DriveAsync(holding, image => image is { Revision: 4 });
            Check((await set.Cold.ReadJournalOfAsync(0, (await set.ImageAsync(holding)).Actor.Id, 0, stop)).Any(entry => entry is
                    { Revision: 3, Entries: """{"Outcome":"refused","Operation":"arrivals-held-terminate","Reason":"parked"}""" }) &&
                    await set.InputsAsync(holding) is [{ Kind: "fail", KeptBy: (short)ParkKeepReason.Consumed },
                        { Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null }, { Kind: "ordinary", Done: true, KeptBy: (short)ParkKeepReason.Arrived }] &&
                    !set.Probe.Saw(holding, Contract.Kinds.Terminate),
                "the owner refuses the held Terminate on the parked root and the Park keeps the arrival behind it");
            set.Answered("arrivals-held-terminate", "Parked");

            var parked = await set.ImageAsync(holding);
            var live = await set.ImageAsync(awake);
            Check(await set.TriggerProbeAsync(parked, ProbeRefusal, Continue(parked)),
                "in the arrivals set a refused entry with only the revision moved passes the triggers (rolled back)");
            foreach (var (column, change) in RowChanges(wire: true, parkRevision: true))
                Check(await set.TriggerProbeAsync(live, null, Change(live, change)) &&
                        !await set.TriggerProbeAsync(live, ProbeRefusal, Change(live, change)) &&
                        !await set.TriggerProbeAsync(parked, ProbeRefusal, Change(parked, change)),
                    $"in the arrivals set a refused entry is refused when {column} changes, on a Running and on a parked root");
            Check(!await set.LoneEntryCommitsAsync(parked, ProbeRefusal), "in the arrivals set a lone refused entry is refused at commit");
        }

        // A composed Park set with jobs inspects its direct inputs, kept and pending, and never lists its job rows.
        async Task JobsInspection()
        {
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + JobsSchema, stop);
            var jobsOptions = new DbContextOptionsBuilder<KernelDb>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = JobsSchema }.ConnectionString).Options;
            var jobs = RootProfiles.Formats[StoreFormat.RootParkInspection] | RootCapabilities.JobProtocol;
            KernelDb OpenJobs() => KernelDb.ForUnshippedComposition(jobsOptions, ComposedJobs, jobs);
            await using (var created = OpenJobs()) await created.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(stop);
            var store = new EfStore(OpenJobs, postgres: true);
            await store.CreateAsync(1);
            var jobsAdmission = new PostgresRootAdmission(OpenJobs, TimeSpan.FromHours(1));
            await jobsAdmission.InstallAsync(stop);
            var jobsIngress = new PostgresRootIngress(jobsAdmission, Probe.Type);
            var root = (await jobsIngress.SendAsync(tenant, new Contract.Target.Start(new(Probe.Type, "{}")), "jobs-root", Contract.Kinds.Start,
                cancellationToken: stop)).Actor!.Value;
            await using var seed = OpenJobs();
            var actor = await seed.Database.SqlQuery<string>($"""
                SELECT "Actor" AS "Value" FROM "FoundationRootBirths" WHERE "Tenant" = {tenant.Tenant} AND "Reference" = {root.Id}
                """).SingleAsync(stop);
            seed.Instances.Add(new InstanceEntity { Id = actor, Partition = 0, Model = Probe.Type, Definition = "", Status = InstanceStatus.Running, State = "{}" });
            await seed.SaveChangesAsync(stop);
            var header = DurableKeyJson.Write(new { Model = Probe.Type, Definition = "", TypeData = "{}", Labels = Array.Empty<KeyValuePair<string, string>>(), EscapedState = false });
            Check(await seed.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "FoundationReference" = {root.Id}, "FoundationTenant" = {tenant.Tenant}, "FoundationHeader" = {header},
                    "Revision" = 1, "FoundationWriteRevision" = 1 WHERE "Id" = {actor}
                """, stop) == 1, "the jobs fixture seeds the running root");
            foreach (var id in new[] { "kept", "pending" })
                Check(await jobsIngress.SendAsync(tenant, new Contract.Target.Ref(root), id, "ordinary", "{}", cancellationToken: stop) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, $"the jobs set admits the {id} direct input");
            Check(await seed.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Status" = {(int)InstanceStatus.Faulted}, "Fault" = {DurableKeyJson.Write(Fault)}, "Revision" = 2,
                    "FoundationWriteRevision" = 2, "FoundationParkAt" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint
                WHERE "FoundationReference" = {root.Id};
                UPDATE "Messages" SET "Done" = true, "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
                WHERE "InboxScope" = -4 AND "Id" LIKE {root.Id + "/%"} AND "Seq" = (SELECT min("Seq") FROM "Messages" WHERE "InboxScope" = -4 AND "Id" LIKE {root.Id + "/%"});
                INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Due", "Seen", "Attempts", "Trial", "Done", "JobGeneration")
                SELECT -6, 'job/' || b."Actor" || '/0', b."Partition", b."Actor", 'kernel.job', 0, 0, 0, 0, false, 1
                FROM "FoundationRootBirths" b WHERE b."Reference" = {root.Id}
                """, stop) >= 3, "the jobs fixture parks the root, keeps its first input and adds a job row");
            Check((await store.ReadRootInboxAsync(tenant.Tenant, root.Id, 0, "", 10, stop)).Rows is
                    [{ Id: "kept", KeptBy: ParkKeepReason.Consumed }, { Id: "pending", KeptBy: null }],
                "inspection of the Park and jobs set lists the kept and the pending input and no job row");
            try
            {
                await new EfStore(() => KernelDb.ForPostgresRootJobs(unreachable), postgres: true).ReadRootInboxAsync(tenant.Tenant, root.Id, 0, "", 10, stop);
                throw new InvalidOperationException("The shipped jobs format inspected its inbox.");
            }
            catch (NotSupportedException)
            {
                Check(true, "the shipped jobs format still refuses inbox inspection");
            }
        }
    }

    private static FormattableString Continue(EfStore.RootImage root) => $"""
        UPDATE "Instances" SET "Revision" = "Revision" + 1, "FoundationWriteRevision" = "Revision" + 1 WHERE "Id" = {root.Actor.Id}
        """;

    // The parked root's end as the terminate commit writes it, with the Park mark and state given.
    private static FormattableString End(EfStore.RootImage root, string parkAt = "NULL", string state = "\"State\"") => FormattableStringFactory.Create(
        "UPDATE \"Instances\" SET \"Revision\" = \"Revision\" + 1, \"FoundationWriteRevision\" = \"Revision\" + 1, \"Status\" = 3, " +
        "\"Suspended\" = false, \"FoundationParkAt\" = " + parkAt + ", \"FoundationParkRevision\" = NULL, " +
        "\"FoundationTerminalRevision\" = \"Revision\" + 1, \"FoundationTerminalAt\" = 1, \"State\" = " + state + " WHERE \"Id\" = {0}", root.Actor.Id);

    private static FormattableString Change(EfStore.RootImage root, string change) => FormattableStringFactory.Create(
        "UPDATE \"Instances\" SET \"Revision\" = \"Revision\" + 1, \"FoundationWriteRevision\" = \"Revision\" + 1, " + change +
        " WHERE \"Id\" = {0}", root.Actor.Id);

    // One change per Instances column besides the identity, revision and fence columns. The state wire exists with a stable
    // body and the Park revision with Park arrivals. Retired has its own guard, the physical retirement trigger, which runs first.
    private static IEnumerable<(string Column, string Change)> RowChanges(bool wire, bool parkRevision)
    {
        yield return ("Status", "\"Status\" = 3");
        yield return ("Suspended", "\"Suspended\" = NOT \"Suspended\"");
        yield return ("State", "\"State\" = \"State\" || ' '");
        yield return ("StateVersion", "\"StateVersion\" = \"StateVersion\" + 1");
        yield return ("FoundationHeader", "\"FoundationHeader\" = \"FoundationHeader\" || ' '");
        yield return ("Fault", "\"Fault\" = coalesce(\"Fault\", '\"\"') || ' '");
        yield return ("History", "\"History\" = CASE WHEN \"History\" = 0 THEN 1 ELSE 0 END");
        yield return ("Jobs", "\"Jobs\" = coalesce(\"Jobs\", '') || 'x'");
        yield return ("Children", "\"Children\" = coalesce(\"Children\", '') || 'x'");
        yield return ("Parent", "\"Parent\" = coalesce(\"Parent\", '') || 'x'");
        yield return ("FoundationParkAt", "\"FoundationParkAt\" = coalesce(\"FoundationParkAt\", 0) + 1");
        yield return ("FoundationTerminalRevision", "\"FoundationTerminalRevision\" = \"Revision\" + 1");
        yield return ("FoundationTerminalAt", "\"FoundationTerminalAt\" = coalesce(\"FoundationTerminalAt\", 0) + 1");
        yield return ("Model", "\"Model\" = \"Model\" || 'x'");
        yield return ("Definition", "\"Definition\" = \"Definition\" || 'x'");
        yield return ("NextJobGeneration", "\"NextJobGeneration\" = coalesce(\"NextJobGeneration\", 0) + 1");
        yield return ("CustomHandles", "\"CustomHandles\" = coalesce(\"CustomHandles\", '') || 'x'");
        if (wire) yield return ("FoundationStateWire", "\"FoundationStateWire\" = coalesce(\"FoundationStateWire\", ''::bytea) || '\\x00'::bytea");
        if (parkRevision) yield return ("FoundationParkRevision", "\"FoundationParkRevision\" = coalesce(\"FoundationParkRevision\", 0) + 1");
    }

    // One composed set: its store, admission, ingress, reads and the probe actor behind one owner of partition 0.
    private static readonly TimeSpan FixtureLease = TimeSpan.FromSeconds(180);
    private const string HeldCaller = "parked-caller";

    // The owner's reply transport: every reply it sends, with its node.
    private sealed class ReplyLog : IReplyTransport
    {
        private readonly List<(string Node, Reply Reply)> _sent = [];
        internal IReadOnlyList<(string Node, Reply Reply)> Sent { get { lock (_sent) return [.. _sent]; } }
        public void Send(string node, Reply reply) { lock (_sent) _sent.Add((node, reply)); }
    }

    private sealed class Set(string name, Func<KernelDb> open, bool owned, bool inBatch, Contract.BoundScope tenant, CancellationToken stop)
    {
        private Contract.IIngress _ingress = null!;
        private readonly ReplyLog _replies = new();
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        private IScopeActivation _owner = null!;
        internal Probe Probe { get; } = new();
        internal EfStore Cold { get; private set; } = null!;
        internal PostgresRootAdmission Admission { get; private set; } = null!;
        private PostgresRootReads _reads = null!;
        private FoundationReadCursor _cursor = null!;
        internal Dictionary<Contract.ActorRef, string> Selections { get; } = [];
        private readonly Dictionary<Contract.ActorRef, EfStore.RootImage> _running = [];

        private EfStore Store() => new(open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };

        internal async Task InstallAsync(bool createTables)
        {
            if (createTables)
                await using (var created = open()) await created.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(stop);
            Cold = Store();
            await Cold.CreateAsync(1);
            Admission = new PostgresRootAdmission(open, TimeSpan.FromHours(1));
            await Admission.InstallAsync(stop);
            _cursor = await Admission.OpenJournalCursorAsync(stop);
            _reads = new PostgresRootReads(Cold, _cursor, 12);
            _ingress = await Cold.OpenSelectedRootIngressAsync(Probe.Type, TimeSpan.FromHours(1), (scope, _) => ReferenceEquals(scope, tenant), stop);
        }

        internal async Task<IScopeActivation> OwnAsync()
        {
            var store = Store();
            var scope = (await store.BindWorkerPartitionAsync(0, stop)).Scope;
            var adapter = new ActorAdapter(new EfStore.RootHeaders(scope), Probe);
            // One heartbeat covers the whole fixture: the default ten-second lease can lapse on a loaded machine.
            var host = new DurableHost([new Worker(name, 0, store, adapter,
                new WorkerOptions { Partitions = 1, Lease = FixtureLease, Transport = _replies })]);
            await host.HeartbeatAsync(FixtureLease, stop);
            return _owner = await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture owner could not take its partition.");
        }

        internal async Task<Contract.ActorRef> StartAsync(string id)
        {
            var root = (await _ingress.SendAsync(tenant, new Contract.Target.Start(new(Probe.Type, "{}")), id, Contract.Kinds.Start, cancellationToken: stop)).Actor!.Value;
            _names[root.Id] = id;
            _running[root] = await DriveAsync(root, image => image is { Revision: 1, Status: InstanceStatus.Running });
            Selections[root] = (await _reads.GetAsync(tenant, root, stop))!.Selection!;
            return root;
        }

        internal async Task<EfStore.RootImage> ImageAsync(Contract.ActorRef root) =>
            await Cold.ReadFirstRootAsync(tenant.Tenant, root.Id, stop) ?? throw new InvalidOperationException($"The root {root.Id} has no checkpoint.");

        internal async Task<bool> SendAsync(Contract.ActorRef root, string id, string kind, long? dueAt = null) =>
            await _ingress.SendAsync(tenant, new Contract.Target.Ref(root), id, kind, "{}", dueAt is null ? null : new(DueAt: dueAt), stop) is
                { Outcome: Contract.AdmissionOutcome.Accepted };

        internal async Task<bool> TerminateAsync(Contract.ActorRef root, string id, string? selection = null) =>
            await _ingress.SendAsync(tenant, new Contract.Target.Ref(root), id, Contract.Kinds.Terminate,
                JsonSerializer.Serialize(new Contract.OperatorCommand(selection ?? Selections[root])), cancellationToken: stop) is
                { Outcome: Contract.AdmissionOutcome.Accepted };

        // Admission without the ingress: Suspend and Resume, held commands and Terminate and delete.
        internal async Task<bool> OperateAsync(Contract.ActorRef root, string id, string kind, string? selection = null, bool reply = false,
            bool thenDelete = false)
        {
            var deadline = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 600_000;
            return await Admission.AdmitRootOperatorAsync(tenant.Tenant, root.Id, id, kind, null, null, stop, selection, thenDelete,
                reply: reply ? new(ReplyTo.Of(HeldCaller, id, deadline), deadline) : null) is { Duplicate: false };
        }

        internal async Task<MessageEntity> StoredOperatorAsync(Contract.ActorRef root)
        {
            await using var read = open();
            return await read.Messages.AsNoTracking().SingleAsync(row => row.InboxScope == -4 && row.Id.StartsWith("operator/" + root.Id + "/"), stop);
        }

        internal string SealPark(Contract.ActorRef root, long revision, long at) => PostgresRootSelection.SealPark(_cursor, tenant.Tenant, root.Id, revision, at);

        // The parked root's view seals the selection of its Park: the opening revision and the Park time.
        internal async Task<string> ParkSelectionAsync(Contract.ActorRef root, long revision)
        {
            var at = await ParkAtAsync(await ImageAsync(root));
            var selection = (await _reads.GetAsync(tenant, root, stop))?.Selection;
            Check(at is { } parkAt && PostgresRootSelection.OpenPark(_cursor, selection, tenant.Tenant, root.Id) == new FoundationParkMark(revision, parkAt),
                $"the view of the parked root {_names[root.Id]} seals its Park selection at revision {revision}");
            return selection!;
        }

        // The Terminate ended the parked root at the latest revision: Cancelled, not suspended, its state kept, its terminal
        // mark written and its Park mark cleared, journalled with the rows it cleared; the view now seals the terminal mark.
        internal async Task TerminatedAsync(Contract.ActorRef root, string operation, int cleared, string? state = null)
        {
            var image = await ImageAsync(root);
            var journal = await Cold.ReadJournalOfAsync(0, image.Actor.Id, 0, stop);
            var listed = (await Cold.ReadRootActorAsync(tenant.Tenant, root.Id, stop))!.Value.Item;
            var selection = (await _reads.GetAsync(tenant, root, stop))?.Selection;
            Check(journal is [.., { Entries: var entry } last] && last.Revision == image.Revision &&
                    entry == Json.Write(new { Outcome = "terminated", Operation = operation, Cleared = cleared }) &&
                    image is { Status: InstanceStatus.Cancelled, Suspended: false } && image.TerminalRevision == image.Revision &&
                    image.State == (state ?? _running[root].State) && await ParkAtAsync(image) is null &&
                    listed is { Status: InstanceStatus.Cancelled, ParkAt: null, ParkFault: null } &&
                    PostgresRootSelection.OpenTerminal(_cursor, selection, tenant.Tenant, root.Id)?.Revision == image.Revision,
                $"{operation} ends the parked root Cancelled with its terminal mark, clears {cleared} kept rows and its Park mark");
        }

        // Drives the owner until every input of the root has settled.
        internal async Task SettledInputsAsync(Contract.ActorRef root)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!(await InputsAsync(root)).All(row => row.Done))
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(30)) throw new InvalidOperationException($"{name}: inputs of {_names[root.Id]} did not settle.");
                await _owner.WokenAsync(null, stop);
                await _owner.RunAsync(stop);
                await Task.Delay(10, stop);
            }
        }

        // The command settled at the latest revision with a journalled refusal, and the parked checkpoint and its Park stayed put.
        internal async Task SettledAsync(Contract.ActorRef root, string operation, string? state = null, bool suspended = false,
            string reason = "parked")
        {
            var image = await ImageAsync(root);
            var journal = await Cold.ReadJournalOfAsync(0, image.Actor.Id, 0, stop);
            var listed = (await Cold.ReadRootActorAsync(tenant.Tenant, root.Id, stop))!.Value.Item;
            using var entry = journal is [.., { Entries: var text }] ? JsonDocument.Parse(text) : null;
            Check(journal.Any(row => row.Entries == """["parked"]""") && journal[^1].Revision == image.Revision && entry?.RootElement is
                    { ValueKind: JsonValueKind.Object } refusal && refusal.GetProperty("Outcome").GetString() == "refused" &&
                    refusal.GetProperty("Operation").GetString() == operation && refusal.GetProperty("Reason").GetString() == reason &&
                    image is { Status: InstanceStatus.Faulted, TerminalRevision: null } && image.Suspended == suspended &&
                    image.State == (state ?? _running[root].State) && listed is { Status: InstanceStatus.Faulted, ParkFault: Fault, ParkAt: > 0 },
                $"{operation} is refused on the parked root at the next revision as {reason}, and the Park stays" +
                    (suspended ? " suspended" : ""));
        }

        internal async Task<InputRow[]> InputsAsync(Contract.ActorRef root)
        {
            await using var read = open();
            var actor = (await ImageAsync(root)).Actor.Id;
            return await read.Database.SqlQuery<InputRow>($"""
                SELECT "Seq", "Id", "Kind", "Done", "FoundationKeptBy" AS "KeptBy", "Payload" IS NOT NULL OR "Start" IS NOT NULL AS "Content" FROM "Messages"
                WHERE "Target" = {actor} AND "InboxScope" = -4 ORDER BY "Seq"
                """).ToArrayAsync(stop);
        }

        internal async Task<long?> ParkAtAsync(EfStore.RootImage root)
        {
            await using var read = open();
            return await read.Database.SqlQuery<long?>($"""SELECT "FoundationParkAt" AS "Value" FROM "Instances" WHERE "Id" = {root.Actor.Id}""").SingleAsync(stop);
        }

        // Passes run the actor turns off the caller, so the wait is bounded by time, not by a count of passes.
        internal async Task<EfStore.RootImage> DriveAsync(Contract.ActorRef root, Func<EfStore.RootImage?, bool> done,
            [CallerArgumentExpression(nameof(done))] string? expected = null)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(30))
            {
                if (await Cold.ReadFirstRootAsync(tenant.Tenant, root.Id, stop) is { } image && done(image)) return image;
                await _owner.WokenAsync(null, stop);
                await _owner.RunAsync(stop);
                await Task.Delay(10, stop);
            }
            var reached = await Cold.ReadFirstRootAsync(tenant.Tenant, root.Id, stop);
            throw new InvalidOperationException($"{name}: the root {_names.GetValueOrDefault(root.Id, root.Id)} did not reach {expected}; " +
                $"it is at revision {reached?.Revision}, {reached?.Status}{(reached?.Suspended == true ? ", suspended" : "")}.");
        }

        // The owner answered the held caller of this request once, with this result or refusal.
        internal void Answered(string request, string result) =>
            Check(_replies.Sent.Where(sent => sent.Reply.Request == request && sent.Node == HeldCaller).ToArray() is [var only] &&
                    (only.Reply.Item is CreationResult { Result: var refusal } ? refusal == result : Equals(only.Reply.Item, result)),
                $"the owner answers the held caller of {request} once, with {result}");

        // A committed checkpoint write with the checkpoint trigger disabled, for one row of the fixture.
        internal async Task BypassAsync(EfStore.RootImage root, string change)
        {
            await using var db = open();
            await using var transaction = await db.Database.BeginTransactionAsync(stop);
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" DISABLE TRIGGER kernel_require_root_checkpoint_writer""", stop);
            if (await db.Database.ExecuteSqlInterpolatedAsync(FormattableStringFactory.Create("UPDATE \"Instances\" SET " + change + " WHERE \"Id\" = {0}", root.Actor.Id), stop) != 1)
                throw new InvalidOperationException("The bypass did not change exactly the root row.");
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" ENABLE TRIGGER kernel_require_root_checkpoint_writer""", stop);
            await transaction.CommitAsync(stop);
        }

        // One raw journal entry at the next revision, then one write, in a transaction that is always rolled back after its
        // deferred checks run. True when all pass the triggers; false when one of them refuses.
        internal async Task<bool> TriggerProbeAsync(EfStore.RootImage root, string? entries, FormattableString? write)
        {
            await using var db = open();
            await using var transaction = await db.Database.BeginTransactionAsync(stop);
            try
            {
                if (entries is not null) await InsertEntryAsync(db, root, entries);
                if (write is not null && await db.Database.ExecuteSqlInterpolatedAsync(write, stop) != 1)
                    throw new InvalidOperationException("The trigger probe did not write exactly the root row.");
                await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", stop);
                return true;
            }
            catch (Exception error) when (IsTriggerRefusal(error))
            {
                return false;
            }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        // One raw journal entry at the next revision with no checkpoint write, committed. True when the commit lands.
        internal async Task<bool> LoneEntryCommitsAsync(EfStore.RootImage root, string entries)
        {
            await using var db = open();
            await using var transaction = await db.Database.BeginTransactionAsync(stop);
            try
            {
                await InsertEntryAsync(db, root, entries);
                await transaction.CommitAsync(stop);
            }
            catch (Exception error) when (IsTriggerRefusal(error))
            {
                return false;
            }
            // A landed probe entry would wedge the root at that revision; remove it so the fixture stays usable.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "Journal" WHERE "Instance" = {root.Actor.Id} AND "Revision" = {root.Revision + 1}
                """, stop);
            return true;
        }

        private Task InsertEntryAsync(KernelDb db, EfStore.RootImage root, string entries) =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Journal" ("Partition", "Position", "Instance", "Revision", "Entries")
                SELECT 0, coalesce(max("Position"), 0) + 1, {root.Actor.Id}, {root.Revision + 1}, {entries} FROM "Journal" WHERE "Partition" = 0
                """, stop);

        private static bool IsTriggerRefusal(Exception error) =>
            (error as PostgresException ?? error.InnerException as PostgresException) is { SqlState: "23514" } refused &&
            TriggerMessages.Contains(refused.MessageText);
    }

    private sealed class Probe : Contract.IActorType
    {
        internal const string Type = "postgres-parked-operators";
        private readonly List<(string Actor, string Kind)> _seen = [];
        public HashSet<(string Actor, string Kind)> FailsOn { get; } = [];
        public bool Saw(Contract.ActorRef actor, string kind) => _seen.Contains((actor.Id, kind));
        string Contract.IActorType.Type => Type;
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            _seen.AddRange(turn.Inbox.Select(input => (turn.Header.Ref.Id, input.Kind)));
            if (turn.Inbox.Any(input => input.Kind == "fail" || FailsOn.Contains((turn.Header.Ref.Id, input.Kind))))
            {
                turn.State = "null";
                throw new InvalidOperationException(Fault);
            }
            turn.State = "{\"running\":true}";
            if (turn.Inbox.Any(input => input.Kind == Contract.Kinds.Terminate)) turn.End(Contract.ActorStatus.Cancelled);
            return default;
        }
    }

    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        Console.WriteLine("ok " + label);
    }
}
