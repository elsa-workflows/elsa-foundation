using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
// "terminated". A stale Park is refused as "selection" and a larger one as "page". Of the shipped formats only RootShipped
// (format 25) takes these paths, the S5b-3b Delete of a parked root among them; the qualification profiles carry none.
internal static class PostgresRootParkedOperatorChecks
{
    private static readonly StoreFormat Composed = StoreFormat.Current with { MessageProtocol = 95 };
    private static readonly StoreFormat ComposedJobs = StoreFormat.Current with { MessageProtocol = 94 };
    private static readonly StoreFormat ComposedArrivals = StoreFormat.Current with { MessageProtocol = 93 };
    private static readonly StoreFormat ComposedRecovery = StoreFormat.Current with { MessageProtocol = 92 };
    private const string JobsSchema = "parked_jobs";
    private const string ArrivalsSchema = "parked_arrivals";
    private const string Format22Schema = "parked_format22";
    private const string RecoverySchema = "parked_recovery";
    private const string ClearingSchema = "parked_clearing";
    private static readonly StoreFormat ComposedClearing = StoreFormat.Current with { MessageProtocol = 90 };
    private const string ThenDeleteSchema = "parked_then_delete";
    private static readonly StoreFormat ComposedThenDelete = StoreFormat.Current with { MessageProtocol = 89 };
    private const string DeleteSchema = "parked_delete";
    private static readonly StoreFormat ComposedDelete = StoreFormat.Current with { MessageProtocol = 88 };
    private const string ProbeDiscarded = """{"Outcome":"protected","Disposition":"discarded","Operation":"probe","Cleared":0}""";
    private const string ProbeProtected = """{"Outcome":"protected","Disposition":"terminated","Operation":"probe","Cleared":0}""";
    private const string Fault = "deliberate actor failure";
    private const string ProbeRefusal = """{"Outcome":"refused","Operation":"probe","Reason":"parked"}""";
    private const string ProbeTerminated = """{"Outcome":"terminated","Operation":"probe","Cleared":0}""";
    private const string ProbeClearing = """{"Outcome":"clearing","Operation":"probe","Cleared":1}""";

    // The operator outcome entries of a root's journal, in order.
    private static string[] Outcomes(IEnumerable<JournalRow> journal) =>
        [.. journal.Select(row => row.Entries).Where(entry => entry.StartsWith("{\"Outcome\":", StringComparison.Ordinal))];

    // The checkpoint, journal and refusal-outcome triggers. A probe that fails any other way is the probe's own error.
    private static readonly string[] TriggerMessages =
    [
        "Qualified actor checkpoint requires a qualified update",
        "Qualified actor journal requires the next running checkpoint revision",
        "An operator outcome entry requires the checkpoint write at its revision"
    ];

    private sealed record InputRow(long Seq, string Id, string Kind, bool Done, short? KeptBy, bool Content);
    private sealed record ControlRow(long Due, long KeepUntil, string Start);

    internal static async Task<int> RunAsync(bool owned, bool inBatch, bool shippedSet = false)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var capabilities = shippedSet ? RootProfiles.Shipped
            : RootProfiles.Formats[StoreFormat.RootOperatorReplies] | RootProfiles.Formats[StoreFormat.RootParkInspection];
        KernelDb Open() => KernelDb.ForUnshippedComposition(options, Composed, capabilities);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(240));
        var stop = timeout.Token;
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the parked-operator fixture uses an empty PostgreSQL database");
        // The ingress turns the owner's refusal into the caller's error; a held caller of a parked root reads "parked".
        Check(PostgresRootIngress.RefusalError("Parked") == "parked" && PostgresRootIngress.RefusalError("Protected") == "protected" &&
                PostgresRootIngress.RefusalError("ParkOutstanding") == "park outstanding" &&
                PostgresRootIngress.RefusalError("ResolutionOpen") == "resolution open" &&
                PostgresRootIngress.RefusalError("ProtectionUnavailable") == "protection unavailable" &&
                PostgresRootIngress.RefusalError("StaleSelection") == "stale selection" &&
                PostgresRootIngress.RefusalError("TargetMismatch") == "target" && PostgresRootIngress.RefusalError("Ended") == "missing",
            "the ingress reports a held command refused on a parked root as \"parked\", \"park outstanding\" or \"resolution open\"");
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
        var planned = await main.StartAsync("planned");
        var deleted = await main.StartAsync("deleted");
        var withheldDelete = await main.StartAsync("withheld-delete");
        var heldRunning = await main.StartAsync("held-running");
        var heldThenDelete = await main.StartAsync("held-then-delete");
        var heldDelete = await main.StartAsync("held-delete");
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
                !await main.OperateAsync(early, "delete-other", Contract.Kinds.Delete, parkLate) &&
                !await main.OperateAsync(late, "delete-stale", Contract.Kinds.Delete, main.SealPark(late, before.Revision + 1, (await main.ParkAtAsync(before))!.Value)),
            "admission refuses a parked root's running selection, another root's Park and a stale Park, for a Terminate and a Delete");
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
        // A Park-terminated root is deleted through the ordinary retirement, whose residue checks admit its cleared rows.
        Check(await main.OperateAsync(late, "delete-late", Contract.Kinds.Delete, await main.SelectionAsync(late)),
            "a Delete with the terminal selection is admitted for the Park-terminated root");
        await main.RetiredAsync(late);

        // A row with jobs, children or custom handles is refused as "outstanding": the end cannot cancel or cascade them yet, and
        // retirement refuses them. No closed composed set reaches one through the owner, so a bypassed write plants children.
        Check(await main.SendAsync(planned, "fail-planned", "fail"), "a failing input is admitted for the planned root");
        var plannedPark = await main.DriveAsync(planned, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
        var parkPlanned = await main.ParkSelectionAsync(planned, 2);
        await main.BypassAsync(plannedPark, """ "Children" = 'fixture' """);
        try
        {
            Check(await main.OperateAsync(planned, "terminate-planned", Contract.Kinds.Terminate, parkPlanned, reply: true),
                "a held Terminate with the Park selection is admitted for the root with children");
            await main.DriveAsync(planned, image => image is { Revision: 3 });
        }
        finally { await main.BypassAsync(plannedPark, """ "Children" = NULL """); }
        await main.SettledAsync(planned, "terminate-planned", reason: "outstanding");
        main.Answered("terminate-planned", "ParkOutstanding");
        // A subscription counts as outstanding too, so the Terminate is refused instead of failing its commit.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Subscriptions" ("Key", "Instance", "Wait", "Partition") VALUES ('planned', {plannedPark.Actor.Id}, 'planned', 0)
            """, stop);
        try
        {
            Check(await main.TerminateAsync(planned, "terminate-subscribed", parkPlanned), "a Terminate is admitted for the root with a subscription");
            await main.DriveAsync(planned, image => image is { Revision: 4 });
        }
        finally { await db.Database.ExecuteSqlRawAsync("""DELETE FROM "Subscriptions" WHERE "Key" = 'planned'""", stop); }
        await main.SettledAsync(planned, "terminate-subscribed", reason: "outstanding");
        Check(await main.TerminateAsync(planned, "terminate-unplanned", parkPlanned), "the root without children admits its Park selection again");
        await main.DriveAsync(planned, image => image is { Revision: 5, Status: InstanceStatus.Cancelled });
        await main.TerminatedAsync(planned, "terminate-unplanned", cleared: 1);

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

        // S5b-3b: a plain Delete with the Park selection retires the parked root, still Faulted, in one commit where the parked
        // root withholds its ordinary inputs, and answers its held caller "deleted" once.
        Check(await main.SendAsync(deleted, "fail-deleted", "fail"), "a failing input is admitted for the root to delete");
        var deletedPark = await main.DriveAsync(deleted, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
        Check(await main.OperateAsync(deleted, "delete-parked", Contract.Kinds.Delete, await main.ParkSelectionAsync(deleted, 2), reply: true),
            "a held Delete with the Park selection is admitted for the parked root");
        await main.RetiredAsync(deleted);
        Check((await main.Cold.ReadJournalOfAsync(0, deletedPark.Actor.Id, 0, stop)) is [{ Revision: -1, Entries: """["delete"]""" }] &&
                (await main.InputsOfAsync(deletedPark.Actor.Id)).All(row => row.Done && row.KeptBy is null && !row.Content) &&
                !probe.Saw(deleted, Contract.Kinds.Delete),
            "the Delete retired the parked root with the deletion marker as its only journal row and its kept input cleared");
        main.Answered("delete-parked", "deleted");

        // Without Park arrivals the parked root withholds its inputs: one read ahead in the failing input's poll, due just
        // after it, stays in the owner's slot. The Delete's retirement settles it, and the owner forgets it with the retired root.
        var behindDue = (await main.ImageAsync(withheldDelete)).AsOf + 1_500;
        Check(await main.SendAsync(withheldDelete, "withheld-behind", "ordinary", behindDue) && await main.SendAsync(withheldDelete, "fail-withheld-delete", "fail"),
            "an input due shortly and a failing one are admitted together");
        var withheldPark = await main.DriveAsync(withheldDelete, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
        while ((await main.ImageAsync(withheldDelete)).AsOf <= behindDue) await Task.Delay(100, stop);
        var worker = (Worker)main.Owner;
        Check(worker.InboxCountOf(withheldPark.Actor.Id) == 1, "the input polled with the failing one stays withheld in the owner's slot");
        Check(await main.OperateAsync(withheldDelete, "delete-withheld", Contract.Kinds.Delete, await main.ParkSelectionAsync(withheldDelete, 2)),
            "a Delete with the Park selection is admitted for the root with a withheld input");
        await main.RetiredAsync(withheldDelete);
        Check(worker.InboxCountOf(withheldPark.Actor.Id) == 0 && ReferenceEquals(main.Owner, worker) && !main.StoodDown &&
                (await main.InputsOfAsync(withheldPark.Actor.Id)).All(row => row.Done && !row.Content) && !probe.Saw(withheldDelete, "ordinary"),
            "the retirement settled the withheld input and no row stays in the retired root's slot");

        // #5756: retirement answers each held direct input still pending on the root once, with "missing", as the in-memory
        // reference answers an input that reaches a retired row; an unheld input settles with no answer. In a set without
        // direct held replies a held input is refused at admission and nothing else changes. The inputs are due shortly
        // after the command that retires the root, so they are still pending when it runs, or withheld by the Park.
        var heldReplies = db.UsesRootReplyProtocol;
        // A running root retired by its Terminate-then-delete.
        var runningDue = (await main.ImageAsync(heldRunning)).AsOf + 1_500;
        var runningRequest = await main.HeldSendAsync(heldRunning, "held-running-input", runningDue);
        Check(runningRequest is not null == heldReplies &&
                await main.SendAsync(heldRunning, "unheld-running-input", "ordinary", runningDue) &&
                await main.OperateAsync(heldRunning, "held-running-delete", Contract.Kinds.Terminate, main.Selections[heldRunning], thenDelete: true),
            heldReplies ? "a held and an unheld input due shortly, and a Terminate-then-delete, are admitted for the running root"
                : "without held replies a held input is refused at admission; an unheld input and a Terminate-then-delete are admitted");
        var runningActor = (await main.ImageAsync(heldRunning)).Actor.Id;
        await main.RetiredAsync(heldRunning);
        await HeldInputsAnsweredAsync(runningActor, "held-running-input", runningRequest, "unheld-running-input");
        // A parked root retired by its Terminate-then-delete, and one retired by a Delete, both with the Park selection.
        foreach (var (root, label, thenDelete) in new[] { (heldThenDelete, "held-then-delete", true), (heldDelete, "held-delete", false) })
        {
            var heldDue = (await main.ImageAsync(root)).AsOf + 1_500;
            var heldRequest = await main.HeldSendAsync(root, label + "-input", heldDue);
            Check(heldRequest is not null == heldReplies && await main.SendAsync(root, "un" + label + "-input", "ordinary", heldDue) &&
                    await main.SendAsync(root, label + "-fail", "fail"),
                $"held and unheld inputs due shortly, and a failing one, are admitted for the {label} root");
            var retiredPark = await main.DriveAsync(root, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            while ((await main.ImageAsync(root)).AsOf <= heldDue) await Task.Delay(100, stop);
            Check(await main.OperateAsync(root, label, thenDelete ? Contract.Kinds.Terminate : Contract.Kinds.Delete,
                    await main.ParkSelectionAsync(root, 2), thenDelete: thenDelete),
                $"a {(thenDelete ? "Terminate-then-delete" : "Delete")} with the Park selection is admitted for the {label} root");
            await main.RetiredAsync(root);
            await HeldInputsAnsweredAsync(retiredPark.Actor.Id, label + "-input", heldRequest, "un" + label + "-input");
        }

        // Admission on a parked root: a Suspend, a Resume, a held running selection and a held Terminate and delete are refused.
        var parkEarly = await main.ParkSelectionAsync(early, 2);
        Check(!await main.OperateAsync(early, "suspend-parked", Contract.Kinds.Suspend) &&
                !await main.OperateAsync(early, "resume-parked", Contract.Kinds.Resume) &&
                !await main.OperateAsync(early, "held-parked", Contract.Kinds.Terminate, main.Selections[early], reply: true),
            "admission refuses a Suspend, a Resume and a held running selection for a parked root");

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

        // The retired root's inputs all settled; a held one was answered "missing" once, and an unheld one not at all.
        async Task HeldInputsAnsweredAsync(string actor, string held, string? request, string unheld)
        {
            var inputs = await main.InputsOfAsync(actor);
            Check(inputs.All(row => row.Done && !row.Content) && !main.AnsweredAny(unheld) &&
                    inputs.Count(row => row.Id.Contains("/" + FoundationRootCreator.OperationDigest(unheld) + "/")) == 1 &&
                    (request is null || inputs.Count(row => row.Id.Contains("/" + FoundationRootCreator.OperationDigest(held) + "/")) == 1),
                $"the retirement settled the inputs of {actor}, and the unheld one got no answer");
            if (request is not null) await main.AnsweredErrorEventuallyAsync(request, "missing");
        }
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
        await RereadAcrossFormats();
        await ClearingSet();
        await ThenDeleteSet();
        await DeleteSet();
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
            // S7: of the shipped formats only format 25 carries Park with termination, and it takes the parked-operator paths.
            Check(RootProfiles.Formats.Where(entry => entry.Key != StoreFormat.RootShipped)
                    .All(entry => (entry.Value & RootCapabilities.ParkOpening) == 0 || (entry.Value & RootCapabilities.Termination) == 0) &&
                KernelDb.ForPostgresRootShipped(options).UsesRootParkedOperators,
                "only format 25 carries Park with termination, on the parked-operator paths, so formats 19-22 and 5, 11-17 keep theirs");
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
            foreach (var (column, planted, cleared) in new[]
            {
                ("jobs", """ "Jobs" = 'fixture' """, """ "Jobs" = NULL """),
                ("children", """ "Children" = 'fixture' """, """ "Children" = NULL """),
                ("custom handles", """ "CustomHandles" = 'fixture' """, """ "CustomHandles" = NULL """),
                ("no fault", """ "Fault" = NULL """, $""" "Fault" = '{DurableKeyJson.Write(Fault)}' """)
            })
            {
                await main.BypassAsync(suspendedPark, planted);
                bool ended;
                try { ended = await main.TriggerProbeAsync(suspendedPark, ProbeTerminated, End(suspendedPark)); }
                finally { await main.BypassAsync(suspendedPark, cleared); }
                Check(!ended, $"a terminated entry is refused for a parked root with {column}");
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Subscriptions" ("Key", "Instance", "Wait", "Partition") VALUES ('probe', {suspendedPark.Actor.Id}, 'probe', 0)
                """, stop);
            bool subscribed;
            try { subscribed = await main.TriggerProbeAsync(suspendedPark, ProbeTerminated, End(suspendedPark)); }
            finally { await db.Database.ExecuteSqlRawAsync("""DELETE FROM "Subscriptions" WHERE "Key" = 'probe'""", stop); }
            Check(!subscribed && await main.TriggerProbeAsync(suspendedPark, ProbeTerminated, End(suspendedPark)),
                "a terminated entry is refused for a parked root with a subscription, and passes again without it");
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

        // A keep ahead of a Recover read in the same poll. Shipped format 22 keeps its behaviour: the stale Recover fails its
        // commit, the owner stands down and the next owner recovers the root. In a composed set with recovery and parked
        // operators, a Consumed keep (an arrival at its attempt limit re-parks the root) marks the Recover unseen, so the same
        // owner reads it again and recovers the root. The hook only delays the poll's Park facts read until the input is due.
        // S5b-2: a Park larger than one page is cleared in pages under its Terminate. Open clears the first page and journals
        // "clearing"; each store control clears the next page; the page that leaves nothing kept ends the root. While the
        // clearing is open nothing is kept: arrivals stay pending in Seq order and settle as Ended after the end.
        async Task ClearingSet()
        {
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + ClearingSchema, stop);
            var clearingConnection = new NpgsqlConnectionStringBuilder(connection) { SearchPath = ClearingSchema }.ConnectionString;
            var clearingHold = new RaceHook(clearingConnection);
            var clearingOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(clearingConnection).AddInterceptors(clearingHold).Options;
            var clearingCapabilities = RootProfiles.Formats[StoreFormat.RootParkArrivals] | RootProfiles.Formats[StoreFormat.RootTerminalSelection] |
                RootCapabilities.OperatorReplies;
            KernelDb OpenClearing() => KernelDb.ForUnshippedComposition(clearingOptions, ComposedClearing, clearingCapabilities);
            EfStore? frozen = null;
            var pageChecked = false;
            var cleared = new List<long?>();
            var settled = new List<string>();
            var arrivals = new[] { "during-1", "during-2", "during-3" };
            // The commit observer reads the large root, so it is assigned before the observer is installed.
            Contract.ActorRef large = default;
            (string Actor, long Due)? holdAfterOpen = null;
            await using var set = new Set("parked-clearing", OpenClearing, owned, inBatch, tenant, stop);
            set.Write = Write;
            await set.InstallAsync(createTables: true);
            await set.OwnAsync();
            var bytes = await set.StartAsync("clearing-bytes");
            large = await set.StartAsync("clearing-large");
            var probed = await set.StartAsync("clearing-probe");
            var between = await set.StartAsync("clearing-between");
            var ahead = await set.StartAsync("clearing-ahead");

            // A Park within 170 rows that exceeds 4 MiB is cleared in pages by bytes.
            Check(await set.SendAsync(bytes, "bytes-fail", "fail"), "the clearing set admits a failing input");
            var bytesPark = await set.DriveAsync(bytes, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            foreach (var index in Enumerable.Range(0, 5)) Check(await set.SendAsync(bytes, $"bytes-{index}", "ordinary"), $"the clearing set admits arrival {index}");
            await set.SettledInputsAsync(bytes);
            await using (var write = OpenClearing())
                Check(await write.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "Payload" = '"' || repeat('x', 1200000) || '"'
                    WHERE "Target" = {bytesPark.Actor.Id} AND "InboxScope" = -4 AND "FoundationKeptBy" = 1
                    """, stop) == 5, "five kept arrivals of 1.2 MB each make the Park larger than 4 MiB in six rows");
            Check(await set.TerminateAsync(bytes, "bytes-terminate", await set.ParkSelectionAsync(bytes, 2)), "a Terminate is admitted for the Park sized by bytes");
            await set.DriveAsync(bytes, image => image is { Status: InstanceStatus.Cancelled });
            Check(Outcomes(await set.Cold.ReadJournalOfAsync(0, bytesPark.Actor.Id, 0, stop)) is
                    ["""{"Outcome":"clearing","Operation":"bytes-terminate","Cleared":4}""", """{"Outcome":"terminated","Operation":"bytes-terminate","Cleared":6}"""],
                "the Park larger than 4 MiB opens with a page of four rows and ends with the last two");
            await set.TerminatedAsync(bytes, "bytes-terminate", cleared: 6);

            var parkedProbe = await ParkProbeAsync();

            // 402 kept rows: one failing input, one arrival and 400 copies of it under their own operations.
            Check(await set.SendAsync(large, "large-fail", "fail"), "the clearing set admits a failing input for the large Park");
            var largePark = await set.DriveAsync(large, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(await set.SendAsync(large, "a1", "ordinary"), "the clearing set admits an arrival for the large Park");
            await set.SettledInputsAsync(large);
            var ops = Enumerable.Range(1, 400).Select(n => $"c{n:D3}").ToArray();
            var ids = ops.Select(op => $"{large.Id}/{FoundationRootCreator.OperationDigest(op)}/{Guid.NewGuid():N}").ToArray();
            await using (var write = OpenClearing())
                Check(await write.Database.ExecuteSqlInterpolatedAsync($$"""
                    INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Payload", "Due", "KeepUntil",
                        "Done", "Seen", "Attempts", "Trial", "JobGeneration", "FoundationKeptBy")
                    SELECT m."InboxScope", d.id, m."Partition", m."Target", m."Kind", replace(m."Start", '"a1"', '"' || d.op || '"'),
                        '{"n":' || d.n || '}', m."Due", m."KeepUntil", true, 0, 0, 0, 0, 1
                    FROM "Messages" m, unnest({{ids}}::text[], {{ops}}::text[]) WITH ORDINALITY d(id, op, n)
                    WHERE m."Target" = {{largePark.Actor.Id}} AND m."FoundationKeptBy" = 1 ORDER BY d.n
                    """, stop) == 400, "raw SQL keeps 400 more arrivals, each under its own operation");
            var parkLarge = await set.ParkSelectionAsync(large, 2);
            Check(await set.OperateAsync(large, "clear-large", Contract.Kinds.Terminate, parkLarge, reply: true) &&
                    await set.OperateAsync(large, "clear-second", Contract.Kinds.Terminate, parkLarge, reply: true),
                "two held Terminates with the Park selection are admitted for the Park of 402 kept rows");
            await set.PassUntilAsync(() => set.StoodDown);
            Check(cleared is [170, 340] && pageChecked, $"the first owner opened the clearing and cleared one page before it stopped ({string.Join(", ", cleared)})");

            // Mid-clearing: the arrivals are pending and unkept, a third Terminate is refused at admission, the view still names the
            // Park and seals no terminal selection, and the checkpoint trigger takes only the clearing's own writes.
            var mid = await set.ImageAsync(large);
            var midInputs = await set.InputsAsync(large);
            Check(midInputs.Count(row => row.KeptBy is not null) == 62 && midInputs.Where(row => row.Id.Contains("/" + FoundationRootCreator.OperationDigest("during-1") + "/") ||
                    row.Id.Contains("/" + FoundationRootCreator.OperationDigest("during-2") + "/") || row.Id.Contains("/" + FoundationRootCreator.OperationDigest("during-3") + "/"))
                    .ToArray() is [{ Done: false, KeptBy: null }, { Done: false, KeptBy: null }, { Done: false, KeptBy: null }] during &&
                    during.Select(row => row.Seq).SequenceEqual(during.Select(row => row.Seq).Order()),
                "mid-clearing 62 rows stay kept and the three arrivals are pending and unkept, in Seq order");
            Check(!await set.TerminateAsync(large, "clear-third", parkLarge), "admission refuses a Terminate while the Park is being cleared");
            var midSelection = await set.SelectionAsync(large);
            Check(mid is { Status: InstanceStatus.Faulted, TerminalRevision: null } && set.OpensPark(large, midSelection) && !set.OpensTerminal(large, midSelection),
                "mid-clearing the root stays Faulted and the view seals its Park selection, with no terminal selection");
            Check(await set.TriggerProbeAsync(mid, null, Raw(mid, """ "FoundationParkCleared" = "FoundationParkCleared" + 1 """)) &&
                    !await set.TriggerProbeAsync(mid, """["x"]""", Raw(mid, """ "FoundationParkCleared" = "FoundationParkCleared" + 1 """)) &&
                    !await set.TriggerProbeAsync(mid, null, Raw(mid, """ "FoundationParkCleared" = "FoundationParkCleared" - 1 """)) &&
                    !await set.TriggerProbeAsync(mid, null, Raw(mid, """ "FoundationParkClearing" = 'other', "FoundationParkCleared" = "FoundationParkCleared" + 1 """)) &&
                    !await set.TriggerProbeAsync(mid, null, Raw(mid, """ "FoundationParkCleared" = "FoundationParkCleared" + 1, "State" = "State" || ' ' """)) &&
                    !await set.TriggerProbeAsync(mid, null, Continue(mid)) &&
                    await set.TriggerProbeAsync(mid, ProbeRefusal, Continue(mid)) &&
                    !await set.TriggerProbeAsync(mid, ProbeRefusal, Raw(mid, """ "FoundationParkCleared" = "FoundationParkCleared" + 1 """)),
                "mid-clearing the trigger takes a page write alone, and a refusal that moves only the revision, and nothing else");
            Check(await set.TriggerProbeAsync(parkedProbe, ProbeClearing, Raw(parkedProbe, """ "FoundationParkClearing" = 'probe', "FoundationParkCleared" = 1 """)) &&
                    !await set.TriggerProbeAsync(parkedProbe, null, Raw(parkedProbe, """ "FoundationParkClearing" = 'probe', "FoundationParkCleared" = 1 """)) &&
                    !await set.TriggerProbeAsync(parkedProbe, ProbeClearing, Raw(parkedProbe, """ "FoundationParkClearing" = 'probe', "FoundationParkCleared" = 1, "Suspended" = NOT "Suspended" """)) &&
                    !await set.TriggerProbeAsync(parkedProbe, ProbeClearing, Continue(parkedProbe)),
                "an Open passes only beside its clearing entry with the row kept, and a clearing entry needs the clearing it opens");
            Check(!await set.TriggerProbeEntriesAsync(parkedProbe, [ProbeRefusal, ProbeClearing],
                    Raw(parkedProbe, """ "FoundationParkClearing" = 'probe', "FoundationParkCleared" = 1 """)),
                "an Open beside a refused entry at the same revision is refused: a refusal pins the clearing's columns");
            // A set without Terminate-then-delete takes no protected entry, even where a terminated one would end the root.
            await set.ExecuteAsync($"""UPDATE "Messages" SET "FoundationKeptBy" = NULL WHERE "Target" = {parkedProbe.Actor.Id} AND "FoundationKeptBy" IS NOT NULL""");
            try
            {
                Check(await set.TriggerProbeAsync(parkedProbe, ProbeTerminated, End(parkedProbe)) &&
                        !await set.TriggerProbeAsync(parkedProbe, ProbeProtected, End(parkedProbe)),
                    "a set without Terminate-then-delete ends a parked root beside a terminated entry and refuses a protected one");
            }
            finally
            {
                await set.ExecuteAsync($"""UPDATE "Messages" SET "FoundationKeptBy" = 0 WHERE "Target" = {parkedProbe.Actor.Id} AND "Kind" = 'fail'""");
            }

            // A new owner continues from the durable state and ends the root.
            await set.RetakeAsync();
            await set.DriveAsync(large, image => image is { Status: InstanceStatus.Cancelled });
            await set.SettledInputsAsync(large);
            await set.TerminatedAsync(large, "clear-large", cleared: 402);
            var journal = Outcomes(await set.Cold.ReadJournalOfAsync(0, largePark.Actor.Id, 0, stop));
            Check(journal is ["""{"Outcome":"clearing","Operation":"clear-large","Cleared":170}""",
                    """{"Outcome":"refused","Operation":"clear-second","Reason":"clearing"}""",
                    """{"Outcome":"terminated","Operation":"clear-large","Cleared":402}"""],
                $"the journal records the Open, the second Terminate's refusal and the end with the total ({string.Join(" | ", journal)})");
            var after = await set.InputsAsync(large);
            var duringIds = arrivals.Select(op => after.Single(row => row.Id.Contains("/" + FoundationRootCreator.OperationDigest(op) + "/")).Id).ToArray();
            var clearedRows = after.Where(row => row.Kind != Contract.Kinds.Terminate && !duringIds.Contains(row.Id)).ToArray();
            Check(after.All(row => row.Done && row.KeptBy is null) && clearedRows.Length == 402 && clearedRows.All(row => !row.Content),
                "every kept row is cleared once and the 402 rows match Cleared");
            Check(settled.Where(duringIds.Contains).SequenceEqual(duringIds) && settled.Count(duringIds.Contains) == 3 &&
                    settled.IndexOf(duringIds[0]) > settled.IndexOf(after.Last(row => row.Kind == Contract.Kinds.Terminate).Id) &&
                    !set.Probe.Saw(large, "ordinary"),
                "the arrivals settle once each after the end, unseen by the actor, in Seq order and outside Cleared");
            set.Answered("clear-large", "started");
            set.Answered("clear-second", "ResolutionOpen");

            // An arrival between two Terminates, all admitted before Open, stays withheld, and the second Terminate is refused
            // with the operator rows as its settled prefix, so the same owner ends the root and answers it once.
            Check(await set.SendAsync(between, "between-fail", "fail"), "the clearing set admits a failing input for the root with an arrival between");
            var betweenPark = await set.DriveAsync(between, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(await set.SendAsync(between, "b1", "ordinary"), "the clearing set admits an arrival to copy");
            await set.SettledInputsAsync(between);
            var copies = Enumerable.Range(1, 200).Select(n => $"b{n:D3}").ToArray();
            var copyIds = copies.Select(op => $"{between.Id}/{FoundationRootCreator.OperationDigest(op)}/{Guid.NewGuid():N}").ToArray();
            await using (var write = OpenClearing())
                Check(await write.Database.ExecuteSqlInterpolatedAsync($$"""
                    INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Payload", "Due", "KeepUntil",
                        "Done", "Seen", "Attempts", "Trial", "JobGeneration", "FoundationKeptBy")
                    SELECT m."InboxScope", d.id, m."Partition", m."Target", m."Kind", m."Start", '{}', m."Due", m."KeepUntil", true, 0, 0, 0, 0, 1
                    FROM "Messages" m, unnest({{copyIds}}::text[]) WITH ORDINALITY d(id, n)
                    WHERE m."Target" = {{betweenPark.Actor.Id}} AND m."FoundationKeptBy" = 1 ORDER BY d.n
                    """, stop) == 200, "raw SQL keeps 200 more arrivals for the root with an arrival between");
            var parkBetween = await set.ParkSelectionAsync(between, 2);
            Check(await set.TerminateAsync(between, "between-first", parkBetween) && await set.SendAsync(between, "between-arrival", "ordinary") &&
                    await set.OperateAsync(between, "between-second", Contract.Kinds.Terminate, parkBetween, reply: true),
                "a Terminate, an arrival and a held Terminate are admitted in that order before Open");
            var betweenOwner = set.Owner;
            await set.DriveAsync(between, image => image is { Status: InstanceStatus.Cancelled });
            await set.SettledInputsAsync(between);
            Check(Outcomes(await set.Cold.ReadJournalOfAsync(0, betweenPark.Actor.Id, 0, stop)) is
                    ["""{"Outcome":"clearing","Operation":"between-first","Cleared":170}""",
                     """{"Outcome":"refused","Operation":"between-second","Reason":"clearing"}""",
                     """{"Outcome":"terminated","Operation":"between-first","Cleared":202}"""] &&
                    ReferenceEquals(set.Owner, betweenOwner) && !set.StoodDown && !set.Probe.Saw(between, "ordinary"),
                "the same owner refused the second Terminate past the withheld arrival and ended the root, which kept the arrival out of Cleared");
            set.Answered("between-second", "ResolutionOpen");

            // An input due shortly is read ahead with the Terminate before Open and comes due mid-clearing. The owner drops its
            // copy at Open, so it is never kept: it stays pending with no stand-down and settles as Ended after the end.
            Check(await set.SendAsync(ahead, "ahead-fail", "fail"), "the clearing set admits a failing input for the read-ahead root");
            var aheadPark = await set.DriveAsync(ahead, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(await set.SendAsync(ahead, "c1", "ordinary"), "the clearing set admits an arrival to copy for the read-ahead root");
            await set.SettledInputsAsync(ahead);
            var aheadCopies = Enumerable.Range(1, 200).Select(n => $"{ahead.Id}/{FoundationRootCreator.OperationDigest($"a{n:D3}")}/{Guid.NewGuid():N}").ToArray();
            await using (var write = OpenClearing())
                Check(await write.Database.ExecuteSqlInterpolatedAsync($$"""
                    INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Payload", "Due", "KeepUntil",
                        "Done", "Seen", "Attempts", "Trial", "JobGeneration", "FoundationKeptBy")
                    SELECT m."InboxScope", d.id, m."Partition", m."Target", m."Kind", m."Start", '{}', m."Due", m."KeepUntil", true, 0, 0, 0, 0, 1
                    FROM "Messages" m, unnest({{aheadCopies}}::text[]) WITH ORDINALITY d(id, n)
                    WHERE m."Target" = {{aheadPark.Actor.Id}} AND m."FoundationKeptBy" = 1 ORDER BY d.n
                    """, stop) == 200, "raw SQL keeps 200 more arrivals for the read-ahead root");
            var aheadDue = (await set.ImageAsync(ahead)).AsOf + 1_500;
            Check(await set.SendAsync(ahead, "ahead-input", "ordinary", aheadDue) &&
                    await set.TerminateAsync(ahead, "ahead-terminate", await set.ParkSelectionAsync(ahead, 2)),
                "an input due shortly and a Terminate with the Park selection behind it are admitted");
            holdAfterOpen = (aheadPark.Actor.Id, aheadDue);
            var aheadOwner = set.Owner;
            await set.DriveAsync(ahead, image => image is { Status: InstanceStatus.Cancelled });
            await set.SettledInputsAsync(ahead);
            var aheadInput = (await set.InputsAsync(ahead)).Single(row => row.Id.Contains("/" + FoundationRootCreator.OperationDigest("ahead-input") + "/"));
            Check(clearingHold.Held == 1 && ReferenceEquals(set.Owner, aheadOwner) && !set.StoodDown &&
                    aheadInput is { Done: true, KeptBy: null } && !set.Probe.Saw(ahead, "ordinary") &&
                    Outcomes(await set.Cold.ReadJournalOfAsync(0, aheadPark.Actor.Id, 0, stop)) is [_, """{"Outcome":"terminated","Operation":"ahead-terminate","Cleared":202}"""],
                "the input read ahead and due mid-clearing was never kept, the owner did not stand down, and it settled after the end outside Cleared");

            // A clearing control's header names a Park; one that names none is refused at decode.
            var decoded = PostgresRootAdmission.DecodeOperator(ControlRow(true), between.Id, true, true, true, operatorReplies: true, parkedTerminate: true);
            Check(decoded.Operator is { RootParkClear: true, RootParkMark: not null }, "a clearing control's header decodes as a control that names its Park");
            try
            {
                PostgresRootAdmission.DecodeOperator(ControlRow(false), between.Id, true, true, true, operatorReplies: true, parkedTerminate: true);
                throw new InvalidOperationException("A clearing control without a Park decoded.");
            }
            catch (StoreException error) when (error.Message == "The operator occurrence differs from its bound reference and operation.")
            {
                Check(true, "a clearing control header without a Park is refused at decode");
            }
            return;

            MessageEntity ControlRow(bool park)
            {
                var occurrence = $"operator/{between.Id}/{FoundationRootCreator.OperationDigest("control")}/{Guid.NewGuid():N}";
                var header = "!kernel-operator/1:" + DurableKeyJson.Write(new
                {
                    Reference = between.Id, Operation = "control", Creation = 1L, Kind = Contract.Kinds.Terminate, ExplicitZeroDue = false,
                    Park = park ? new FoundationParkMark(2, 1) : null, Window = park ? 1000L : (long?)null, Clear = true
                });
                return new MessageEntity { InboxScope = -4, Id = occurrence, Target = "fixture", Kind = Contract.Kinds.Terminate, Start = header, Due = 0, KeepUntil = 1 };
            }

            async Task<EfStore.RootImage> ParkProbeAsync()
            {
                Check(await set.SendAsync(probed, "probe-fail", "fail"), "the clearing set admits a failing input for the probe root");
                return await set.DriveAsync(probed, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            }

            async ValueTask<Committed> Write(EfStore store, CommitBatch batch, CancellationToken token)
            {
                if (ReferenceEquals(store, frozen)) return new Committed(false, 0, []);
                var clear = batch.Turns.SingleOrDefault(turn => turn.FoundationRootOperator?.Id == "clear-large");
                var page = clear is { Journal: null } && clear.FoundationRootOperator!.Operator!.RootParkClear;
                if (page && !pageChecked)
                    Check(!(await store.CommitAsync(batch with { Epoch = batch.Epoch - 1 }, token)).Landed, "a page under a stale epoch is refused");
                var committed = await store.CommitAsync(batch, token);
                if (!committed.Landed) return committed;
                foreach (var turn in batch.Turns) settled.AddRange(turn.Done.Select(key => key.Id));
                if (clear is not null && clear.Row?.Status == InstanceStatus.Faulted) cleared.Add((await set.ClearingAsync(large)).Cleared);
                if (holdAfterOpen is { } hold && batch.Turns.Any(turn => turn.FoundationRootOperator?.Id == "ahead-terminate" &&
                        turn.Journal?.StartsWith("""{"Outcome":"clearing",""", StringComparison.Ordinal) == true))
                {
                    // The next page's Park facts wait until the read-ahead input is due, so it comes due mid-clearing.
                    clearingHold.Hold(hold.Actor, hold.Due);
                    holdAfterOpen = null;
                }
                if (clear is { Journal: { } entry } && entry.StartsWith("""{"Outcome":"clearing",""", StringComparison.Ordinal))
                    foreach (var arrival in arrivals) Check(await set.SendAsync(large, arrival, "ordinary"), $"the clearing root admits {arrival} after Open");
                if (page && !pageChecked)
                {
                    pageChecked = true;
                    var before = await set.ClearingAsync(large);
                    Check(!(await store.CommitAsync(batch, token)).Landed && await set.ClearingAsync(large) == before,
                        "the page replayed after it committed is refused and changes nothing");
                    // After this page the owner dies: none of its commits lands, and the next control waits in the inbox.
                    frozen = store;
                }
                return committed;
            }
        }

        // S5b-3a: a Terminate-then-delete of a parked root clears the Park as a Terminate does and, in the commit that ends the
        // root, retires it with the deletion marker unless the row is protected then, when it is terminated and kept.
        async Task ThenDeleteSet()
        {
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + ThenDeleteSchema, stop);
            var options = new DbContextOptionsBuilder<KernelDb>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = ThenDeleteSchema }.ConnectionString).Options;
            var capabilities = RootProfiles.Formats[StoreFormat.RootOperatorReplies] | RootProfiles.Formats[StoreFormat.RootParkArrivals];
            KernelDb OpenThenDelete() => KernelDb.ForUnshippedComposition(options, ComposedThenDelete, capabilities);
            EfStore? frozen = null;
            var pageSeen = false;
            var arrivals = new[] { "td-during-1", "td-during-2" };
            Contract.ActorRef large = default;
            await using var set = new Set("parked-then-delete", OpenThenDelete, owned, inBatch, tenant, stop);
            set.Write = Write;
            await set.InstallAsync(createTables: true);
            await set.Cold.InstallRootWorkflowHistoryAsync(stop);
            await set.OwnAsync();
            var one = await set.StartAsync("td-one");
            large = await set.StartAsync("td-large");
            var kept = await set.StartAsync("td-kept");
            var probed = await set.StartAsync("td-probe");
            var pair = await set.StartAsync("td-pair");
            var reverse = await set.StartAsync("td-reverse");
            var residue = await set.StartAsync("td-residue");
            var keptLarge = await set.StartAsync("td-kept-large");
            set.Probe.FailsOn.Add((probed.Id, Contract.Kinds.Terminate));

            // One page: the end commit retires the root, answers the held caller "deleted" once and removes its history row.
            Check(await set.SendAsync(one, "td-one-fail", "fail"), "the Terminate-then-delete set admits a failing input");
            var onePark = await set.DriveAsync(one, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            var parkOne = await set.ParkSelectionAsync(one, 2);
            Check(await set.HistoryRowsAsync(one) == 1, "the parked root has its history row");
            Check(await set.OperateAsync(one, "td-one", Contract.Kinds.Terminate, parkOne, reply: true, thenDelete: true),
                "a held Terminate-then-delete with the Park selection is admitted");
            await set.RetiredAsync(one);
            Check((await set.Cold.ReadJournalOfAsync(0, onePark.Actor.Id, 0, stop)) is [{ Revision: -1, Entries: """["delete"]""" }] &&
                    await set.HistoryRowsAsync(one) == 0 && (await set.InputsOfAsync(onePark.Actor.Id)).All(row => row.Done && row.KeptBy is null && !row.Content),
                "the root is retired with the deletion marker as its only journal row, its history row and kept rows gone");
            set.Answered("td-one", "deleted");

            // Paged: 402 kept rows, arrivals during the clearing, and a takeover after the first page.
            Check(await set.SendAsync(large, "td-large-fail", "fail"), "the Terminate-then-delete set admits a failing input for the large Park");
            var largePark = await set.DriveAsync(large, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(await set.SendAsync(large, "d1", "ordinary"), "the Terminate-then-delete set admits an arrival to copy");
            await set.SettledInputsAsync(large);
            var copies = Enumerable.Range(1, 400).Select(n => $"{large.Id}/{FoundationRootCreator.OperationDigest($"d{n:D3}")}/{Guid.NewGuid():N}").ToArray();
            await using (var write = OpenThenDelete())
                Check(await write.Database.ExecuteSqlInterpolatedAsync($$"""
                    INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Payload", "Due", "KeepUntil",
                        "Done", "Seen", "Attempts", "Trial", "JobGeneration", "FoundationKeptBy")
                    SELECT m."InboxScope", d.id, m."Partition", m."Target", m."Kind", m."Start", '{}', m."Due", m."KeepUntil", true, 0, 0, 0, 0, 1
                    FROM "Messages" m, unnest({{copies}}::text[]) WITH ORDINALITY d(id, n)
                    WHERE m."Target" = {{largePark.Actor.Id}} AND m."FoundationKeptBy" = 1 ORDER BY d.n
                    """, stop) == 400, "raw SQL keeps 400 more arrivals for the large Terminate-then-delete");
            Check(await set.OperateAsync(large, "td-large", Contract.Kinds.Terminate, await set.ParkSelectionAsync(large, 2), reply: true, thenDelete: true),
                "a held Terminate-then-delete is admitted for the Park of 402 kept rows");
            await set.PassUntilAsync(() => set.StoodDown);
            Check(pageSeen && await set.ImageAsync(large) is { Status: InstanceStatus.Faulted } && (await set.ClearingAsync(large)).Cleared == 340 &&
                    (await set.InputsAsync(large)).Count(row => arrivals.Any(op => row.Id.Contains("/" + FoundationRootCreator.OperationDigest(op) + "/")) &&
                        !row.Done && row.KeptBy is null) == 2,
                "the first owner opened the clearing and cleared one page before it stopped; the arrivals are pending and unkept");
            await set.RetakeAsync();
            await set.RetiredAsync(large);
            var largeInputs = await set.InputsOfAsync(largePark.Actor.Id);
            Check((await set.Cold.ReadJournalOfAsync(0, largePark.Actor.Id, 0, stop)) is [{ Revision: -1, Entries: """["delete"]""" }] &&
                    await set.HistoryRowsAsync(large) == 0 && largeInputs.All(row => row.Done && row.KeptBy is null && !row.Content) &&
                    !set.Probe.Saw(large, "ordinary"),
                "the next owner cleared the rest and retired the root; the arrivals were settled by the retirement, unseen by the actor");
            set.Answered("td-large", "started");

            // Protected at the end: terminated and kept, Cancelled with its terminal mark, then deleted through ordinary retirement.
            set.Probe.Protects.Add(kept.Id);
            Check(await set.SendAsync(kept, "td-kept-fail", "fail"), "the Terminate-then-delete set admits a failing input for the protected root");
            var keptPark = await set.DriveAsync(kept, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(await set.OperateAsync(kept, "td-kept", Contract.Kinds.Terminate, await set.ParkSelectionAsync(kept, 2), reply: true, thenDelete: true),
                "a held Terminate-then-delete is admitted for the protected root");
            var keptEnd = await set.DriveAsync(kept, image => image is { Status: InstanceStatus.Cancelled });
            Check(keptEnd.TerminalRevision == keptEnd.Revision && keptEnd.TerminalAt is > 0 &&
                    Outcomes(await set.Cold.ReadJournalOfAsync(0, keptPark.Actor.Id, 0, stop)) is [.., """{"Outcome":"protected","Disposition":"terminated","Operation":"td-kept","Cleared":1}"""],
                "the protected root is terminated and kept: Cancelled with its terminal mark, journalled as protected");
            set.Answered("td-kept", "terminated and kept");
            set.Probe.Protects.Remove(kept.Id);
            Check(await set.OperateAsync(kept, "td-kept-delete", Contract.Kinds.Delete, await set.SelectionAsync(kept)),
                "a Delete with its terminal selection is admitted once protection ends");
            await set.RetiredAsync(kept);

            // A Terminate then a held Terminate-then-delete on one Park: the first ends the root, and the second names a Park the
            // root no longer has, so it is refused as a stale selection and answered once; it never throws.
            Check(await set.SendAsync(pair, "td-pair-fail", "fail"), "the Terminate-then-delete set admits a failing input for the pair root");
            await set.DriveAsync(pair, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            var parkPair = await set.ParkSelectionAsync(pair, 2);
            Check(await set.TerminateAsync(pair, "td-pair-terminate", parkPair) &&
                    await set.OperateAsync(pair, "td-pair-delete", Contract.Kinds.Terminate, parkPair, reply: true, thenDelete: true),
                "a Terminate and a held Terminate-then-delete with one Park selection are admitted");
            var pairEnd = await set.DriveAsync(pair, image => image is { Status: InstanceStatus.Cancelled });
            await set.SettledInputsAsync(pair);
            Check(await set.ImageAsync(pair) is { Status: InstanceStatus.Cancelled } ended && ended.Revision == pairEnd.Revision,
                "the second command settled on the ended root and left it Cancelled, unjournalled");
            set.Answered("td-pair-delete", "StaleSelection");
            // The reverse order: the Terminate-then-delete retires the root, and the held Terminate behind it is answered once.
            Check(await set.SendAsync(reverse, "td-reverse-fail", "fail"), "the Terminate-then-delete set admits a failing input for the reverse root");
            await set.DriveAsync(reverse, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            var parkReverse = await set.ParkSelectionAsync(reverse, 2);
            Check(await set.OperateAsync(reverse, "td-reverse-delete", Contract.Kinds.Terminate, parkReverse, thenDelete: true) &&
                    await set.OperateAsync(reverse, "td-reverse-terminate", Contract.Kinds.Terminate, parkReverse, reply: true),
                "a Terminate-then-delete and a held Terminate with one Park selection are admitted");
            await set.RetiredAsync(reverse);
            set.AnsweredError("td-reverse-terminate", "missing");

            // A pending row the retirement refuses (here one without a keep window) makes a Terminate-then-delete outstanding.
            Check(await set.SendAsync(residue, "td-residue-fail", "fail"), "the Terminate-then-delete set admits a failing input for the residue root");
            var residuePark = await set.DriveAsync(residue, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            await set.ExecuteAsync($"""
                INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Due", "Seen", "Attempts", "Trial", "Done")
                VALUES (-4, 'td-residue-pending', 0, {residuePark.Actor.Id}, 'ordinary', 4102444800000, 0, 0, 0, false)
                """);
            try
            {
                Check(await set.OperateAsync(residue, "td-residue-delete", Contract.Kinds.Terminate, await set.ParkSelectionAsync(residue, 2),
                        reply: true, thenDelete: true), "a held Terminate-then-delete is admitted for the root with a pending residue");
                await set.DriveAsync(residue, image => image is { Revision: 3 });
            }
            finally { await set.ExecuteAsync($"""DELETE FROM "Messages" WHERE "Id" = 'td-residue-pending'"""); }
            await set.SettledAsync(residue, "td-residue-delete", reason: "outstanding");
            set.Answered("td-residue-delete", "ParkOutstanding");

            // Protected at the last page of a clearing: terminated and kept, the held caller answered "started" at Open only.
            set.Probe.Protects.Add(keptLarge.Id);
            Check(await set.SendAsync(keptLarge, "td-kept-large-fail", "fail"), "the Terminate-then-delete set admits a failing input for the protected large root");
            var keptLargePark = await set.DriveAsync(keptLarge, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(await set.SendAsync(keptLarge, "k1", "ordinary"), "the Terminate-then-delete set admits an arrival to copy for the protected large root");
            await set.SettledInputsAsync(keptLarge);
            var keptCopies = Enumerable.Range(1, 400).Select(n => $"{keptLarge.Id}/{FoundationRootCreator.OperationDigest($"k{n:D3}")}/{Guid.NewGuid():N}").ToArray();
            await using (var write = OpenThenDelete())
                Check(await write.Database.ExecuteSqlInterpolatedAsync($$"""
                    INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Payload", "Due", "KeepUntil",
                        "Done", "Seen", "Attempts", "Trial", "JobGeneration", "FoundationKeptBy")
                    SELECT m."InboxScope", d.id, m."Partition", m."Target", m."Kind", m."Start", '{}', m."Due", m."KeepUntil", true, 0, 0, 0, 0, 1
                    FROM "Messages" m, unnest({{keptCopies}}::text[]) WITH ORDINALITY d(id, n)
                    WHERE m."Target" = {{keptLargePark.Actor.Id}} AND m."FoundationKeptBy" = 1 ORDER BY d.n
                    """, stop) == 400, "raw SQL keeps 400 more arrivals for the protected large root");
            Check(await set.OperateAsync(keptLarge, "td-kept-large", Contract.Kinds.Terminate, await set.ParkSelectionAsync(keptLarge, 2), reply: true, thenDelete: true),
                "a held Terminate-then-delete is admitted for the protected Park of 402 kept rows");
            var keptLargeEnd = await set.DriveAsync(keptLarge, image => image is { Status: InstanceStatus.Cancelled });
            Check(keptLargeEnd.TerminalRevision == keptLargeEnd.Revision &&
                    Outcomes(await set.Cold.ReadJournalOfAsync(0, keptLargePark.Actor.Id, 0, stop)) is
                        ["""{"Outcome":"clearing","Operation":"td-kept-large","Cleared":170}""",
                         """{"Outcome":"protected","Disposition":"terminated","Operation":"td-kept-large","Cleared":402}"""],
                "the clearing protected at its last page ends Cancelled and kept, journalled as protected with the total");
            set.Answered("td-kept-large", "started");
            set.Probe.Protects.Remove(keptLarge.Id);

            // The parked retirement branch: a raw retirement passes on a parked root with nothing kept (rolled back), and each
            // condition of the C# retirement lock refuses it.
            Check(await set.TerminateAsync(probed, "td-probe-terminate"), "a Terminate whose own turn fails is admitted for the probe root");
            await set.DriveAsync(probed, image => image is { Revision: 3, Status: InstanceStatus.Faulted });
            var parkedProbe = await set.ImageAsync(probed);
            Check(await set.TriggerProbeAsync(parkedProbe, null, Retire(parkedProbe)), "a parked root with nothing kept passes the parked retirement (rolled back)");
            Check(await set.TriggerProbeAsync(parkedProbe, ProbeProtected, End(parkedProbe)),
                "a parked root with nothing kept ends Cancelled beside a protected entry (rolled back)");
            foreach (var (label, planted, cleared) in new[]
            {
                ("jobs", """ "Jobs" = 'fixture' """, """ "Jobs" = NULL """),
                ("children", """ "Children" = 'fixture' """, """ "Children" = NULL """),
                ("a parent", """ "Parent" = 'fixture' """, """ "Parent" = NULL """),
                ("custom handles", """ "CustomHandles" = 'fixture' """, """ "CustomHandles" = NULL """)
            })
            {
                await set.BypassAsync(parkedProbe, planted);
                bool retired;
                try { retired = await set.TriggerProbeAsync(parkedProbe, null, Retire(parkedProbe)); }
                finally { await set.BypassAsync(parkedProbe, cleared); }
                Check(!retired, $"the parked retirement is refused for a root with {label}");
            }
            foreach (var (label, plant, remove) in new (string, FormattableString, FormattableString)[]
            {
                ("a subscription", $"""INSERT INTO "Subscriptions" ("Key", "Instance", "Wait", "Partition") VALUES ('probe', {parkedProbe.Actor.Id}, 'probe', 0)""",
                    $"""DELETE FROM "Subscriptions" WHERE "Key" = 'probe'"""),
                ("a pending row that is not a direct input", $"""
                    INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Due", "Seen", "Attempts", "Trial", "Done")
                    VALUES (-4, 'probe-pending', 0, {parkedProbe.Actor.Id}, 'ordinary', 4102444800000, 0, 0, 0, false)
                    """, $"""DELETE FROM "Messages" WHERE "Id" = 'probe-pending'"""),
                ("a kept row", $"""
                    INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Due", "KeepUntil", "Seen", "Attempts", "Trial", "Done", "FoundationKeptBy")
                    VALUES (-4, 'probe-kept', 0, {parkedProbe.Actor.Id}, 'ordinary', 0, 1, 0, 0, 0, true, 1)
                    """, $"""DELETE FROM "Messages" WHERE "Id" = 'probe-kept'""")
            })
            {
                await set.ExecuteAsync(plant);
                bool retired;
                try { retired = await set.TriggerProbeAsync(parkedProbe, null, Retire(parkedProbe)); }
                finally { await set.ExecuteAsync(remove); }
                Check(!retired, $"the parked retirement is refused for a root with {label}");
            }
            Check(!await set.LoneEntryCommitsAsync(parkedProbe, ProbeProtected), "a lone protected entry with no checkpoint write is refused at commit");
            Check(!await set.TriggerProbeAsync(parkedProbe, ProbeProtected, Continue(parkedProbe)),
                "a protected entry beside a write that leaves the root parked is refused: it needs the root ended Cancelled");
            return;

            async ValueTask<Committed> Write(EfStore store, CommitBatch batch, CancellationToken token)
            {
                if (ReferenceEquals(store, frozen)) return new Committed(false, 0, []);
                var clear = batch.Turns.SingleOrDefault(turn => turn.FoundationRootOperator?.Id == "td-large");
                var committed = await store.CommitAsync(batch, token);
                if (!committed.Landed) return committed;
                if (clear is { Journal: { } entry } && entry.StartsWith("""{"Outcome":"clearing",""", StringComparison.Ordinal))
                    foreach (var arrival in arrivals) Check(await set.SendAsync(large, arrival, "ordinary"), $"the clearing root admits {arrival} after Open");
                if (clear is { Journal: null } && clear.FoundationRootOperator!.Operator!.RootParkClear && !pageSeen)
                {
                    // After the first page this owner dies: none of its commits lands, and the next control waits in the inbox.
                    pageSeen = true;
                    frozen = store;
                }
                return committed;
            }
        }

        // S5b-3b: a plain Delete that names a parked root's Park, in a set with retirement and without Terminate-then-delete.
        // It clears the Park as a Terminate does, and the commit that leaves nothing kept retires the root still Faulted, unless
        // the row is protected then: the Park resolves as a discard, Faulted under a new terminal mark (DT-4, end state only).
        async Task DeleteSet()
        {
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + DeleteSchema, stop);
            var options = new DbContextOptionsBuilder<KernelDb>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = DeleteSchema }.ConnectionString).Options;
            var capabilities = RootProfiles.Formats[StoreFormat.RootRetirement] | RootCapabilities.OperatorReplies |
                RootProfiles.Formats[StoreFormat.RootParkArrivals];
            KernelDb OpenDelete() => KernelDb.ForUnshippedComposition(options, ComposedDelete, capabilities);
            await using (var check = OpenDelete())
                Check(RootProfiles.MissingPrerequisites(capabilities) == RootCapabilities.None && check.UsesRootParkedOperators &&
                        check.UsesRootRetirement && !check.UsesRootTerminateThenDelete && check.UsesRootParkArrivals && check.UsesRootOperatorReplies,
                    "the Delete set carries Park arrivals with retirement and held replies, without Terminate-then-delete");
            EfStore? frozen = null;
            var pageSeen = false;
            var during = new Dictionary<string, (Contract.ActorRef Root, string Park, string[] Arrivals)>();
            Contract.ActorRef large = default, keptLarge = default;
            var opened = new Dictionary<string, Action>();
            var retries = new List<(string Entry, Set.ClearingRow Clearing, int Kept, long Delay, bool Admitted)>();
            string? parkUnreadableShared = null;
            await using var set = new Set("parked-delete", OpenDelete, owned, inBatch, tenant, stop);
            set.Write = Write;
            await set.InstallAsync(createTables: true);
            await set.Cold.InstallRootWorkflowHistoryAsync(stop);
            await set.OwnAsync();
            var one = await set.StartAsync("del-one");
            large = await set.StartAsync("del-large");
            var kept = await set.StartAsync("del-kept");
            keptLarge = await set.StartAsync("del-kept-large");
            var mid = await set.StartAsync("del-mid");
            var stale = await set.StartAsync("del-stale");
            var residue = await set.StartAsync("del-residue");
            var probed = await set.StartAsync("del-probe");
            set.Probe.FailsOn.Add((kept.Id, Contract.Kinds.Resume));
            set.Probe.FailsOn.Add((probed.Id, Contract.Kinds.Terminate));

            // One page: the Delete retires the root still Faulted, answers its held caller "deleted" once and removes its history row.
            Check(await set.SendAsync(one, "del-one-fail", "fail"), "the Delete set admits a failing input");
            var onePark = await set.DriveAsync(one, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            var parkOne = await set.ParkSelectionAsync(one, 2);
            Check(!await set.OperateAsync(one, "del-one-then-delete", Contract.Kinds.Terminate, parkOne, thenDelete: true) &&
                    !await set.OperateAsync(large, "del-one-other", Contract.Kinds.Delete, parkOne) &&
                    !await set.OperateAsync(one, "del-one-stale", Contract.Kinds.Delete, set.SealPark(one, 3, (await set.ParkAtAsync(onePark))!.Value)) &&
                    await set.HistoryRowsAsync(one) == 1,
                "the Delete set refuses a Terminate-then-delete, and a Delete with another root's Park or a stale Park; the parked root has its history row");
            Check(await set.OperateAsync(one, "del-one", Contract.Kinds.Delete, parkOne, reply: true), "a held Delete with the Park selection is admitted");
            await set.RetiredAsync(one);
            Check((await set.Cold.ReadJournalOfAsync(0, onePark.Actor.Id, 0, stop)) is [{ Revision: -1, Entries: """["delete"]""" }] &&
                    await RetiredFaultedAsync(onePark.Actor.Id) && await set.HistoryRowsAsync(one) == 0 &&
                    (await set.InputsOfAsync(onePark.Actor.Id)).All(row => row.Done && row.KeptBy is null && !row.Content) &&
                    !set.Probe.Saw(one, Contract.Kinds.Delete),
                "the root is retired still Faulted under its terminal mark, with the deletion marker as its only journal row and its history row and kept rows gone");
            set.Answered("del-one", "deleted");

            // Paged: 402 kept rows, a second held Delete and a held Terminate behind the first, arrivals during the clearing, and
            // a takeover after the first page.
            var largePark = await ParkLargeAsync(large, "e");
            var parkLarge = await set.ParkSelectionAsync(large, 2);
            during["del-large"] = (large, parkLarge, ["del-during-1", "del-during-2"]);
            Check(await set.OperateAsync(large, "del-large", Contract.Kinds.Delete, parkLarge, reply: true) &&
                    await set.OperateAsync(large, "del-large-second", Contract.Kinds.Delete, parkLarge, reply: true) &&
                    await set.OperateAsync(large, "del-large-terminate", Contract.Kinds.Terminate, parkLarge, reply: true),
                "a held Delete for the Park of 402 kept rows, and a held Delete and a held Terminate behind it, are admitted");
            await set.PassUntilAsync(() => set.StoodDown);
            Check(pageSeen && await set.ImageAsync(large) is { Status: InstanceStatus.Faulted, TerminalRevision: null } &&
                    (await set.ClearingAsync(large)).Cleared == 340 &&
                    (await set.InputsAsync(large)).Count(row => during["del-large"].Arrivals.Any(op => row.Id.Contains("/" + FoundationRootCreator.OperationDigest(op) + "/")) &&
                        !row.Done && row.KeptBy is null) == 2,
                "the first owner opened the clearing, refused the two commands behind it and cleared one page before it stopped; the arrivals are pending and unkept");
            set.Answered("del-large-second", "ResolutionOpen");
            set.Answered("del-large-terminate", "ResolutionOpen");
            await set.RetakeAsync();
            await set.RetiredAsync(large);
            Check((await set.Cold.ReadJournalOfAsync(0, largePark.Actor.Id, 0, stop)) is [{ Revision: -1, Entries: """["delete"]""" }] &&
                    await RetiredFaultedAsync(largePark.Actor.Id) && await set.HistoryRowsAsync(large) == 0 &&
                    (await set.InputsOfAsync(largePark.Actor.Id)).All(row => row.Done && row.KeptBy is null && !row.Content) &&
                    !set.Probe.Saw(large, "ordinary") && !set.Probe.Saw(large, Contract.Kinds.Delete),
                "the next owner cleared the rest and retired the root still Faulted; the arrivals were settled by the retirement, unseen by the actor");
            set.Answered("del-large", "started");

            // Protected at Open, on a Park that kept the root suspended: a Delete runs ProtectsDeletion at its Open, as the
            // in-memory OpenParkDelete does. One page and paged, a protected root is refused as "protected" and one whose
            // protection cannot be read as "unavailable"; the Park, its kept rows and the root stay as they were.
            Check(await set.OperateAsync(kept, "del-kept-suspend", Contract.Kinds.Suspend), "the Delete set admits a Suspend");
            var suspended = await set.DriveAsync(kept, image => image is { Revision: 2, Status: InstanceStatus.Running, Suspended: true });
            Check(await set.OperateAsync(kept, "del-kept-resume", Contract.Kinds.Resume), "the Delete set admits a Resume that fails");
            var keptPark = await set.DriveAsync(kept, image => image is { Revision: 4, Status: InstanceStatus.Faulted, Suspended: true });
            var parkKept = await set.ParkSelectionAsync(kept, 3);
            set.Probe.Protects.Add(kept.Id);
            Check(await set.OperateAsync(kept, "del-kept", Contract.Kinds.Delete, parkKept, reply: true),
                "a held Delete with the Park selection is admitted for the protected suspended root");
            await set.DriveAsync(kept, image => image is { Revision: 5 });
            await set.SettledAsync(kept, "del-kept", suspended.State, suspended: true, reason: "protected");
            set.Answered("del-kept", "Protected");
            set.Probe.Protects.Remove(kept.Id);
            set.Probe.Unreadable.Add(kept.Id);
            Check(await set.OperateAsync(kept, "del-kept-unreadable", Contract.Kinds.Delete, parkKept, reply: true),
                "a held Delete is admitted for the root whose protection cannot be read");
            await set.DriveAsync(kept, image => image is { Revision: 6 });
            await set.SettledAsync(kept, "del-kept-unreadable", suspended.State, suspended: true, reason: "unavailable");
            set.Answered("del-kept-unreadable", "ProtectionUnavailable");
            set.Probe.Unreadable.Remove(kept.Id);
            // The same Park made larger than a page: 171 kept rows.
            await KeepCopiesAsync(kept, keptPark, "g", 171);
            set.Probe.Protects.Add(kept.Id);
            Check(await set.OperateAsync(kept, "del-kept-paged", Contract.Kinds.Delete, parkKept, reply: true),
                "a held Delete is admitted for the protected Park of 171 kept rows");
            await set.DriveAsync(kept, image => image is { Revision: 7 });
            await set.SettledAsync(kept, "del-kept-paged", suspended.State, suspended: true, reason: "protected");
            set.Answered("del-kept-paged", "Protected");
            set.Probe.Protects.Remove(kept.Id);
            set.Probe.Unreadable.Add(kept.Id);
            Check(await set.OperateAsync(kept, "del-kept-paged-unreadable", Contract.Kinds.Delete, parkKept, reply: true),
                "a held Delete is admitted for the large Park whose protection cannot be read");
            await set.DriveAsync(kept, image => image is { Revision: 8 });
            await set.SettledAsync(kept, "del-kept-paged-unreadable", suspended.State, suspended: true, reason: "unavailable");
            set.Answered("del-kept-paged-unreadable", "ProtectionUnavailable");
            set.Probe.Unreadable.Remove(kept.Id);
            Check((await set.InputsAsync(kept)).Count(row => row.KeptBy == (short)ParkKeepReason.Arrived && row.Content) == 171 &&
                    await set.ClearingAsync(kept) == new Set.ClearingRow(null, null),
                "the refused Deletes left the 171 kept rows with their content and opened no clearing");

            // Protection that appears during the clearing resolves the Park as a discard at its last page: the root stays
            // Faulted and suspended under a new terminal mark, the held caller was answered "started" at Open only, the ended
            // root refuses later inputs and its spent Park selection, and once protection ends a Delete with its terminal
            // selection retires it.
            opened["del-kept-mid"] = () => set.Probe.Protects.Add(kept.Id);
            Check(await set.OperateAsync(kept, "del-kept-mid", Contract.Kinds.Delete, parkKept, reply: true),
                "a held Delete is admitted for the suspended Park while it is not protected");
            await set.DriveAsync(kept, image => image is { TerminalRevision: not null });
            await set.DiscardedAsync(kept, "del-kept-mid", cleared: 171, suspended: true, suspended.State);
            Check(Outcomes(await set.Cold.ReadJournalOfAsync(0, keptPark.Actor.Id, 0, stop))[^2..] is
                    ["""{"Outcome":"clearing","Operation":"del-kept-mid","Cleared":170}""",
                     """{"Outcome":"protected","Disposition":"discarded","Operation":"del-kept-mid","Cleared":171}"""],
                "the clearing opened unprotected and its last page, protected by then, discarded the Park");
            set.Answered("del-kept-mid", "started");
            Check(!await set.SendAsync(kept, "del-kept-late", "ordinary") && !await set.OperateAsync(kept, "del-kept-again", Contract.Kinds.Delete, parkKept) &&
                    !set.OpensPark(kept, await set.SelectionAsync(kept)),
                "the discarded root refuses a later input and its spent Park selection, and its view no longer seals a Park");
            set.Probe.Protects.Remove(kept.Id);
            Check(await set.OperateAsync(kept, "del-kept-delete", Contract.Kinds.Delete, await set.SelectionAsync(kept)),
                "a Delete with its terminal selection is admitted for the discarded root once protection ends");
            await set.RetiredAsync(kept);

            // The same with arrivals admitted during the clearing: they settle as Ended after the discard, with no stand-down.
            var midPark = await ParkLargeAsync(mid, "h", copied: 169);
            var parkMid = await set.ParkSelectionAsync(mid, 2);
            during["del-mid"] = (mid, parkMid, ["del-mid-during-1", "del-mid-during-2"]);
            opened["del-mid"] = () => set.Probe.Protects.Add(mid.Id);
            Check(await set.OperateAsync(mid, "del-mid", Contract.Kinds.Delete, parkMid, reply: true),
                "a held Delete is admitted for the Park of 171 kept rows while it is not protected");
            var midOwner = set.Owner;
            await set.DriveAsync(mid, image => image is { TerminalRevision: not null });
            await set.SettledInputsAsync(mid);
            await set.DiscardedAsync(mid, "del-mid", cleared: 171);
            var midInputs = await set.InputsAsync(mid);
            Check(Outcomes(await set.Cold.ReadJournalOfAsync(0, midPark.Actor.Id, 0, stop)) is
                        ["""{"Outcome":"clearing","Operation":"del-mid","Cleared":170}""",
                         """{"Outcome":"protected","Disposition":"discarded","Operation":"del-mid","Cleared":171}"""] &&
                    ReferenceEquals(set.Owner, midOwner) && !set.StoodDown && midInputs.All(row => row.Done && row.KeptBy is null) &&
                    midInputs.Count(row => during["del-mid"].Arrivals.Any(op => row.Id.Contains("/" + FoundationRootCreator.OperationDigest(op) + "/"))) == 2 &&
                    !set.Probe.Saw(mid, "ordinary"),
                "the discard counts the 171 rows, and the arrivals settle as Ended on the root that ended Faulted");
            set.Answered("del-mid", "started");
            set.Probe.Protects.Remove(mid.Id);

            // Protection that cannot be read at the last page neither deletes nor discards. The control is refused as
            // "unavailable" with the clearing open, and the same commit inserts the next control after a backoff of one second,
            // then two. The owner does not stand down, a Delete is still refused as "clearing" at admission, and once the
            // protection can be read again the last page retires the root.
            var unreadablePark = await ParkLargeAsync(keptLarge, "f", copied: 169);
            var parkUnreadable = await set.ParkSelectionAsync(keptLarge, 2);
            parkUnreadableShared = parkUnreadable;
            during["del-kept-large"] = (keptLarge, parkUnreadable, ["del-kept-during-1", "del-kept-during-2"]);
            opened["del-kept-large"] = () => set.Probe.Unreadable.Add(keptLarge.Id);
            Check(await set.OperateAsync(keptLarge, "del-kept-large", Contract.Kinds.Delete, parkUnreadable, reply: true),
                "a held Delete is admitted for the Park of 171 kept rows while its protection can be read");
            var unreadableOwner = set.Owner;
            await set.PassUntilAsync(() => retries.Count == 2);
            set.Probe.Unreadable.Remove(keptLarge.Id);
            await set.RetiredAsync(keptLarge);
            Check(ReferenceEquals(set.Owner, unreadableOwner) && !set.StoodDown &&
                    retries.Select(retry => retry.Entry).SequenceEqual(Enumerable.Repeat(
                        """{"Outcome":"refused","Operation":"del-kept-large","Reason":"unavailable"}""", 2)) &&
                    retries.All(retry => retry.Clearing == new Set.ClearingRow("del-kept-large", 170) && retry.Kept == 1 && !retry.Admitted) &&
                    retries[0].Delay == 1_000 && retries[1].Delay == 2_000,
                "each unreadable answer was journalled with the clearing open and one row kept, the next control came due after 1 s, " +
                    "then 2 s, admission still refused a Delete, and the same owner ran throughout");
            Check(await RetiredFaultedAsync(unreadablePark.Actor.Id) &&
                    (await set.InputsOfAsync(unreadablePark.Actor.Id)).All(row => row.Done && row.KeptBy is null && !row.Content) &&
                    !set.Probe.Saw(keptLarge, "ordinary"),
                "once the protection could be read the last page retired the root, and the arrivals were settled by the retirement");
            set.Answered("del-kept-large", "started");

            // A Park selection the owner finds stale is refused there as "selection", and the held caller answered once.
            Check(await set.SendAsync(stale, "del-stale-fail", "fail"), "the Delete set admits a failing input for the stale root");
            var staleImage = await set.DriveAsync(stale, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(await set.OperateAsync(stale, "del-stale", Contract.Kinds.Delete, await set.ParkSelectionAsync(stale, 2), reply: true),
                "a held Delete with the Park selection is admitted for the stale root");
            var staleAt = await set.ParkAtAsync(staleImage);
            await set.BypassAsync(staleImage, $""" "FoundationParkAt" = {staleAt + 1} """);
            try { await set.DriveAsync(stale, image => image is { Revision: 3 }); }
            finally { await set.BypassAsync(staleImage, $""" "FoundationParkAt" = {staleAt} """); }
            await set.SettledAsync(stale, "del-stale", reason: "selection");
            set.Answered("del-stale", "StaleSelection");

            // A pending row the retirement refuses (one without a keep window) makes a Delete outstanding.
            Check(await set.SendAsync(residue, "del-residue-fail", "fail"), "the Delete set admits a failing input for the residue root");
            var residuePark = await set.DriveAsync(residue, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            await set.ExecuteAsync($"""
                INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Due", "Seen", "Attempts", "Trial", "Done")
                VALUES (-4, 'del-residue-pending', 0, {residuePark.Actor.Id}, 'ordinary', 4102444800000, 0, 0, 0, false)
                """);
            try
            {
                Check(await set.OperateAsync(residue, "del-residue", Contract.Kinds.Delete, await set.ParkSelectionAsync(residue, 2), reply: true),
                    "a held Delete is admitted for the root with a pending residue");
                await set.DriveAsync(residue, image => image is { Revision: 3 });
            }
            finally { await set.ExecuteAsync($"""DELETE FROM "Messages" WHERE "Id" = 'del-residue-pending'"""); }
            await set.SettledAsync(residue, "del-residue", reason: "outstanding");
            set.Answered("del-residue", "ParkOutstanding");

            // The discard end and the parked retirement that keeps the status, as raw writes on a parked root with nothing kept
            // (rolled back). The discard needs its entry, its terminal mark and the Suspended flag kept; its entry needs the
            // root Faulted with the Park cleared; a retirement needs its terminal mark and nothing kept.
            Check(await set.TerminateAsync(probed, "del-probe-terminate"), "a Terminate whose own turn fails is admitted for the probe root");
            await set.DriveAsync(probed, image => image is { Revision: 3, Status: InstanceStatus.Faulted });
            var parkedProbe = await set.ImageAsync(probed);
            // A discard is the last page of a clearing: without an open clearing it is refused, so a single page never discards.
            Check(!await set.TriggerProbeAsync(parkedProbe, ProbeDiscarded, Discard(parkedProbe)),
                "a discard of a parked root with no open clearing is refused");
            await set.BypassAsync(parkedProbe, """ "FoundationParkClearing" = 'del-probe-clearing', "FoundationParkCleared" = 1 """);
            bool[] clearingProbes;
            try
            {
                clearingProbes =
                [
                    await set.TriggerProbeAsync(parkedProbe, ProbeDiscarded, Discard(parkedProbe)),
                    !await set.TriggerProbeAsync(parkedProbe, null, Discard(parkedProbe)),
                    !await set.TriggerProbeAsync(parkedProbe, ProbeTerminated, Discard(parkedProbe)),
                    !await set.TriggerProbeAsync(parkedProbe, ProbeDiscarded, Discard(parkedProbe, """, "Suspended" = NOT "Suspended" """)),
                    !await set.TriggerProbeAsync(parkedProbe, ProbeDiscarded, Discard(parkedProbe, terminal: false))
                ];
            }
            finally { await set.BypassAsync(parkedProbe, """ "FoundationParkClearing" = NULL, "FoundationParkCleared" = NULL """); }
            Check(clearingProbes.All(passed => passed),
                "with an open clearing, a discarded entry passes beside the discard end only: its entry, Suspended kept and a new terminal mark");
            Check(!await set.TriggerProbeAsync(parkedProbe, ProbeDiscarded, End(parkedProbe)) &&
                    !await set.TriggerProbeAsync(parkedProbe, ProbeDiscarded, Continue(parkedProbe)) &&
                    !await set.LoneEntryCommitsAsync(parkedProbe, ProbeDiscarded),
                "a discarded entry passes only beside the discard end: Faulted, Suspended kept, a new terminal mark and the Park cleared");
            Check(await set.TriggerProbeAsync(parkedProbe, null, RetireFaulted(parkedProbe)) &&
                    !await set.TriggerProbeAsync(parkedProbe, null, RetireFaulted(parkedProbe, terminal: false)),
                "a parked root with nothing kept passes the parked retirement still Faulted (rolled back), and only with its terminal mark");
            await set.ExecuteAsync($"""
                INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Due", "KeepUntil", "Seen", "Attempts", "Trial", "Done", "FoundationKeptBy")
                VALUES (-4, 'del-probe-kept', 0, {parkedProbe.Actor.Id}, 'ordinary', 0, 1, 0, 0, 0, true, 1)
                """);
            bool keptRetired;
            try { keptRetired = await set.TriggerProbeAsync(parkedProbe, null, RetireFaulted(parkedProbe)); }
            finally { await set.ExecuteAsync($"""DELETE FROM "Messages" WHERE "Id" = 'del-probe-kept'"""); }
            Check(!keptRetired, "the parked retirement still Faulted is refused for a root with a kept row");
            return;

            // Parks the root on a failing input and keeps arrivals beside it: one, then the given number of raw copies.
            async Task<EfStore.RootImage> ParkLargeAsync(Contract.ActorRef root, string prefix, int copied = 400)
            {
                Check(await set.SendAsync(root, prefix + "-fail", "fail"), $"the Delete set admits a failing input for the {prefix} Park");
                var park = await set.DriveAsync(root, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
                Check(await set.SendAsync(root, prefix + "-copy", "ordinary"), $"the Delete set admits an arrival to copy for the {prefix} Park");
                await set.SettledInputsAsync(root);
                await KeepCopiesAsync(root, park, prefix, copied);
                return park;
            }

            // Raw SQL keeps the given number of arrivals on a parked root, copied from its own kept arrival, or from another
            // root's direct input when it has none (a Park that kept the root suspended was opened by an operator command).
            async Task KeepCopiesAsync(Contract.ActorRef root, EfStore.RootImage park, string prefix, int copied)
            {
                var copies = Enumerable.Range(1, copied).Select(n => $"{root.Id}/{FoundationRootCreator.OperationDigest($"{prefix}{n:D3}")}/{Guid.NewGuid():N}").ToArray();
                await using var write = OpenDelete();
                Check(await write.Database.ExecuteSqlInterpolatedAsync($$"""
                    INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Payload", "Due", "KeepUntil",
                        "Done", "Seen", "Attempts", "Trial", "JobGeneration", "FoundationKeptBy")
                    SELECT m."InboxScope", d.id, {{new InstanceRef(park.Actor.Id).Partition}}, {{park.Actor.Id}}, m."Kind", m."Start", '{}', m."Due", m."KeepUntil", true, 0, 0, 0, 0, 1
                    FROM (SELECT * FROM "Messages" WHERE "InboxScope" = -4 AND "Kind" = 'ordinary'
                        ORDER BY ("Target" = {{park.Actor.Id}} AND "FoundationKeptBy" = 1) DESC, "Seq" LIMIT 1) m, unnest({{copies}}::text[]) WITH ORDINALITY d(id, n)
                    ORDER BY d.n
                    """, stop) == copied, $"raw SQL keeps {copied} more arrivals for the {prefix} Park");
            }

            // The retired row kept the Faulted status, under a terminal mark at its revision, with its Park cleared.
            async Task<bool> RetiredFaultedAsync(string actor)
            {
                await using var read = OpenDelete();
                return await read.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {actor} AND "Retired" AND "Status" = {(int)InstanceStatus.Faulted}
                        AND "FoundationTerminalRevision" = "Revision" AND "FoundationTerminalAt" IS NOT NULL AND "FoundationParkAt" IS NULL) AS "Value"
                    """).SingleAsync(stop);
            }

            async ValueTask<Committed> Write(EfStore store, CommitBatch batch, CancellationToken token)
            {
                if (ReferenceEquals(store, frozen)) return new Committed(false, 0, []);
                var turn = batch.Turns.SingleOrDefault(turn => turn.FoundationRootOperator?.Id is { } id && during.ContainsKey(id));
                var committed = await store.CommitAsync(batch, token);
                if (!committed.Landed) return committed;
                if (batch.Turns.FirstOrDefault(turn => turn.FoundationRootOperator?.Id == "del-kept-large" &&
                        turn.Journal?.Contains("\"Reason\":\"unavailable\"", StringComparison.Ordinal) == true) is { Journal: { } refused })
                {
                    // The refusal's commit: the clearing, the kept rows, the next control's delay, and admission while it waits.
                    await using var read = OpenDelete();
                    var actor = (await set.ImageAsync(keptLarge)).Actor.Id;
                    // Due and KeepUntil share the commit's time: the delay is Due less KeepUntil's start, its window apart.
                    var control = await read.Database.SqlQuery<ControlRow>($"""
                        SELECT "Due", "KeepUntil", "Start" FROM "Messages"
                        WHERE "Target" = {actor} AND "Kind" = {Contract.Kinds.Delete} AND NOT "Done"
                        """).SingleAsync(token);
                    using var header = JsonDocument.Parse(control.Start[control.Start.IndexOf('{')..]);
                    var delay = control.Due - (control.KeepUntil - header.RootElement.GetProperty("Window").GetInt64());
                    retries.Add((refused, await set.ClearingAsync(keptLarge), (await set.InputsAsync(keptLarge)).Count(row => row.KeptBy is not null),
                        delay, await set.OperateAsync(keptLarge, $"del-kept-retry-{retries.Count}", Contract.Kinds.Delete, parkUnreadableShared!)));
                }
                if (batch.Turns.FirstOrDefault(turn => turn.FoundationRootOperator?.Id is { } id && opened.ContainsKey(id) &&
                        turn.Journal?.StartsWith("""{"Outcome":"clearing",""", StringComparison.Ordinal) == true) is { } open)
                {
                    // The protection changes after the Open, before the next page.
                    opened[open.FoundationRootOperator!.Id]();
                    opened.Remove(open.FoundationRootOperator.Id);
                }
                if (turn is null) return committed;
                var operation = turn.FoundationRootOperator!.Id;
                var (root, park, arrivals) = during[operation];
                if (turn.Journal?.StartsWith("""{"Outcome":"clearing",""", StringComparison.Ordinal) == true)
                {
                    foreach (var arrival in arrivals) Check(await set.SendAsync(root, arrival, "ordinary"), $"the clearing root admits {arrival} after Open");
                    Check(!await set.OperateAsync(root, operation + "-during", Contract.Kinds.Delete, park) &&
                            !await set.TerminateAsync(root, operation + "-during-terminate", park),
                        "admission refuses a Delete and a Terminate with the Park selection while the Park is being cleared");
                }
                if (root.Id == large.Id && turn is { Journal: null } && turn.FoundationRootOperator.Operator!.RootParkClear && !pageSeen)
                {
                    // After the first page this owner dies: none of its commits lands, and the next control waits in the inbox.
                    pageSeen = true;
                    frozen = store;
                }
                return committed;
            }
        }

        async Task RereadAcrossFormats()
        {
            foreach (var (label, schema, format22) in new[] { ("format 22", Format22Schema, true), ("the composed recovery set", RecoverySchema, false) })
            {
#pragma warning disable EF1003 // the schema name is one of this fixture's constants
                await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + schema, stop);
#pragma warning restore EF1003
                var schemaConnection = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
                var hold = new RaceHook(schemaConnection);
                var schemaOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(schemaConnection).AddInterceptors(hold).Options;
                var recoveryTerminations = RootProfiles.Formats[StoreFormat.RootParkRecovery] | RootProfiles.Formats[StoreFormat.RootTerminalSelection];
                Func<KernelDb> openSchema = format22 ? () => KernelDb.ForPostgresRootParkRecovery(schemaOptions)
                    : () => KernelDb.ForUnshippedComposition(schemaOptions, ComposedRecovery, recoveryTerminations);
                await using (var check = openSchema())
                    Check(check.UsesRootParkRecovery && check.UsesRootParkedOperators != format22, $"{label} carries Park recovery" +
                        (format22 ? " without parked operators" : " with parked operators"));
                await using var set = new Set("parked-" + schema, openSchema, owned, inBatch, tenant, stop);
                await set.InstallAsync(createTables: true);
                await set.OwnAsync();
                var root = await set.StartAsync(schema + "-root");
                Check(await set.SendAsync(root, schema + "-fail", "fail"), $"{label} admits a failing input");
                var parked = await set.DriveAsync(root, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
                var due = parked.AsOf + 1_500;
                Check(await set.SendAsync(root, schema + "-input", "ordinary", due), $"{label} admits an input due shortly");
                if (!format22) await set.AttemptLimitAsync(root, schema + "-input");
                Check(await set.OperateAsync(root, schema + "-recover", Contract.Kinds.Recover, await set.ParkSelectionAsync(root, 2)),
                    $"{label} admits a Recover with the Park selection behind it");
                hold.Hold(parked.Actor.Id, due);
                var first = set.Owner;
                if (format22)
                {
                    await set.PassUntilAsync(() => set.StoodDown);
                    Check(hold.Held == 1 && await set.ImageAsync(root) is { Status: InstanceStatus.Faulted },
                        "format 22: the stale Recover failed its commit after the keep, and the owner stood down");
                    await set.RetakeAsync();
                }
                var recovered = await set.DriveAsync(root, image => image is { Status: InstanceStatus.Running });
                var journal = await set.Cold.ReadJournalOfAsync(0, recovered.Actor.Id, 0, stop);
                Check(journal[^1].Entries == Json.Write(new { Outcome = "recovered", Operation = schema + "-recover",
                        Restored = format22 ? 1 : 0, Cleared = format22 ? 1 : 2, AlreadyRearmed = 0 }) &&
                        (format22 || ReferenceEquals(set.Owner, first) && !set.StoodDown),
                    format22 ? "format 22: the next owner read the Recover again and recovered the root with the kept arrival"
                        : "the composed recovery set: the owner that re-parked the root read the Recover again and recovered it, with no stand-down");
            }
        }

        // A composed set with Park arrivals and termination journals the refusal through the arrivals branch and keeps
        // arrivals on a parked root, suspended or not. A held command is refused there and the arrival behind it is kept.
        async Task ArrivalsSet()
        {
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + ArrivalsSchema, stop);
            var arrivalConnection = new NpgsqlConnectionStringBuilder(connection) { SearchPath = ArrivalsSchema }.ConnectionString;
            var race = new RaceHook(arrivalConnection);
            var arrivalOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(arrivalConnection).AddInterceptors(race).Options;
            var arrivalCapabilities = RootProfiles.Formats[StoreFormat.RootParkArrivals] | RootProfiles.Formats[StoreFormat.RootTerminalSelection] |
                RootCapabilities.OperatorReplies;
            KernelDb OpenArrivals() => KernelDb.ForUnshippedComposition(arrivalOptions, ComposedArrivals, arrivalCapabilities);
            await using (var check = OpenArrivals())
                Check(RootProfiles.MissingPrerequisites(arrivalCapabilities) == RootCapabilities.None && check.UsesRootParkedOperators &&
                        check.UsesRootParkArrivals && !check.UsesRootParkRecovery && check.UsesRootStableBody,
                    "the closed arrivals set carries Park arrivals with terminal selection, termination and held replies");
            await using var set = new Set("parked-arrivals", OpenArrivals, owned, inBatch, tenant, stop);
            await set.InstallAsync(createTables: true);
            await set.OwnAsync();
            var plain = await set.StartAsync("arrivals-plain");
            var asleep = await set.StartAsync("arrivals-suspended");
            var holding = await set.StartAsync("arrivals-held");
            var awake = await set.StartAsync("arrivals-running");
            var raceKeep = await set.StartAsync("arrivals-race-keep");
            var raceMark = await set.StartAsync("arrivals-race-mark");
            var raceChildren = await set.StartAsync("arrivals-race-children");
            var together = await set.StartAsync("arrivals-together");
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

            // An arrival kept between the owner's Park facts and its commit changes the kept set: the commit rolls back, the
            // command is polled again with the new facts and clears both rows.
            Check(await set.SendAsync(raceKeep, "race-fail", "fail") &&
                    await set.SendAsync(raceKeep, "race-late", "ordinary", (await set.ImageAsync(raceKeep)).AsOf + 600_000),
                "the arrivals set admits a failing input and a future one for the raced root");
            var raced = await set.DriveAsync(raceKeep, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            var late = (await set.InputsAsync(raceKeep)).Single(row => row.Kind == "ordinary" && !row.Done).Id;
            Check(await set.OperateAsync(raceKeep, "race-keep-terminate", Contract.Kinds.Terminate, await set.ParkSelectionAsync(raceKeep, 2), reply: true),
                "the arrivals set admits a held Terminate with the Park selection for the raced root");
            race.Arm(raced.Actor.Id, $"""UPDATE "Messages" SET "Done" = true, "FoundationKeptBy" = 1 WHERE "Id" = '{late}'""");
            await set.PassUntilAsync(() => race.Fired == 1);
            Check(await set.ImageAsync(raceKeep) is { Revision: 2, Status: InstanceStatus.Faulted },
                "the commit that met a changed kept set rolled back, and its owner stood down");
            await set.RetakeAsync();
            await set.DriveAsync(raceKeep, image => image is { Status: InstanceStatus.Cancelled });
            await set.TerminatedAsync(raceKeep, "race-keep-terminate", cleared: 2);
            Check(race.Fired == 1 && await set.InputsAsync(raceKeep) is [{ Kind: "fail", KeptBy: null }, { Kind: "ordinary", Done: true, KeptBy: null }, _],
                "the command, polled again by the next owner, cleared the raced row too");
            set.Answered("race-keep-terminate", "terminated");

            // A Park mark moved between the owner's facts and its commit fails the lock: the commit rolls back, and the
            // command, polled again, is refused as a stale selection.
            Check(await set.SendAsync(raceMark, "race-mark-fail", "fail"), "the arrivals set admits a failing input for the second raced root");
            var marked = await set.DriveAsync(raceMark, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(await set.TerminateAsync(raceMark, "race-mark-terminate", await set.ParkSelectionAsync(raceMark, 2)),
                "the arrivals set admits a Terminate with the Park selection for the second raced root");
            race.Arm(marked.Actor.Id, $"""
                UPDATE "Instances" SET "FoundationParkAt" = "FoundationParkAt" + 1 WHERE "Id" = '{marked.Actor.Id}';
                UPDATE "FoundationActorList" SET "ParkAt" = "ParkAt" + 1 WHERE "Reference" = '{raceMark.Id}'
                """);
            await set.PassUntilAsync(() => race.Fired == 2);
            Check(await set.ImageAsync(raceMark) is { Revision: 2, Status: InstanceStatus.Faulted },
                "the commit that met a moved Park mark rolled back, and its owner stood down");
            await set.RetakeAsync();
            await set.DriveAsync(raceMark, image => image is { Revision: 3 });
            await set.SettledAsync(raceMark, "race-mark-terminate", reason: "selection");

            // Children planted in the same window fail the commit lock: the commit rolls back cleanly, and the command,
            // polled again, is refused as outstanding.
            Check(await set.SendAsync(raceChildren, "race-children-fail", "fail"), "the arrivals set admits a failing input for the third raced root");
            var planted = await set.DriveAsync(raceChildren, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(await set.TerminateAsync(raceChildren, "race-children-terminate", await set.ParkSelectionAsync(raceChildren, 2)),
                "the arrivals set admits a Terminate with the Park selection for the third raced root");
            race.Arm(planted.Actor.Id, $"""UPDATE "Instances" SET "Children" = 'fixture' WHERE "Id" = '{planted.Actor.Id}'""");
            await set.PassUntilAsync(() => race.Fired == 3);
            Check(await set.ImageAsync(raceChildren) is { Revision: 2, Status: InstanceStatus.Faulted },
                "the commit that met planted children rolled back, and its owner stood down");
            await set.RetakeAsync();
            await set.DriveAsync(raceChildren, image => image is { Revision: 3 });
            await set.SettledAsync(raceChildren, "race-children-terminate", reason: "outstanding");
            Check(await set.InputsAsync(raceChildren) is [{ Kind: "fail", KeptBy: (short)ParkKeepReason.Consumed }, { Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null }] &&
                    race.TriggerRefusals == 0,
                "the lock refused the commit before any write, so no trigger refused it: the Terminate was not kept, and the fault input stays kept");

            // An arrival read with the Terminate and kept first by the same owner: the keep makes the Terminate's Park facts
            // stale, so the owner reads it again and ends the root itself, clearing the arrival. The hook holds the poll's
            // facts read until the arrival is due, so the owner meets both in one pass; it writes nothing.
            Check(await set.SendAsync(together, "together-fail", "fail"), "the arrivals set admits a failing input for the root polled together");
            var togetherImage = await set.DriveAsync(together, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
            var togetherPark = await set.ParkSelectionAsync(together, 2);
            var arrivalDue = (await set.ImageAsync(together)).AsOf + 1_500;
            Check(await set.SendAsync(together, "together-arrival", "ordinary", arrivalDue) &&
                    await set.OperateAsync(together, "together-terminate", Contract.Kinds.Terminate, togetherPark, reply: true),
                "an arrival due shortly and a Terminate with the Park selection behind it are admitted");
            race.Hold(togetherImage.Actor.Id, arrivalDue);
            var togetherOwner = set.Owner;
            await set.DriveAsync(together, image => image is { Status: InstanceStatus.Cancelled });
            await set.TerminatedAsync(together, "together-terminate", cleared: 2);
            Check(race.Held == 1 && ReferenceEquals(set.Owner, togetherOwner) && !set.StoodDown,
                "the owner that kept the arrival read the held Terminate again and ended the root, with no stand-down and no retake");
            set.Answered("together-terminate", "terminated");

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

    private static FormattableString Raw(EfStore.RootImage root, string change) => Change(root, change);

    // A parked root's retirement as the Terminate-then-delete commit writes it.
    private static FormattableString Retire(EfStore.RootImage root) => Change(root, """
        "State" = 'null', "FoundationHeader" = NULL, "FoundationStateWire" = NULL, "Model" = '', "Definition" = '', "Fault" = NULL,
        "StateVersion" = 1, "History" = 2, "NextJobGeneration" = NULL, "Suspended" = false, "Retired" = true, "Status" = 3,
        "FoundationParkAt" = NULL, "FoundationParkRevision" = NULL, "FoundationParkClearing" = NULL, "FoundationParkCleared" = NULL,
        "FoundationTerminalRevision" = "Revision" + 1, "FoundationTerminalAt" = 1
        """);

    // S5b-3b: a protected Delete's end: the Park and its mark cleared, a new terminal mark, the row otherwise as it was.
    private static FormattableString Discard(EfStore.RootImage root, string extra = "", bool terminal = true) => Change(root, """
        "FoundationParkAt" = NULL, "FoundationParkRevision" = NULL, "FoundationParkClearing" = NULL, "FoundationParkCleared" = NULL
        """ + (terminal ? """, "FoundationTerminalRevision" = "Revision" + 1, "FoundationTerminalAt" = 1""" : "") + extra);

    // A parked root's retirement as a Delete writes it: still Faulted, with or without its terminal mark.
    private static FormattableString RetireFaulted(EfStore.RootImage root, bool terminal = true) => Change(root, """
        "State" = 'null', "FoundationHeader" = NULL, "FoundationStateWire" = NULL, "Model" = '', "Definition" = '', "Fault" = NULL,
        "StateVersion" = 1, "History" = 2, "NextJobGeneration" = NULL, "Suspended" = false, "Retired" = true,
        "FoundationParkAt" = NULL, "FoundationParkRevision" = NULL, "FoundationParkClearing" = NULL, "FoundationParkCleared" = NULL
        """ + (terminal ? """, "FoundationTerminalRevision" = "Revision" + 1, "FoundationTerminalAt" = 1""" : ""));

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
        : IAsyncDisposable
    {
        private Contract.IIngress _ingress = null!;
        private readonly ReplyLog _replies = new();
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        private IScopeActivation _owner = null!;
        internal Probe Probe { get; } = new();
        internal IScopeActivation Owner => _owner;
        // The owner gave up its partition after a commit that did not land.
        internal bool StoodDown => _owner is Worker { Owns: false };
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
            await using (var profile = open())
                // Format 22 has no selected-root ingress; its Recover is admitted directly.
                _ingress = profile.UsesRootTerminalSelection
                    ? await Cold.OpenSelectedRootIngressAsync(Probe.Type, TimeSpan.FromHours(1), (scope, _) => ReferenceEquals(scope, tenant), stop)
                    : new PostgresRootIngress(Admission, Probe.Type, authorizeRecover: (_, _) => true);
        }

        // Sees every commit an owner of the set writes, with that owner's store.
        internal Func<EfStore, CommitBatch, CancellationToken, ValueTask<Committed>>? Write { get; set; }

        internal async Task<IScopeActivation> OwnAsync()
        {
            var store = Store();
            var scope = (await ShippedComposition.BindWorkerPartitionAsync(store, open, 0, stop)).Scope;
            var adapter = new ActorAdapter(new EfStore.RootHeaders(scope), Probe);
            IKernelStore kernel = store;
            if (Write is { } write)
            {
                var observed = System.Reflection.DispatchProxy.Create<IKernelStore, PostgresRootParkOpeningChecks.ObservedCommit>();
                var proxy = (PostgresRootParkOpeningChecks.ObservedCommit)(object)observed;
                (proxy.Store, proxy.Write) = (store, (batch, token) => write(store, batch, token));
                kernel = observed;
            }
            // One heartbeat covers the whole fixture: the default ten-second lease can lapse on a loaded machine.
            var host = new DurableHost([new Worker(name, 0, kernel, adapter,
                new WorkerOptions { Partitions = 1, Lease = FixtureLease, Transport = _replies })]);
            await host.HeartbeatAsync(FixtureLease, stop);
            return _owner = await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture owner could not take its partition.");
        }

        // The owner stood down after a commit that did not land. It releases the partition and a new owner takes it. Each
        // activation is disposed once: here when it is replaced, or when the set is disposed.
        internal async Task RetakeAsync()
        {
            await _owner.DisposeAsync();
            await OwnAsync();
        }

        public ValueTask DisposeAsync() => _owner?.DisposeAsync() ?? ValueTask.CompletedTask;

        internal async Task PassUntilAsync(Func<bool> done)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!done())
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(30)) throw new InvalidOperationException($"{name}: the armed race did not fire.");
                await _owner.WokenAsync(null, stop);
                await _owner.RunAsync(stop);
                await Task.Delay(10, stop);
            }
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

        // #5756: a held direct input, admitted as an edge admits it, whose caller is this fixture's held caller. When admitted,
        // the request its answer names (from its reply address); else null.
        internal async Task<string?> HeldSendAsync(Contract.ActorRef root, string id, long dueAt)
        {
            var deadline = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 600_000;
            var request = new Requests().OpenBound(id, deadline, tenant.Tenant, null);
            return await Admission.AdmitDirectRequestAsync(tenant.Tenant, root.Id, "ordinary", "{}", HeldCaller, request, false, stop, dueAt: dueAt)
                == PostgresRootAdmission.DirectResult.Accepted ? ReplyTo.Parse(request.ReplyAddress(HeldCaller, false)).Request : null;
        }

        internal bool AnsweredAny(string request) => _replies.Sent.Any(sent => sent.Reply.Request == request);

        // A retirement answers its held callers after its commit lands, off the pass that drove it: wait for the answer, then
        // require exactly one.
        internal async Task AnsweredErrorEventuallyAsync(string request, string error)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!AnsweredAny(request) && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20, stop);
            await Task.Delay(100, stop);
            AnsweredError(request, error);
            // The caller's side accepts only a reply bound to its own tenant.
            Check(_replies.Sent.Single(sent => sent.Reply.Request == request).Reply.Tenant == tenant.Tenant,
                $"the answer to {request} is bound to the root's tenant");
        }

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

        // Puts a pending input at the owner's attempt limit, so its next delivery re-parks the root.
        internal async Task AttemptLimitAsync(Contract.ActorRef root, string id)
        {
            await using var write = open();
            var actor = (await ImageAsync(root)).Actor.Id;
            if (await write.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Attempts" = {new WorkerOptions().MaxAttempts} WHERE "Target" = {actor} AND "InboxScope" = -4
                    AND "Id" LIKE {"%/" + FoundationRootCreator.OperationDigest(id) + "/%"} AND NOT "Done"
                """, stop) != 1) throw new InvalidOperationException("The attempt-limit fixture changed no row.");
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
                    image.TerminalAt is > 0 && listed is { Status: InstanceStatus.Cancelled, ParkAt: null, ParkFault: null } &&
                    listed.EndedAt == image.TerminalAt && listed.TerminalRevision == image.Revision &&
                    PostgresRootSelection.OpenTerminal(_cursor, selection, tenant.Tenant, root.Id) == new FoundationTerminalStamp(image.Revision, image.TerminalAt.Value),
                $"{operation} ends the parked root Cancelled with its terminal mark, clears {cleared} kept rows and its Park mark");
        }

        // S5b-3b: a protected Delete resolved the Park as a discard: the root stays Faulted with its Suspended flag and state,
        // under a new terminal mark with its Park mark cleared, journalled with the rows it cleared; the view seals the mark.
        internal async Task DiscardedAsync(Contract.ActorRef root, string operation, int cleared, bool suspended = false, string? state = null)
        {
            var image = await ImageAsync(root);
            var journal = await Cold.ReadJournalOfAsync(0, image.Actor.Id, 0, stop);
            var listed = (await Cold.ReadRootActorAsync(tenant.Tenant, root.Id, stop))!.Value.Item;
            var selection = (await _reads.GetAsync(tenant, root, stop))?.Selection;
            Check(journal is [.., { Entries: var entry } last] && last.Revision == image.Revision &&
                    entry == Json.Write(new { Outcome = "protected", Disposition = "discarded", Operation = operation, Cleared = cleared }) &&
                    image is { Status: InstanceStatus.Faulted } && image.Suspended == suspended && image.TerminalRevision == image.Revision &&
                    image.State == (state ?? _running[root].State) && await ParkAtAsync(image) is null && image.TerminalAt is > 0 &&
                    listed is { Status: InstanceStatus.Faulted, ParkAt: null, ParkFault: null } &&
                    listed.EndedAt == image.TerminalAt && listed.TerminalRevision == image.Revision &&
                    PostgresRootSelection.OpenTerminal(_cursor, selection, tenant.Tenant, root.Id) == new FoundationTerminalStamp(image.Revision, image.TerminalAt.Value),
                $"{operation} resolves the protected Park as a discard: Faulted{(suspended ? " and suspended" : "")} under a new terminal mark, {cleared} kept rows cleared");
        }

        internal async Task ExecuteAsync(FormattableString sql)
        {
            await using var write = open();
            await write.Database.ExecuteSqlInterpolatedAsync(sql, stop);
        }

        internal async Task<int> HistoryRowsAsync(Contract.ActorRef root)
        {
            await using var read = open();
            return await read.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM "FoundationWorkflowHistory" WHERE "Tenant" = {tenant.Tenant} AND "Reference" = {root.Id}
                """).SingleAsync(stop);
        }

        internal async Task<string> SelectionAsync(Contract.ActorRef root) => (await _reads.GetAsync(tenant, root, stop))!.Selection!;
        internal bool OpensPark(Contract.ActorRef root, string selection) => PostgresRootSelection.OpenPark(_cursor, selection, tenant.Tenant, root.Id) is not null;
        internal bool OpensTerminal(Contract.ActorRef root, string selection) => PostgresRootSelection.OpenTerminal(_cursor, selection, tenant.Tenant, root.Id) is not null;

        internal sealed record ClearingRow(string? Clearing, long? Cleared);
        // The root's open clearing: the Terminate's operation and the rows cleared so far.
        internal async Task<ClearingRow> ClearingAsync(Contract.ActorRef root)
        {
            await using var read = open();
            var actor = (await ImageAsync(root)).Actor.Id;
            return await read.Database.SqlQuery<ClearingRow>($"""
                SELECT "FoundationParkClearing" AS "Clearing", "FoundationParkCleared" AS "Cleared" FROM "Instances" WHERE "Id" = {actor}
                """).SingleAsync(stop);
        }

        // Drives the owner until the root is retired: no checkpoint, no list row.
        internal async Task RetiredAsync(Contract.ActorRef root)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (await Cold.ReadFirstRootAsync(tenant.Tenant, root.Id, stop) is not null)
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(30)) throw new InvalidOperationException($"{name}: {_names[root.Id]} was not retired.");
                await _owner.WokenAsync(null, stop);
                await _owner.RunAsync(stop);
                await Task.Delay(10, stop);
            }
            Check(await Cold.ReadRootActorAsync(tenant.Tenant, root.Id, stop) is null && await _reads.GetAsync(tenant, root, stop) is null,
                $"the Delete retires {_names[root.Id]}: its checkpoint and list row are gone");
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

        internal async Task<InputRow[]> InputsAsync(Contract.ActorRef root) => await InputsOfAsync((await ImageAsync(root)).Actor.Id);

        // The direct inputs of an actor by its id, which stay readable after the root is retired.
        internal async Task<InputRow[]> InputsOfAsync(string actor)
        {
            await using var read = open();
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
        internal void AnsweredError(string request, string error) =>
            Check(_replies.Sent.Where(sent => sent.Reply.Request == request && sent.Node == HeldCaller).ToArray() is [var only] &&
                    only.Reply.Error == error, $"the owner answers the held caller of {request} once, with the error {error}");

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
        internal Task<bool> TriggerProbeAsync(EfStore.RootImage root, string? entries, FormattableString? write) =>
            TriggerProbeEntriesAsync(root, entries is null ? [] : [entries], write);

        // Several raw entries at the next revision, then one write, always rolled back.
        internal async Task<bool> TriggerProbeEntriesAsync(EfStore.RootImage root, string[] entries, FormattableString? write)
        {
            await using var db = open();
            await using var transaction = await db.Database.BeginTransactionAsync(stop);
            try
            {
                foreach (var entry in entries) await InsertEntryAsync(db, root, entry);
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

    // Runs one raw write, with triggers off (so it also keeps the projection the triggers would), just before the next commit lock of the armed actor's checkpoint: after the
    // owner read its Park facts and before its commit binds them.
    private sealed class RaceHook(string connection) : DbCommandInterceptor
    {
        private (string Actor, string Sql)? _armed;
        private int _fired;
        internal int Fired => Volatile.Read(ref _fired);
        internal void Arm(string actor, string sql) => _armed = (actor, sql);
        private (string Actor, long Until)? _hold;
        private int _held;
        internal int Held => Volatile.Read(ref _held);
        // Holds the next Park facts read for the actor until database time reaches the given time. It writes nothing.
        internal void Hold(string actor, long until) => _hold = (actor, until);
        private int _refused;
        // Commands of the set that a trigger refused: a commit lock refuses before any write, so it adds none.
        internal int TriggerRefusals => Volatile.Read(ref _refused);

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Exception is PostgresException { SqlState: "23514" }) Interlocked.Increment(ref _refused);
            return Task.CompletedTask;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (_hold is { } hold && command.CommandText.Contains("""AS "Outstanding" """.TrimEnd(), StringComparison.Ordinal) &&
                command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, hold.Actor)))
            {
                _hold = null;
                Interlocked.Increment(ref _held);
                await using var clock = new NpgsqlConnection(connection);
                await clock.OpenAsync(cancellationToken);
                await using var now = new NpgsqlCommand("SELECT floor(extract(epoch from clock_timestamp()) * 1000)::bigint", clock);
                while ((long)(await now.ExecuteScalarAsync(cancellationToken))! <= hold.Until) await Task.Delay(50, cancellationToken);
            }
            if (_armed is { } armed && command.CommandText.Contains("FOR UPDATE OF i", StringComparison.Ordinal) &&
                command.CommandText.Contains("""AS "StateDigest" """.TrimEnd(), StringComparison.Ordinal) &&
                command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, armed.Actor)))
            {
                _armed = null;
                Interlocked.Increment(ref _fired);
                await using var raw = new NpgsqlConnection(connection);
                await raw.OpenAsync(cancellationToken);
                await using var write = new NpgsqlCommand("SET session_replication_role = replica; " + armed.Sql, raw);
                if (await write.ExecuteNonQueryAsync(cancellationToken) < 1) throw new InvalidOperationException("The race write changed no row.");
            }
            return result;
        }
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
        public HashSet<string> Protects { get; } = [];
        // A root whose protection cannot be read: ProtectsDeletion throws, which the adapter answers as ProtectionUnavailable.
        public HashSet<string> Unreadable { get; } = [];
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) =>
            Unreadable.Contains(header.Ref.Id) ? throw new InvalidOperationException("The fixture's protection cannot be read.") : Protects.Contains(header.Ref.Id);
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
