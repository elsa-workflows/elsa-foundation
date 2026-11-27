using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Kernel.Native.Run;

// S1 (#4229): every generated-root profile is one KernelDb with a capability set. No database is opened.
internal static class ProfileCapabilityChecks
{
    internal static int Run()
    {
        // A connection string that is never opened: building a model and resolving services needs the provider only.
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql("Host=profile-capabilities.invalid").Options;
        // Each factory and the closed set its old subclass chain implied, written out so that no edit widens a profile silently.
        (string Name, Func<KernelDb> Open, StoreFormat Format, RootCapabilities Expected)[] profiles =
        [
            ("ordinary", () => new KernelDb(options), StoreFormat.Current,
                RootCapabilities.None),
            ("root-self", () => KernelDb.ForPostgresRootSelf(options), StoreFormat.RootSelf,
                RootCapabilities.SelfProtocol),
            ("root-timers", () => KernelDb.ForPostgresRootTimers(options), StoreFormat.RootTimers,
                RootCapabilities.SelfProtocol | RootCapabilities.Timers),
            ("root-operators", () => KernelDb.ForPostgresRootOperators(options), StoreFormat.RootOperators,
                RootCapabilities.OperatorProtocol),
            ("root-jobs", () => KernelDb.ForPostgresRootJobs(options), StoreFormat.RootJobs,
                RootCapabilities.JobProtocol | RootCapabilities.JobPreview),
            ("root-timed-job-issuance", () => KernelDb.ForPostgresRootTimedJobIssuance(options), StoreFormat.RootTimedJobIssuance,
                RootCapabilities.JobProtocol | RootCapabilities.TimedJobIssuance),
            ("root-values", () => KernelDb.ForPostgresRootValues(options), StoreFormat.RootValues,
                RootCapabilities.ValueProtocol),
            ("root-timed-job-settlement", () => KernelDb.ForPostgresRootTimedJobSettlement(options), StoreFormat.RootTimedJobSettlement,
                RootCapabilities.JobProtocol | RootCapabilities.TimedJobSettlement),
            ("root-replies", () => KernelDb.ForPostgresRootReplies(options), StoreFormat.RootReplies,
                RootCapabilities.ReplyProtocol),
            ("root-terminations", () => KernelDb.ForPostgresRootTerminations(options), StoreFormat.RootTerminations,
                RootCapabilities.OperatorProtocol | RootCapabilities.Termination),
            ("root-stable-body", () => KernelDb.ForPostgresRootStableBody(options), StoreFormat.RootStableBody,
                RootCapabilities.OperatorProtocol | RootCapabilities.Termination | RootCapabilities.StableBody),
            ("root-terminal-selection", () => KernelDb.ForPostgresRootTerminalSelection(options), StoreFormat.RootTerminalSelection,
                RootCapabilities.OperatorProtocol | RootCapabilities.Termination | RootCapabilities.StableBody | RootCapabilities.TerminalSelection),
            ("root-retirement", () => KernelDb.ForPostgresRootRetirement(options), StoreFormat.RootRetirement,
                RootCapabilities.OperatorProtocol | RootCapabilities.Termination | RootCapabilities.StableBody | RootCapabilities.TerminalSelection | RootCapabilities.Retirement),
            ("root-terminate-then-delete", () => KernelDb.ForPostgresRootTerminateThenDelete(options), StoreFormat.RootTerminateThenDelete,
                RootCapabilities.OperatorProtocol | RootCapabilities.Termination | RootCapabilities.StableBody | RootCapabilities.TerminalSelection | RootCapabilities.Retirement | RootCapabilities.TerminateThenDelete),
            ("root-retention", () => KernelDb.ForPostgresRootRetention(options), StoreFormat.RootRetention,
                RootCapabilities.OperatorProtocol | RootCapabilities.Termination | RootCapabilities.StableBody | RootCapabilities.TerminalSelection | RootCapabilities.Retirement | RootCapabilities.TerminateThenDelete | RootCapabilities.Retention),
            ("root-operator-replies", () => KernelDb.ForPostgresRootOperatorReplies(options), StoreFormat.RootOperatorReplies,
                RootCapabilities.OperatorProtocol | RootCapabilities.Termination | RootCapabilities.StableBody | RootCapabilities.TerminalSelection | RootCapabilities.Retirement | RootCapabilities.TerminateThenDelete | RootCapabilities.Retention | RootCapabilities.OperatorReplies),
            ("root-timed-job-preview", () => KernelDb.ForPostgresRootTimedJobPreview(options), StoreFormat.RootTimedJobPreview,
                RootCapabilities.JobProtocol | RootCapabilities.TimedJobSettlement | RootCapabilities.TimedJobPreview),
            ("root-park-opening", () => KernelDb.ForPostgresRootParkOpening(options), StoreFormat.RootParkOpening,
                RootCapabilities.ParkOpening),
            ("root-park-inspection", () => KernelDb.ForPostgresRootParkInspection(options), StoreFormat.RootParkInspection,
                RootCapabilities.ParkOpening | RootCapabilities.ParkInspection),
            ("root-park-arrivals", () => KernelDb.ForPostgresRootParkArrivals(options), StoreFormat.RootParkArrivals,
                RootCapabilities.ParkOpening | RootCapabilities.ParkInspection | RootCapabilities.ParkArrivals),
            ("root-park-recovery", () => KernelDb.ForPostgresRootParkRecovery(options), StoreFormat.RootParkRecovery,
                RootCapabilities.ParkOpening | RootCapabilities.ParkInspection | RootCapabilities.ParkArrivals | RootCapabilities.ParkRecovery | RootCapabilities.OperatorProtocol),
            // S7: the shipped format, the capabilities qualified together today.
            ("root-shipped", () => KernelDb.ForPostgresRootShipped(options), StoreFormat.RootShipped,
                RootCapabilities.OperatorProtocol | RootCapabilities.Termination | RootCapabilities.StableBody | RootCapabilities.TerminalSelection | RootCapabilities.Retirement | RootCapabilities.TerminateThenDelete | RootCapabilities.Retention | RootCapabilities.OperatorReplies |
                RootCapabilities.JobProtocol | RootCapabilities.TimedJobSettlement | RootCapabilities.TimedJobPreview | RootCapabilities.ReplyProtocol |
                RootCapabilities.ParkOpening | RootCapabilities.ParkInspection | RootCapabilities.ParkArrivals | RootCapabilities.ParkRecovery |
                RootCapabilities.SelfProtocol | RootCapabilities.Timers | RootCapabilities.TimerRearm | RootCapabilities.DueBacklog | RootCapabilities.TerminalOutput |
                RootCapabilities.Children | RootCapabilities.SequencedJournal | RootCapabilities.LiveEventWaits | RootCapabilities.RetentionPolicy |
                RootCapabilities.Playback | RootCapabilities.PlaybackDescendants | RootCapabilities.ScheduleInventory |
                RootCapabilities.ScheduleSettlement | RootCapabilities.ScheduleControls | RootCapabilities.ScheduleLineage | RootCapabilities.PreparedStarts)
        ];
        // The true accessors of each profile, taken from the pre-refactor subclass chains (UsesRootTimers is new),
        // so that a property bound to the wrong capability fails even when the capability set is right.
        var accessors = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ordinary"] = ["SupportsCorePreview", "SupportsRootWorkflowHistory"],
            ["root-self"] = ["SupportsRootWorkflowHistory", "UsesRootSelfProtocol"],
            ["root-timers"] = ["SupportsRootWorkflowHistory", "UsesRootSelfProtocol", "UsesRootTimers"],
            ["root-operators"] = ["SupportsRootWorkflowHistory", "UsesRootOperatorProtocol"],
            ["root-jobs"] = ["SupportsCorePreview", "SupportsRootWorkflowHistory", "UsesRootJobPreview", "UsesRootJobProtocol"],
            ["root-timed-job-issuance"] = ["SupportsRootWorkflowHistory", "UsesRootJobProtocol", "UsesRootTimedJobIssuance"],
            ["root-values"] = ["SupportsRootWorkflowHistory", "UsesRootValueProtocol"],
            ["root-timed-job-settlement"] = ["SupportsRootWorkflowHistory", "UsesRootJobProtocol", "UsesRootTimedJobSettlement"],
            ["root-replies"] = ["SupportsRootWorkflowHistory", "UsesRootReplyProtocol"],
            ["root-terminations"] = ["SupportsRootWorkflowHistory", "UsesRootOperatorProtocol", "UsesRootTermination"],
            ["root-stable-body"] = ["SupportsRootWorkflowHistory", "UsesRootOperatorProtocol", "UsesRootStableBody", "UsesRootTermination"],
            ["root-terminal-selection"] = ["UsesRootOperatorProtocol", "UsesRootStableBody", "UsesRootTerminalSelection", "UsesRootTermination"],
            ["root-retirement"] = ["RequiresPinnedRootNamespace", "UsesRootOperatorProtocol", "UsesRootRetirement", "UsesRootStableBody", "UsesRootTerminalSelection", "UsesRootTermination"],
            ["root-terminate-then-delete"] = ["RequiresPinnedRootNamespace", "UsesRootOperatorProtocol", "UsesRootRetirement", "UsesRootStableBody", "UsesRootTerminalSelection", "UsesRootTerminateThenDelete", "UsesRootTermination"],
            ["root-retention"] = ["RequiresPinnedRootNamespace", "UsesRootOperatorProtocol", "UsesRootRetention", "UsesRootRetirement", "UsesRootStableBody", "UsesRootTerminalSelection", "UsesRootTerminateThenDelete", "UsesRootTermination"],
            ["root-operator-replies"] = ["RequiresPinnedRootNamespace", "UsesRootOperatorProtocol", "UsesRootOperatorReplies", "UsesRootRetention", "UsesRootRetirement", "UsesRootStableBody", "UsesRootTerminalSelection", "UsesRootTerminateThenDelete", "UsesRootTermination"],
            ["root-timed-job-preview"] = ["SupportsCorePreview", "SupportsRootWorkflowHistory", "UsesRootJobProtocol", "UsesRootTimedJobPreview", "UsesRootTimedJobSettlement"],
            ["root-park-opening"] = ["RequiresPinnedRootNamespace", "SupportsRootWorkflowHistory", "UsesRootParkOpening"],
            ["root-park-inspection"] = ["RequiresPinnedRootNamespace", "SupportsRootWorkflowHistory", "UsesRootParkInspection", "UsesRootParkOpening"],
            ["root-park-arrivals"] = ["RequiresPinnedRootNamespace", "SupportsRootWorkflowHistory", "UsesRootParkArrivals", "UsesRootParkInspection", "UsesRootParkOpening"],
            ["root-park-recovery"] = ["RequiresPinnedRootNamespace", "SupportsRootWorkflowHistory", "UsesRootOperatorProtocol", "UsesRootParkArrivals", "UsesRootParkInspection", "UsesRootParkOpening", "UsesRootParkRecovery"],
            ["root-shipped"] = ["RequiresPinnedRootNamespace", "SupportsCorePreview", "SupportsRootWorkflowHistory", "UsesRootChildren", "UsesRootComposedParkRecovery", "UsesRootDueBacklog", "UsesRootJobProtocol", "UsesRootLiveEventWaits",
                "UsesRootOperatorProtocol", "UsesRootOperatorReplies", "UsesRootParkArrivals", "UsesRootParkInspection", "UsesRootParkOpening",
                "UsesRootParkRecovery", "UsesRootParkedOperators", "UsesRootPlayback", "UsesRootPlaybackDescendants", "UsesRootPreparedStarts",
                "UsesRootPrivateParkInputs", "UsesRootReplyProtocol", "UsesRootRetention", "UsesRootRetentionPolicy", "UsesRootRetirement",
                "UsesRootScheduleControls", "UsesRootScheduleInventory", "UsesRootScheduleLineage", "UsesRootScheduleSettlement",
                "UsesRootSelfProtocol", "UsesRootSequencedJournal", "UsesRootStableBody", "UsesRootTerminalOutput", "UsesRootTerminalSelection", "UsesRootTerminateThenDelete", "UsesRootTermination",
                "UsesRootTimedJobPreview", "UsesRootTimedJobSettlement", "UsesRootTimerRearm", "UsesRootTimers"],
        };
        // S3: each profile's root state family, the prefix bytes written before S3 and the statuses besides Finished that end it.
        const string family = "!kernel-state/";
        var families = new Dictionary<string, (string Prefix, bool Cancelled, bool Faulted)>(StringComparer.Ordinal)
        {
            ["ordinary"] = (family + "4:public-root:", false, false),
            ["root-self"] = (family + "4:public-root:", false, false),
            ["root-timers"] = (family + "4:public-root:", false, false),
            ["root-operators"] = (family + "4:public-root:", false, false),
            ["root-jobs"] = (family + "4:public-root:", false, false),
            ["root-timed-job-issuance"] = (family + "4:public-root:", false, false),
            ["root-values"] = (family + "5:public-root-values:", false, false),
            ["root-timed-job-settlement"] = (family + "6:public-root-timed-jobs:", false, true),
            ["root-replies"] = (family + "7:public-root-replies:", false, false),
            ["root-terminations"] = (family + "11:public-root-termination:", true, false),
            ["root-stable-body"] = (family + "12:public-root-stable-body:", true, false),
            ["root-terminal-selection"] = (family + "12:public-root-stable-body:", true, false),
            ["root-retirement"] = (family + "12:public-root-stable-body:", true, false),
            ["root-terminate-then-delete"] = (family + "12:public-root-stable-body:", true, false),
            ["root-retention"] = (family + "12:public-root-stable-body:", true, false),
            ["root-operator-replies"] = (family + "12:public-root-stable-body:", true, false),
            ["root-timed-job-preview"] = (family + "6:public-root-timed-jobs:", false, true),
            ["root-park-opening"] = (family + "4:public-root:", false, false),
            ["root-park-inspection"] = (family + "4:public-root:", false, false),
            ["root-park-arrivals"] = (family + "4:public-root:", false, false),
            ["root-park-recovery"] = (family + "4:public-root:", false, false),
            ["root-shipped"] = (family + "13:public-root-composed:timed-jobs+stable-body+replies:", true, true),
        };
        var flags = typeof(KernelDb).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(p => p.PropertyType == typeof(bool) && (p.Name.StartsWith("Uses", StringComparison.Ordinal) ||
                p.Name.StartsWith("Supports", StringComparison.Ordinal) || p.Name.StartsWith("Requires", StringComparison.Ordinal)))
            .OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        var failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok" : "FAILED")} {what}");
            if (!ok) failures++;
        }
        var keys = new List<(string Name, object Key)>();
        foreach (var (name, open, format, expected) in profiles)
        {
            using var db = open();
            var set = flags.Where(p => (bool)p.GetValue(db)!).Select(p => p.Name);
            var model = db.GetService<IDesignTimeModel>().Model;
            var seeds = model.FindEntityType(typeof(StoreVersionEntity))!.GetSeedData().ToArray();
            var marker = seeds.Single(row => Equals(row["Id"], StoreVersionEntity.MarkerId))["MessageProtocol"];
            var shipped = format == StoreFormat.RootShipped;
            Check(seeds.Length == (shipped ? 2 : 1) &&
                seeds.Any(row => Equals(row["Id"], StoreDefinitionsRevision.MarkerId)) == shipped,
                $"{name} seeds exactly the markers it requires");
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(model.ToDebugString(MetadataDebugStringOptions.LongDefault))))[..16];
            Console.WriteLine($"profile {name} protocol={db.RequiredFormat.MessageProtocol} marker={marker} timers={db.UsesRootTimers} model={fingerprint} uses=[{string.Join(",", set)}]");
            Check(db.GetType() == typeof(KernelDb), $"{name} is a plain KernelDb");
            Check(db.RequiredFormat == format, $"{name} carries format {format.MessageProtocol}");
            Check(db.Capabilities == expected, $"{name} carries exactly {expected}");
            Check(RootProfiles.Formats[format] == expected, $"{name} mapping for protocol {format.MessageProtocol} is {expected}");
            Check(Equals(marker, format.MessageProtocol), $"{name} model seeds marker protocol {format.MessageProtocol}");
            Check(db.UsesRootTimers == (format == StoreFormat.RootTimers || shipped), $"{name} timer selection matches its format");
            Check(set.SequenceEqual(accessors[name]), $"{name} turns on exactly [{string.Join(",", accessors[name])}]");
            var (prefix, cancelled, faulted) = families[name];
            Check(db.RootStateFamily == new RootStateFamily(prefix, cancelled, faulted),
                $"{name} keeps state family {prefix} (Cancelled ends={cancelled}, Faulted ends={faulted})");
            var factory = db.GetService<IModelCacheKeyFactory>();
            Check(factory is KernelModelCacheKeyFactory, $"{name} resolves the kernel model cache key factory");
            var key = factory.Create(db, designTime: false);
            using (var again = open()) Check(Equals(key, factory.Create(again, designTime: false)), $"{name} gets the same cache key twice");
            Check(!Equals(key, factory.Create(db, designTime: true)), $"{name} design-time and runtime cache keys differ");
            keys.Add((name, key));
        }
        Check(RootProfiles.Formats.Count == profiles.Length, $"the mapping has {profiles.Length} formats, one per factory");
        Check(accessors.Count == profiles.Length, $"every profile has its expected accessors");
        Check(families.Count == profiles.Length, "every profile has its expected state family");
        var mapped = RootProfiles.Formats.Values.Aggregate(RootCapabilities.None, (all, next) => all | next);
        using var backlog = KernelDb.ForRootDueBacklogQualification(options);
        Check(backlog.RequiredFormat == StoreFormat.RootDueBacklogQualification && backlog.UsesRootDueBacklog &&
            backlog.Capabilities == (RootProfiles.ParkRecoveryComposition | RootCapabilities.DueBacklog) &&
            !RootProfiles.Formats.ContainsKey(backlog.RequiredFormat),
            "the earlier private pressure format retains its original capabilities");
        using var children = KernelDb.ForPostgresRootChildrenQualification(options);
        Check(children.RequiredFormat == StoreFormat.PostgresRootChildrenQualification &&
            children.RequiredFormat is { StoreSchema: 3, MessageProtocol: 111, JournalCodec: 1 } &&
            StoreDefinitionsRevision.Seed(children) is { StoreSchema: 1, MessageProtocol: 10, JournalCodec: 0 } &&
            children.UsesRootChildren && !children.UsesRootDueBacklog &&
            children.Capabilities == (RootProfiles.TimerRearmQualification | RootCapabilities.Children) &&
            !RootProfiles.Formats.ContainsKey(children.RequiredFormat),
            "the private PostgreSQL child profile keeps its full format, definition and capability sets");
        var sqliteOptions = new DbContextOptionsBuilder<KernelDb>().UseSqlite("Data Source=:memory:").Options;
        using var sqliteBacklog = KernelDb.ForRootDueBacklogQualification(sqliteOptions, new SqliteAdmissionDefinitions());
        Check(StoreDefinitionsRevision.Seed(backlog) is { StoreSchema: 1, MessageProtocol: 5, JournalCodec: 0 } &&
              StoreDefinitionsRevision.Seed(sqliteBacklog) is { StoreSchema: 2, MessageProtocol: 10, JournalCodec: 0 },
            "the due-backlog qualification retains its installed PostgreSQL and SQLite definitions");
        using var terminalOutput = KernelDb.ForRootTerminalOutputQualification(options);
        Check(terminalOutput.RequiredFormat == new StoreFormat(3, 112, 1) && terminalOutput.UsesRootTerminalOutput &&
            terminalOutput.Capabilities == (RootCapabilities.OperatorProtocol | RootCapabilities.Termination | RootCapabilities.StableBody |
                RootCapabilities.TerminalSelection | RootCapabilities.Retirement | RootCapabilities.TerminateThenDelete | RootCapabilities.Retention |
                RootCapabilities.OperatorReplies | RootCapabilities.JobProtocol | RootCapabilities.TimedJobSettlement | RootCapabilities.TimedJobPreview |
                RootCapabilities.ReplyProtocol | RootCapabilities.ParkOpening | RootCapabilities.ParkInspection | RootCapabilities.ParkArrivals |
                RootCapabilities.ParkRecovery | RootCapabilities.TerminalOutput) &&
            RootProfiles.MissingPrerequisites(terminalOutput.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(terminalOutput.RequiredFormat),
            "protocol 112 retains its original private terminal-output capabilities");
        using var sqliteTerminalOutput = KernelDb.ForRootTerminalOutputQualification(sqliteOptions, sqliteShippedInstall: true);
        Check(StoreDefinitionsRevision.Seed(terminalOutput) is { StoreSchema: 1, MessageProtocol: 6, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(sqliteTerminalOutput) is { StoreSchema: 2, MessageProtocol: 10, JournalCodec: 0 },
            "protocol 112 retains its original PostgreSQL and SQLite definition stamps");
        using var s6TerminalOutput = KernelDb.ForRootTerminalOutputQualification(options, format: StoreFormat.RootS6TerminalOutputQualification);
        using var sqliteS6TerminalOutput = KernelDb.ForRootTerminalOutputQualification(sqliteOptions, sqliteShippedInstall: true,
            format: StoreFormat.RootS6TerminalOutputQualification);
        Check(s6TerminalOutput.RequiredFormat == new StoreFormat(3, 113, 1) &&
            s6TerminalOutput.Capabilities == (terminalOutput.Capabilities | RootCapabilities.Timers | RootCapabilities.SelfProtocol | RootCapabilities.TimerRearm) &&
            RootProfiles.MissingPrerequisites(s6TerminalOutput.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(s6TerminalOutput.RequiredFormat) &&
            StoreDefinitionsRevision.Seed(s6TerminalOutput) is { StoreSchema: 1, MessageProtocol: 7, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(sqliteS6TerminalOutput) is { StoreSchema: 2, MessageProtocol: 11, JournalCodec: 0 },
            "protocol 113 qualifies terminal output alongside the S6 timers with separate definition stamps");
        using var sharedPostgres = KernelDb.ForPostgresRootShipped(options);
        using var sharedSqlite = KernelDb.ForSqliteRootShipped(sqliteOptions);
        Check(sharedPostgres.RequiredFormat == StoreFormat.RootShipped && sharedSqlite.RequiredFormat == sharedPostgres.RequiredFormat &&
            sharedPostgres.Capabilities == (s6TerminalOutput.Capabilities | RootCapabilities.DueBacklog | RootCapabilities.Children |
                RootCapabilities.SequencedJournal | RootCapabilities.LiveEventWaits | RootCapabilities.RetentionPolicy |
                RootCapabilities.Playback | RootCapabilities.PlaybackDescendants | RootCapabilities.ScheduleInventory |
                RootCapabilities.ScheduleSettlement | RootCapabilities.ScheduleControls | RootCapabilities.ScheduleLineage | RootCapabilities.PreparedStarts) &&
            sharedSqlite.Capabilities == sharedPostgres.Capabilities &&
            StoreDefinitionsRevision.Seed(sharedPostgres) is { StoreSchema: 1, MessageProtocol: 20, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(sharedSqlite) is { StoreSchema: 2, MessageProtocol: 23, JournalCodec: 0 },
            "the shared shipped format composes managed schedules and prepared starts with the previous provider capabilities");
        using var sequencedPostgres = KernelDb.ForPostgresRootSequencedJournalQualification(options);
        using var sequencedSqlite = KernelDb.ForSqliteRootRuntime(sqliteOptions, new SqliteAdmissionDefinitions(),
            StoreFormat.RootSequencedJournalQualification);
        Check(sequencedPostgres.RequiredFormat == new StoreFormat(3, 115, 1) &&
            sequencedSqlite.RequiredFormat == sequencedPostgres.RequiredFormat &&
            sequencedPostgres.Capabilities == (s6TerminalOutput.Capabilities | RootCapabilities.DueBacklog | RootCapabilities.SequencedJournal) &&
            sequencedSqlite.Capabilities == sequencedPostgres.Capabilities &&
            sequencedPostgres.UsesRootSequencedJournal && sequencedSqlite.UsesRootSequencedJournal &&
            RootProfiles.MissingPrerequisites(sequencedPostgres.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(sequencedPostgres.RequiredFormat) &&
            StoreDefinitionsRevision.Seed(sequencedPostgres) is { StoreSchema: 1, MessageProtocol: 10, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(sequencedSqlite) is { StoreSchema: 2, MessageProtocol: 14, JournalCodec: 0 },
            "protocol 115 privately retains the protocol-29 capabilities and distinct PostgreSQL and SQLite journal definitions");
        using var liveEventsPostgres = KernelDb.ForPostgresRootLiveEventQualification(options);
        using var liveEventsSqlite = KernelDb.ForSqliteRootRuntime(sqliteOptions, new SqliteAdmissionDefinitions(), StoreFormat.RootLiveEventQualification);
        Check(liveEventsPostgres.RequiredFormat != StoreFormat.RootShipped && liveEventsSqlite.RequiredFormat == liveEventsPostgres.RequiredFormat &&
            liveEventsPostgres.Capabilities == (sequencedPostgres.Capabilities | RootCapabilities.Children | RootCapabilities.LiveEventWaits) &&
            liveEventsSqlite.Capabilities == liveEventsPostgres.Capabilities &&
            StoreDefinitionsRevision.Seed(liveEventsPostgres) is { StoreSchema: 1, MessageProtocol: 11, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(liveEventsSqlite) is { StoreSchema: 2, MessageProtocol: 14, JournalCodec: 0 } &&
            RootProfiles.MissingPrerequisites(liveEventsPostgres.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(liveEventsPostgres.RequiredFormat),
            "the private live-event profile retains its isolated format and the qualified capability set");
        using var playbackPostgres = KernelDb.ForPostgresRootPlaybackQualification(options);
        using var playbackSqlite = KernelDb.ForSqliteRootRuntime(sqliteOptions, new SqliteAdmissionDefinitions(), StoreFormat.RootPlaybackQualification);
        Check(playbackPostgres.RequiredFormat == new StoreFormat(3, 116, 1) && playbackSqlite.RequiredFormat == playbackPostgres.RequiredFormat &&
            playbackPostgres.Capabilities == (sequencedPostgres.Capabilities | RootCapabilities.Children | RootCapabilities.Playback) &&
            playbackSqlite.Capabilities == playbackPostgres.Capabilities &&
            !playbackPostgres.UsesRootLiveEventWaits && !playbackSqlite.UsesRootLiveEventWaits &&
            !playbackPostgres.UsesRootPlaybackDescendants && !playbackSqlite.UsesRootPlaybackDescendants &&
            RootProfiles.MissingPrerequisites(playbackPostgres.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(playbackPostgres.RequiredFormat) &&
            StoreDefinitionsRevision.Seed(playbackPostgres) is { StoreSchema: 1, MessageProtocol: 12, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(playbackSqlite) is { StoreSchema: 2, MessageProtocol: 15, JournalCodec: 0 },
            "private playback retains exactly protocol 31 plus indexed occurrences and its own provider definitions");
        using var descendantsPostgres = KernelDb.ForPostgresRootPlaybackDescendantsQualification(options);
        using var descendantsSqlite = KernelDb.ForSqliteRootRuntime(sqliteOptions, new SqliteAdmissionDefinitions(), StoreFormat.RootPlaybackDescendantsQualification);
        Check(descendantsPostgres.RequiredFormat == new StoreFormat(3, 118, 1) && descendantsSqlite.RequiredFormat == descendantsPostgres.RequiredFormat &&
            descendantsPostgres.Capabilities == (playbackPostgres.Capabilities | RootCapabilities.LiveEventWaits |
                RootCapabilities.RetentionPolicy | RootCapabilities.PlaybackDescendants) &&
            descendantsSqlite.Capabilities == descendantsPostgres.Capabilities &&
            RootProfiles.MissingPrerequisites(descendantsPostgres.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(descendantsPostgres.RequiredFormat) &&
            StoreDefinitionsRevision.Seed(descendantsPostgres) is { StoreSchema: 1, MessageProtocol: 14, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(descendantsSqlite) is { StoreSchema: 2, MessageProtocol: 17, JournalCodec: 0 },
            "private descendant Playback composes protocol 33, indexed scopes and independent provider definitions");
        using var schedulePostgres = KernelDb.ForPostgresRootScheduleInventoryQualification(options);
        using var scheduleSqlite = KernelDb.ForSqliteRootRuntime(sqliteOptions, new SqliteAdmissionDefinitions(), StoreFormat.RootScheduleInventoryQualification);
        Check(schedulePostgres.RequiredFormat == new StoreFormat(3, 119, 1) && scheduleSqlite.RequiredFormat == schedulePostgres.RequiredFormat &&
            schedulePostgres.UsesRootScheduleInventory && scheduleSqlite.Capabilities == schedulePostgres.Capabilities &&
            RootProfiles.MissingPrerequisites(schedulePostgres.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(schedulePostgres.RequiredFormat) &&
            StoreDefinitionsRevision.Seed(schedulePostgres) is { StoreSchema: 1, MessageProtocol: 15, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(scheduleSqlite) is { StoreSchema: 2, MessageProtocol: 18, JournalCodec: 0 },
            "the schedule inventory uses a private protocol with its own definitions on both providers");
        using var settlementPostgres = KernelDb.ForPostgresRootScheduleSettlementQualification(options);
        using var settlementSqlite = KernelDb.ForSqliteRootRuntime(sqliteOptions, new SqliteAdmissionDefinitions(), StoreFormat.RootScheduleSettlementQualification);
        Check(settlementPostgres.RequiredFormat == new StoreFormat(3, 120, 1) && settlementSqlite.RequiredFormat == settlementPostgres.RequiredFormat &&
            settlementPostgres.Capabilities == (schedulePostgres.Capabilities | RootCapabilities.ScheduleSettlement) &&
            settlementSqlite.Capabilities == settlementPostgres.Capabilities &&
            !schedulePostgres.UsesRootScheduleSettlement &&
            RootProfiles.MissingPrerequisites(settlementPostgres.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(settlementPostgres.RequiredFormat),
            "scheduled occurrence settlement has a private protocol and retains the installed inventory definitions");
        using var controlsPostgres = KernelDb.ForPostgresRootScheduleControlsQualification(options);
        using var controlsSqlite = KernelDb.ForSqliteRootRuntime(sqliteOptions, new SqliteAdmissionDefinitions(), StoreFormat.RootScheduleControlsQualification);
        Check(controlsPostgres.RequiredFormat == new StoreFormat(3, 121, 1) && controlsSqlite.RequiredFormat == controlsPostgres.RequiredFormat &&
            controlsPostgres.Capabilities == (settlementPostgres.Capabilities | RootCapabilities.ScheduleControls) &&
            controlsSqlite.Capabilities == controlsPostgres.Capabilities &&
            !schedulePostgres.UsesRootScheduleControls && !settlementPostgres.UsesRootScheduleControls &&
            RootProfiles.MissingPrerequisites(controlsPostgres.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(controlsPostgres.RequiredFormat) &&
            StoreDefinitionsRevision.Seed(controlsPostgres) is { StoreSchema: 1, MessageProtocol: 16, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(controlsSqlite) is { StoreSchema: 2, MessageProtocol: 19, JournalCodec: 0 },
            "trusted schedule controls are private with independently verified provider definitions");

        using var lineagePostgres = KernelDb.ForPostgresRootScheduleLineageQualification(options);
        using var lineageSqlite = KernelDb.ForSqliteRootRuntime(sqliteOptions, new SqliteAdmissionDefinitions(), StoreFormat.RootScheduleLineageQualification);
        Check(lineagePostgres.RequiredFormat == new StoreFormat(3, 122, 1) && lineageSqlite.RequiredFormat == lineagePostgres.RequiredFormat &&
            lineagePostgres.Capabilities == (settlementPostgres.Capabilities | RootCapabilities.ScheduleLineage) &&
            lineageSqlite.Capabilities == lineagePostgres.Capabilities &&
            !settlementPostgres.UsesRootScheduleLineage &&
            !lineagePostgres.UsesRootScheduleControls && !controlsPostgres.UsesRootScheduleLineage &&
            RootProfiles.MissingPrerequisites(lineagePostgres.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(lineagePostgres.RequiredFormat) &&
            StoreDefinitionsRevision.Seed(lineagePostgres) is { StoreSchema: 1, MessageProtocol: 17, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(lineageSqlite) is { StoreSchema: 2, MessageProtocol: 20, JournalCodec: 0 },
            "the occurrence lineage has a closed private profile and provider-specific definitions");
        using var authoredPostgres = KernelDb.ForPostgresRootAuthoredEventsQualification(options);
        using var authoredSqlite = KernelDb.ForSqliteRootRuntime(sqliteOptions, new SqliteAdmissionDefinitions(), StoreFormat.RootAuthoredEventsQualification);
        Check(authoredPostgres.RequiredFormat == new StoreFormat(3, 126, 1) && authoredSqlite.RequiredFormat == authoredPostgres.RequiredFormat &&
            authoredPostgres.Capabilities == (RootProfiles.PreparedStartsQualification | RootCapabilities.AuthoredEvents) &&
            authoredSqlite.Capabilities == authoredPostgres.Capabilities &&
            authoredPostgres.UsesRootAuthoredEvents && authoredSqlite.UsesRootAuthoredEvents &&
            !sharedPostgres.UsesRootAuthoredEvents && !sharedSqlite.UsesRootAuthoredEvents &&
            RootScheduleOwnerMode.Applies(authoredPostgres) && RootScheduleOwnerMode.Applies(authoredSqlite) &&
            RootProfiles.MissingPrerequisites(authoredPostgres.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(authoredPostgres.RequiredFormat) &&
            StoreDefinitionsRevision.Seed(authoredPostgres) is { StoreSchema: 1, MessageProtocol: 20, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(authoredSqlite) is { StoreSchema: 2, MessageProtocol: 23, JournalCodec: 0 },
            "private authored events only widen the protocol-35 wire and write path on both physical providers");
        using var startEventsPostgres = KernelDb.ForPostgresRootEventStartsQualification(options);
        using var startEventsSqlite = KernelDb.ForSqliteRootRuntime(sqliteOptions, new SqliteAdmissionDefinitions(), StoreFormat.RootEventStartsQualification);
        Check(startEventsPostgres.RequiredFormat == new StoreFormat(3, 127, 1) &&
            startEventsSqlite.RequiredFormat == startEventsPostgres.RequiredFormat &&
            startEventsPostgres.Capabilities == (authoredPostgres.Capabilities | RootCapabilities.EventStarts) &&
            startEventsSqlite.Capabilities == startEventsPostgres.Capabilities &&
            startEventsPostgres.UsesRootEventStarts && startEventsSqlite.UsesRootEventStarts &&
            !authoredPostgres.UsesRootEventStarts && !authoredSqlite.UsesRootEventStarts &&
            RootProfiles.MissingPrerequisites(startEventsPostgres.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(startEventsPostgres.RequiredFormat) &&
            StoreDefinitionsRevision.Seed(startEventsPostgres) is { StoreSchema: 1, MessageProtocol: 23, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(startEventsSqlite) is { StoreSchema: 2, MessageProtocol: 26, JournalCodec: 0 },
            "private event starts add exact registration and settlement definitions without changing earlier formats");
        using var receiptsPostgres = KernelDb.ForPostgresRootEventStartReceiptsQualification(options);
        using var receiptsSqlite = KernelDb.ForSqliteRootRuntime(sqliteOptions, new SqliteAdmissionDefinitions(), StoreFormat.RootEventStartReceiptsQualification);
        Check(receiptsPostgres.RequiredFormat == new StoreFormat(3, 128, 1) && receiptsSqlite.RequiredFormat == receiptsPostgres.RequiredFormat &&
            receiptsPostgres.Capabilities == (startEventsPostgres.Capabilities | RootCapabilities.EventStartReceipts) &&
            receiptsSqlite.Capabilities == receiptsPostgres.Capabilities &&
            !startEventsPostgres.UsesRootEventStartReceipts && !startEventsSqlite.UsesRootEventStartReceipts &&
            !sharedPostgres.UsesRootEventStartReceipts && !sharedSqlite.UsesRootEventStartReceipts &&
            RootProfiles.MissingPrerequisites(receiptsPostgres.Capabilities) == RootCapabilities.None &&
            !RootProfiles.Formats.ContainsKey(receiptsPostgres.RequiredFormat) &&
            StoreDefinitionsRevision.Seed(receiptsPostgres) is { StoreSchema: 1, MessageProtocol: 24, JournalCodec: 0 } &&
            StoreDefinitionsRevision.Seed(receiptsSqlite) is { StoreSchema: 2, MessageProtocol: 27, JournalCodec: 0 },
            "bounded Event receipt retention has a distinct private profile and exact provider definitions");
        mapped |= backlog.Capabilities | terminalOutput.Capabilities | children.Capabilities | sequencedPostgres.Capabilities |
            playbackPostgres.Capabilities | descendantsPostgres.Capabilities | controlsPostgres.Capabilities | lineagePostgres.Capabilities |
            authoredPostgres.Capabilities | startEventsPostgres.Capabilities | receiptsPostgres.Capabilities;
        foreach (var capability in Enum.GetValues<RootCapabilities>().Where(c => c != RootCapabilities.None))
            Check(mapped.HasFlag(capability), $"capability {capability} is carried by at least one format");
        // S3 r2: every shipped set is closed under the prerequisite map, and each prerequisite is one that every shipped
        // carrier of its capability holds.
        foreach (var (name, _, format, _) in profiles)
            Check(RootProfiles.MissingPrerequisites(RootProfiles.Formats[format]) == RootCapabilities.None,
                $"{name} is closed under the prerequisite map");
        foreach (var (capability, needs) in RootProfiles.Prerequisites)
            Check(needs != RootCapabilities.None && (needs & capability) == 0 &&
                RootProfiles.Formats.Values.Where(set => set.HasFlag(capability)).All(set => set.HasFlag(needs)),
                $"{capability} needs {needs}, which every shipped set with {capability} carries");
        // The edge set itself is pinned, so dropping or widening one prerequisite fails here even when every shipped set stays closed.
        var expectedEdges = new Dictionary<RootCapabilities, RootCapabilities>
        {
            [RootCapabilities.Timers] = RootCapabilities.SelfProtocol,
            [RootCapabilities.Termination] = RootCapabilities.OperatorProtocol,
            [RootCapabilities.StableBody] = RootCapabilities.Termination,
            [RootCapabilities.TerminalSelection] = RootCapabilities.StableBody,
            [RootCapabilities.Retirement] = RootCapabilities.TerminalSelection,
            [RootCapabilities.TerminateThenDelete] = RootCapabilities.Retirement,
            [RootCapabilities.Retention] = RootCapabilities.TerminateThenDelete,
            [RootCapabilities.OperatorReplies] = RootCapabilities.TerminalSelection,
            [RootCapabilities.JobPreview] = RootCapabilities.JobProtocol,
            [RootCapabilities.TimedJobIssuance] = RootCapabilities.JobProtocol,
            [RootCapabilities.TimedJobSettlement] = RootCapabilities.JobProtocol,
            [RootCapabilities.TimedJobPreview] = RootCapabilities.TimedJobSettlement,
            [RootCapabilities.ParkInspection] = RootCapabilities.ParkOpening,
            [RootCapabilities.ParkArrivals] = RootCapabilities.ParkInspection,
            [RootCapabilities.ParkRecovery] = RootCapabilities.ParkArrivals | RootCapabilities.OperatorProtocol,
            [RootCapabilities.TimerRearm] = RootProfiles.ParkCompositionBase | RootCapabilities.Timers | RootCapabilities.ParkRecovery,
            [RootCapabilities.TerminalOutput] = RootCapabilities.TerminalSelection | RootCapabilities.Retention,
            [RootCapabilities.SequencedJournal] = RootCapabilities.Retention,
            [RootCapabilities.LiveEventWaits] = RootCapabilities.ReplyProtocol | RootCapabilities.ParkRecovery,
            [RootCapabilities.AuthoredEvents] = RootCapabilities.LiveEventWaits | RootCapabilities.SelfProtocol,
            [RootCapabilities.EventStarts] = RootCapabilities.AuthoredEvents | RootCapabilities.PreparedStarts,
            [RootCapabilities.EventStartReceipts] = RootCapabilities.EventStarts,
            [RootCapabilities.Playback] = RootCapabilities.SequencedJournal,
            [RootCapabilities.PlaybackDescendants] = RootCapabilities.Playback,
            [RootCapabilities.RetentionPolicy] = RootCapabilities.Retention | RootCapabilities.TerminalSelection | RootCapabilities.ParkInspection,
            [RootCapabilities.ScheduleInventory] = RootCapabilities.Timers | RootCapabilities.ParkOpening,
            [RootCapabilities.ScheduleSettlement] = RootCapabilities.ScheduleInventory | RootCapabilities.JobProtocol,
            [RootCapabilities.ScheduleControls] = RootCapabilities.ScheduleInventory | RootCapabilities.TimedJobSettlement,
            [RootCapabilities.ScheduleLineage] = RootCapabilities.ScheduleSettlement | RootCapabilities.Children | RootCapabilities.Retirement
        };
        Check(RootProfiles.Prerequisites.Count == expectedEdges.Count &&
            expectedEdges.All(edge => RootProfiles.Prerequisites.TryGetValue(edge.Key, out var needs) && needs == edge.Value),
            $"the prerequisite map is exactly the {expectedEdges.Count} pinned edges");
        // An unshipped composition must be closed: stable body without termination would write a shipped prefix over the wire layout.
        var unshipped = StoreFormat.Current with { MessageProtocol = 96 };
        var standaloneRearm = RootProfiles.Formats[StoreFormat.RootParkRecovery] | RootProfiles.Formats[StoreFormat.RootTimers] |
            RootCapabilities.TimerRearm;
        foreach (var (composition, missing) in new (RootCapabilities Set, RootCapabilities Missing)[]
                 {
                     (RootCapabilities.StableBody, RootCapabilities.Termination),
                     (RootCapabilities.StableBody | RootCapabilities.Termination, RootCapabilities.OperatorProtocol),
                     (RootCapabilities.ParkInspection, RootCapabilities.ParkOpening),
                     (standaloneRearm, RootProfiles.ParkCompositionBase & ~standaloneRearm),
                     (RootCapabilities.TimedJobPreview | RootCapabilities.JobProtocol, RootCapabilities.TimedJobSettlement),
                     (RootProfiles.Formats[StoreFormat.RootStableBody] | RootCapabilities.OperatorReplies, RootCapabilities.TerminalSelection)
                 })
        {
            try
            {
                KernelDb.ForUnshippedComposition(options, unshipped, composition).Dispose();
                Check(false, $"an unclosed composition {composition} was refused");
            }
            catch (ArgumentException error) when (error is not ArgumentOutOfRangeException)
            {
                Check(RootProfiles.MissingPrerequisites(composition) == missing && error.Message.Contains(missing.ToString(), StringComparison.Ordinal),
                    $"an unclosed composition {composition} is refused for lacking {missing}");
            }
        }
        using (var closed = KernelDb.ForUnshippedComposition(options, unshipped,
                   RootProfiles.Formats[StoreFormat.RootTerminalSelection] | RootProfiles.Formats[StoreFormat.RootTimedJobPreview] |
                   RootCapabilities.ReplyProtocol | RootCapabilities.OperatorReplies))
            Check(closed.RootStateFamily.Prefix == "!kernel-state/13:public-root-composed:timed-jobs+stable-body+replies:",
                "a closed composition with held operator replies and no retention is accepted");
        for (var i = 0; i < keys.Count; i++)
            for (var j = i + 1; j < keys.Count; j++)
                if (Equals(keys[i].Key, keys[j].Key)) Check(false, $"{keys[i].Name} and {keys[j].Name} share a model cache key");
        Check(keys.Select(k => k.Key).Distinct().Count() == keys.Count, $"{keys.Count} profiles get {keys.Count} distinct model cache keys");
        return failures == 0 ? 0 : 1;
    }
}
