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
                RootCapabilities.ParkOpening | RootCapabilities.ParkInspection)
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
            ["root-shipped"] = ["RequiresPinnedRootNamespace", "SupportsCorePreview", "SupportsRootWorkflowHistory", "UsesRootJobProtocol", "UsesRootOperatorProtocol",
                "UsesRootOperatorReplies", "UsesRootParkInspection", "UsesRootParkOpening", "UsesRootParkedOperators", "UsesRootReplyProtocol", "UsesRootRetention",
                "UsesRootRetirement", "UsesRootStableBody", "UsesRootTerminalSelection", "UsesRootTerminateThenDelete", "UsesRootTermination",
                "UsesRootTimedJobPreview", "UsesRootTimedJobSettlement"],
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
            Check(seeds.Count(row => Equals(row["Id"], StoreDefinitionsRevision.MarkerId)) ==
                (format == StoreFormat.RootShipped ? 1 : 0), $"{name} seeds the definitions marker only for the shipped format");
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(model.ToDebugString(MetadataDebugStringOptions.LongDefault))))[..16];
            Console.WriteLine($"profile {name} protocol={db.RequiredFormat.MessageProtocol} marker={marker} timers={db.RequiredFormat == StoreFormat.RootTimers} model={fingerprint} uses=[{string.Join(",", set)}]");
            Check(db.GetType() == typeof(KernelDb), $"{name} is a plain KernelDb");
            Check(db.RequiredFormat == format, $"{name} carries format {format.MessageProtocol}");
            Check(db.Capabilities == expected, $"{name} carries exactly {expected}");
            Check(RootProfiles.Formats[format] == expected, $"{name} mapping for protocol {format.MessageProtocol} is {expected}");
            Check(Equals(marker, format.MessageProtocol), $"{name} model seeds marker protocol {format.MessageProtocol}");
            Check(db.UsesRootTimers == (format == StoreFormat.RootTimers), $"{name} timer selection matches its format");
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
            [RootCapabilities.ParkRecovery] = RootCapabilities.ParkArrivals | RootCapabilities.OperatorProtocol
        };
        Check(RootProfiles.Prerequisites.Count == expectedEdges.Count &&
            expectedEdges.All(edge => RootProfiles.Prerequisites.TryGetValue(edge.Key, out var needs) && needs == edge.Value),
            $"the prerequisite map is exactly the {expectedEdges.Count} pinned edges");
        // An unshipped composition must be closed: stable body without termination would write a shipped prefix over the wire layout.
        var unshipped = StoreFormat.Current with { MessageProtocol = 96 };
        foreach (var (composition, missing) in new (RootCapabilities Set, RootCapabilities Missing)[]
                 {
                     (RootCapabilities.StableBody, RootCapabilities.Termination),
                     (RootCapabilities.StableBody | RootCapabilities.Termination, RootCapabilities.OperatorProtocol),
                     (RootCapabilities.ParkInspection, RootCapabilities.ParkOpening),
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
