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
// carries Park arrivals with terminal selection and held replies; a third carries Park inspection with jobs. A Terminate is
// admitted with its receipt pinned, polled past withheld inputs, kept in its FIFO place while the Park opens, delivered and
// refused with a journalled reason that S5b replaces. A Park keeps the root's Suspended flag, as the in-memory reference does.
// A held command admitted before the Park opened is refused there and its caller answered "Parked". Admission refuses
// held commands on a parked root, and every set with Park recovery refuses Suspend. Shipped formats carry none of these paths.
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

    // The checkpoint, journal and refusal-outcome triggers. A probe that fails any other way is the probe's own error.
    private static readonly string[] TriggerMessages =
    [
        "Qualified actor checkpoint requires a qualified update",
        "Qualified actor journal requires the next running checkpoint revision",
        "An operator refusal entry requires the checkpoint write at its revision"
    ];

    private sealed record InputRow(long Seq, string Id, string Kind, bool Done, short? KeptBy);

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

        // A Terminate admitted after the Park passes a withheld ordinary input and changes nothing else.
        var due = (await main.ImageAsync(late)).AsOf + 1_500;
        Check(await main.SendAsync(late, "withheld", "ordinary", due) && await main.SendAsync(late, "fail-late", "fail"),
            "a future input and a failing one are admitted");
        var before = await main.DriveAsync(late, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
        while ((await main.ImageAsync(late)).AsOf <= due) await Task.Delay(100, stop);
        await ReceiptsPinned();
        Check(await main.TerminateAsync(late, "terminate-late"), "a Terminate with the root's running selection is admitted for the parked root");
        Check(!await main.TerminateAsync(early, "terminate-other", main.Selections[late]), "a Terminate with another root's selection is refused at admission");
        var inbox = (await main.Cold.ReadRootInboxAsync(tenant.Tenant, late.Id, 0, "", 10, stop)).Rows;
        Check(inbox is [{ Id: "withheld", KeptBy: null }, { Id: "fail-late", KeptBy: ParkKeepReason.Consumed }, { Id: "terminate-late", KeptBy: null }],
            "inspection lists the withheld input, the kept fault input and the pending Terminate in FIFO order");
        var after = await main.DriveAsync(late, image => image is { Revision: 3 });
        await main.SettledAsync(late, "terminate-late");
        Check(await main.InputsAsync(late) is [{ Id: var withheld, Done: false, KeptBy: null }, { Kind: "fail", KeptBy: (short)ParkKeepReason.Consumed }, { Done: true }] &&
            withheld.StartsWith(late.Id, StringComparison.Ordinal) && !probe.Saw(late, "ordinary") && after.Revision == before.Revision + 1,
            "the refusal moves only the revision: the withheld input stays pending and the kept input stays kept");

        // A Terminate whose own turn fails opens the Park without it; the owner polls it again and refuses it.
        Check(await main.TerminateAsync(lone, "terminate-lone"), "a Terminate is admitted for the root whose terminate turn fails");
        await main.DriveAsync(lone, image => image is { Revision: 3, Status: InstanceStatus.Faulted });
        await main.SettledAsync(lone, "terminate-lone");
        Check(await main.InputsAsync(lone) is [{ Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null }] && probe.Saw(lone, Contract.Kinds.Terminate),
            "the failed Terminate opened the Park outside the kept set and settled on the parked root");

        // A held command admitted while the root runs, behind the input that parks it, is refused on the parked root with a
        // journalled reason, and its caller is answered "Parked". The unheld Terminate behind it is refused next.
        Check(await main.SendAsync(held, "fail-held", "fail") &&
                await main.OperateAsync(held, "held-terminate", Contract.Kinds.Terminate, main.Selections[held], reply: true),
            "a failing input and a held Terminate behind it are admitted while the root runs");
        await main.DriveAsync(held, image => image is { Revision: >= 2, Status: InstanceStatus.Faulted });
        Check(await main.TerminateAsync(held, "after-held"), "an unheld Terminate is admitted behind the held one on the parked root");
        await main.DriveAsync(held, image => image is { Revision: 4 });
        var heldJournal = await main.Cold.ReadJournalOfAsync(0, (await main.ImageAsync(held)).Actor.Id, 0, stop);
        Check(heldJournal is [.., { Entries: """["parked"]""" } park, var heldRefusal, var afterRefusal] && park.Revision == 2 &&
                heldRefusal is { Revision: 3, Entries: """{"Outcome":"refused","Operation":"held-terminate","Reason":"parked"}""" } &&
                afterRefusal is { Revision: 4, Entries: """{"Outcome":"refused","Operation":"after-held","Reason":"parked"}""" } &&
                await main.InputsAsync(held) is [{ Kind: "fail", Done: true, KeptBy: (short)ParkKeepReason.Consumed },
                    { Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null }, { Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null }] &&
                !probe.Saw(held, Contract.Kinds.Terminate),
            "the owner refuses the held Terminate on the parked root, then the unheld Terminate behind it, each at its own revision");
        main.Answered("held-terminate");

        // Admission on a parked root: only an unheld Terminate, or Terminate and delete, is admitted.
        Check(!await main.OperateAsync(late, "suspend-parked", Contract.Kinds.Suspend) &&
                !await main.OperateAsync(late, "resume-parked", Contract.Kinds.Resume),
            "admission refuses a Suspend and a Resume for a parked root");
        Check(!await main.OperateAsync(late, "held-parked", Contract.Kinds.Terminate, main.Selections[late], reply: true) &&
                !await main.OperateAsync(late, "held-then-delete-parked", Contract.Kinds.Terminate, main.Selections[late], reply: true, thenDelete: true),
            "admission refuses a held Terminate and a held Terminate and delete for a parked root");

        // Two commands for one parked root are delivered one at a time in Seq order, each refused at its own revision.
        Check(await main.TerminateAsync(lone, "pair-first") && await main.TerminateAsync(lone, "pair-second") &&
                await main.OperateAsync(lone, "pair-then-delete", Contract.Kinds.Terminate, main.Selections[lone], thenDelete: true),
            "two Terminates and an unheld Terminate and delete are admitted for the parked root");
        var pair = await main.DriveAsync(lone, image => image is { Revision: 6 });
        var pairJournal = await main.Cold.ReadJournalOfAsync(0, pair.Actor.Id, 0, stop);
        Check(pairJournal is [.., var first, var second, var third] &&
                first is { Revision: 4, Entries: """{"Outcome":"refused","Operation":"pair-first","Reason":"parked"}""" } &&
                second is { Revision: 5, Entries: """{"Outcome":"refused","Operation":"pair-second","Reason":"parked"}""" } &&
                third is { Revision: 6, Entries: """{"Outcome":"refused","Operation":"pair-then-delete","Reason":"parked"}""" } &&
                await main.InputsAsync(lone) is { Length: 4 } pairInputs && pairInputs.All(row => row.Done) &&
                pairInputs.Select(row => row.Seq).SequenceEqual(pairInputs.Select(row => row.Seq).Order()),
            "the owner delivers the parked root's commands in Seq order and refuses each at the next revision");
        await main.SettledAsync(lone, "pair-then-delete");

        // A Terminate whose turn fails on a suspended root opens a Park that keeps Suspended. The owner refuses it there.
        Check(await main.OperateAsync(suspendedTerminate, "suspend-first", Contract.Kinds.Suspend), "a Suspend is admitted for a running root");
        var suspended = await main.DriveAsync(suspendedTerminate, image => image is { Revision: 2, Status: InstanceStatus.Running, Suspended: true });
        Check(await main.TerminateAsync(suspendedTerminate, "terminate-suspended"), "a Terminate is admitted for the suspended root");
        await main.DriveAsync(suspendedTerminate, image => image is { Revision: 4, Status: InstanceStatus.Faulted });
        await main.SettledAsync(suspendedTerminate, "terminate-suspended", suspended.State, suspended: true);
        Check(await main.InputsAsync(suspendedTerminate) is [{ Kind: Contract.Kinds.Suspend, Done: true }, { Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null }] &&
                probe.Saw(suspendedTerminate, Contract.Kinds.Terminate),
            "the failed Terminate on the suspended root parked it suspended, outside the kept set, and settled there");

        // A Resume whose turn fails parks the root suspended. The owner refuses it, and a later Terminate, on the suspended Park.
        Check(await main.OperateAsync(suspendedResume, "suspend-second", Contract.Kinds.Suspend), "a Suspend is admitted for the second root");
        suspended = await main.DriveAsync(suspendedResume, image => image is { Revision: 2, Suspended: true });
        Check(await main.OperateAsync(suspendedResume, "resume-fails", Contract.Kinds.Resume), "a Resume is admitted for the suspended root");
        await main.DriveAsync(suspendedResume, image => image is { Revision: 4, Status: InstanceStatus.Faulted });
        await main.SettledAsync(suspendedResume, "resume-fails", suspended.State, suspended: true);
        Check(!await main.OperateAsync(suspendedResume, "resume-parked", Contract.Kinds.Resume) &&
                await main.TerminateAsync(suspendedResume, "terminate-suspended-park"),
            "admission refuses a Resume and admits a Terminate for the root parked while suspended");
        await main.DriveAsync(suspendedResume, image => image is { Revision: 5 });
        await main.SettledAsync(suspendedResume, "terminate-suspended-park", suspended.State, suspended: true);

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
                if (shipped(options).UsesRootParkedOperators) throw new InvalidOperationException("A shipped profile refuses parked operators no more.");
            Check(true, "the shipped Park and operator profiles do not use the parked-operator paths");
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
            foreach (var (column, change) in RowChanges(wire: true, parkRevision: false))
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
        }

        // The parked-root admission predicates, each broken alone through a bypassed checkpoint write and then restored.
        async Task ParkedRootPredicates()
        {
            var image = await main.ImageAsync(late);
            var parkAt = await main.ParkAtAsync(image);
            foreach (var (predicate, broken, restored) in new (string, string, string)[]
            {
                ("lacks a Park mark", """ "FoundationParkAt" = NULL """, $""" "FoundationParkAt" = {parkAt} """),
                ("has a terminal revision", $""" "FoundationTerminalRevision" = {image.Revision} """, """ "FoundationTerminalRevision" = NULL """)
            })
            {
                await main.BypassAsync(image, broken);
                bool admitted;
                try { admitted = await main.TerminateAsync(late, "predicate-" + predicate.Length); }
                finally { await main.BypassAsync(image, restored); }
                Check(!admitted, $"admission refuses a Terminate for a Faulted root that {predicate}");
            }
            Check(await main.TerminateAsync(late, "predicate-control"), "the restored parked root admits a Terminate again");
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
            set.Probe.FailsOn.Add((asleep.Id, Contract.Kinds.Resume));

            Check(await set.SendAsync(plain, "arrivals-fail", "fail"), "the arrivals set admits a failing input");
            await set.DriveAsync(plain, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(await set.TerminateAsync(plain, "arrivals-terminate"), "the arrivals set admits a Terminate for the parked root");
            await set.DriveAsync(plain, image => image is { Revision: 3 });
            await set.SettledAsync(plain, "arrivals-terminate");
            Check(await set.SendAsync(plain, "arrivals-after", "ordinary"), "the arrivals set admits an arrival for the parked root");
            var arrived = await set.DriveAsync(plain, image => image is { Revision: 4 });
            Check(await set.InputsAsync(plain) is [{ Kind: "fail", KeptBy: (short)ParkKeepReason.Consumed }, { Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null },
                    { Kind: "ordinary", Done: true, KeptBy: (short)ParkKeepReason.Arrived }] && arrived is { Status: InstanceStatus.Faulted, Suspended: false },
                "the refusal leaves the Park to keep the next arrival");

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
            Check(await set.TerminateAsync(asleep, "arrivals-terminate-asleep"), "the arrivals set admits a Terminate for the root parked while suspended");
            await set.DriveAsync(asleep, image => image is { Revision: 6 });
            await set.SettledAsync(asleep, "arrivals-terminate-asleep", sleeping.State, suspended: true);

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
            set.Answered("arrivals-held-terminate");

            var parked = await set.ImageAsync(plain);
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
            _reads = new PostgresRootReads(Cold, await Admission.OpenJournalCursorAsync(stop), 12);
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

        // The command settled at the latest revision with a journalled refusal, and the parked checkpoint and its Park stayed put.
        internal async Task SettledAsync(Contract.ActorRef root, string operation, string? state = null, bool suspended = false)
        {
            var image = await ImageAsync(root);
            var journal = await Cold.ReadJournalOfAsync(0, image.Actor.Id, 0, stop);
            var listed = (await Cold.ReadRootActorAsync(tenant.Tenant, root.Id, stop))!.Value.Item;
            using var entry = journal is [.., { Entries: var text }] ? JsonDocument.Parse(text) : null;
            Check(journal.Any(row => row.Entries == """["parked"]""") && journal[^1].Revision == image.Revision && entry?.RootElement is
                    { ValueKind: JsonValueKind.Object } refusal && refusal.GetProperty("Outcome").GetString() == "refused" &&
                    refusal.GetProperty("Operation").GetString() == operation && refusal.GetProperty("Reason").GetString() == "parked" &&
                    image is { Status: InstanceStatus.Faulted, TerminalRevision: null } && image.Suspended == suspended &&
                    image.State == (state ?? _running[root].State) && listed is { Status: InstanceStatus.Faulted, ParkFault: Fault, ParkAt: > 0 },
                $"{operation} is refused on the parked root at the next revision with a journalled reason, and the Park stays" +
                    (suspended ? " suspended" : ""));
        }

        internal async Task<InputRow[]> InputsAsync(Contract.ActorRef root)
        {
            await using var read = open();
            var actor = (await ImageAsync(root)).Actor.Id;
            return await read.Database.SqlQuery<InputRow>($"""
                SELECT "Seq", "Id", "Kind", "Done", "FoundationKeptBy" AS "KeptBy" FROM "Messages"
                WHERE "Target" = {actor} AND "InboxScope" = -4 ORDER BY "Seq"
                """).ToArrayAsync(stop);
        }

        internal async Task<long> ParkAtAsync(EfStore.RootImage root)
        {
            await using var read = open();
            return await read.Database.SqlQuery<long>($"""SELECT "FoundationParkAt" AS "Value" FROM "Instances" WHERE "Id" = {root.Actor.Id}""").SingleAsync(stop);
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

        // The owner answered the held caller of this request with the "Parked" refusal, once.
        internal void Answered(string request) =>
            Check(_replies.Sent.Count(sent => sent.Reply is { Item: CreationResult { Result: "Parked" } } && sent.Reply.Request == request &&
                    sent.Node == HeldCaller) == 1,
                $"the owner answers the held caller of {request} with the Parked refusal");

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
