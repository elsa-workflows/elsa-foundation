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
                RootCapabilities.ParkOpening | RootCapabilities.ParkInspection | RootCapabilities.ParkArrivals | RootCapabilities.ParkRecovery | RootCapabilities.OperatorProtocol)
        ];
        // The true accessors of each profile, taken from the pre-refactor subclass chains (UsesRootTimers is new),
        // so that a property bound to the wrong capability fails even when the capability set is right.
        var accessors = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ordinary"] = ["SupportsCorePreview"],
            ["root-self"] = ["UsesRootSelfProtocol"],
            ["root-timers"] = ["UsesRootSelfProtocol", "UsesRootTimers"],
            ["root-operators"] = ["UsesRootOperatorProtocol"],
            ["root-jobs"] = ["SupportsCorePreview", "UsesRootJobPreview", "UsesRootJobProtocol"],
            ["root-timed-job-issuance"] = ["UsesRootJobProtocol", "UsesRootTimedJobIssuance"],
            ["root-values"] = ["UsesRootValueProtocol"],
            ["root-timed-job-settlement"] = ["UsesRootJobProtocol", "UsesRootTimedJobSettlement"],
            ["root-replies"] = ["UsesRootReplyProtocol"],
            ["root-terminations"] = ["UsesRootOperatorProtocol", "UsesRootTermination"],
            ["root-stable-body"] = ["UsesRootOperatorProtocol", "UsesRootStableBody", "UsesRootTermination"],
            ["root-terminal-selection"] = ["UsesRootOperatorProtocol", "UsesRootStableBody", "UsesRootTerminalSelection", "UsesRootTermination"],
            ["root-retirement"] = ["RequiresPinnedRootNamespace", "UsesRootOperatorProtocol", "UsesRootRetirement", "UsesRootStableBody", "UsesRootTerminalSelection", "UsesRootTermination"],
            ["root-terminate-then-delete"] = ["RequiresPinnedRootNamespace", "UsesRootOperatorProtocol", "UsesRootRetirement", "UsesRootStableBody", "UsesRootTerminalSelection", "UsesRootTerminateThenDelete", "UsesRootTermination"],
            ["root-retention"] = ["RequiresPinnedRootNamespace", "UsesRootOperatorProtocol", "UsesRootRetention", "UsesRootRetirement", "UsesRootStableBody", "UsesRootTerminalSelection", "UsesRootTerminateThenDelete", "UsesRootTermination"],
            ["root-operator-replies"] = ["RequiresPinnedRootNamespace", "UsesRootOperatorProtocol", "UsesRootOperatorReplies", "UsesRootRetention", "UsesRootRetirement", "UsesRootStableBody", "UsesRootTerminalSelection", "UsesRootTerminateThenDelete", "UsesRootTermination"],
            ["root-timed-job-preview"] = ["SupportsCorePreview", "UsesRootJobProtocol", "UsesRootTimedJobPreview", "UsesRootTimedJobSettlement"],
            ["root-park-opening"] = ["RequiresPinnedRootNamespace", "UsesRootParkOpening"],
            ["root-park-inspection"] = ["RequiresPinnedRootNamespace", "UsesRootParkInspection", "UsesRootParkOpening"],
            ["root-park-arrivals"] = ["RequiresPinnedRootNamespace", "UsesRootParkArrivals", "UsesRootParkInspection", "UsesRootParkOpening"],
            ["root-park-recovery"] = ["RequiresPinnedRootNamespace", "UsesRootOperatorProtocol", "UsesRootParkArrivals", "UsesRootParkInspection", "UsesRootParkOpening", "UsesRootParkRecovery"],
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
            var marker = model.FindEntityType(typeof(StoreVersionEntity))!.GetSeedData().Single()["MessageProtocol"];
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(model.ToDebugString(MetadataDebugStringOptions.LongDefault))))[..16];
            Console.WriteLine($"profile {name} protocol={db.RequiredFormat.MessageProtocol} marker={marker} timers={db.RequiredFormat == StoreFormat.RootTimers} model={fingerprint} uses=[{string.Join(",", set)}]");
            Check(db.GetType() == typeof(KernelDb), $"{name} is a plain KernelDb");
            Check(db.RequiredFormat == format, $"{name} carries format {format.MessageProtocol}");
            Check(db.Capabilities == expected, $"{name} carries exactly {expected}");
            Check(RootProfiles.Formats[format] == expected, $"{name} mapping for protocol {format.MessageProtocol} is {expected}");
            Check(Equals(marker, format.MessageProtocol), $"{name} model seeds marker protocol {format.MessageProtocol}");
            Check(db.UsesRootTimers == (format == StoreFormat.RootTimers), $"{name} timer selection matches its format");
            Check(set.SequenceEqual(accessors[name]), $"{name} turns on exactly [{string.Join(",", accessors[name])}]");
            var factory = db.GetService<IModelCacheKeyFactory>();
            Check(factory is KernelModelCacheKeyFactory, $"{name} resolves the kernel model cache key factory");
            var key = factory.Create(db, designTime: false);
            using (var again = open()) Check(Equals(key, factory.Create(again, designTime: false)), $"{name} gets the same cache key twice");
            Check(!Equals(key, factory.Create(db, designTime: true)), $"{name} design-time and runtime cache keys differ");
            keys.Add((name, key));
        }
        Check(RootProfiles.Formats.Count == profiles.Length, $"the mapping has {profiles.Length} formats, one per factory");
        Check(accessors.Count == profiles.Length, $"every profile has its expected accessors");
        var mapped = RootProfiles.Formats.Values.Aggregate(RootCapabilities.None, (all, next) => all | next);
        foreach (var capability in Enum.GetValues<RootCapabilities>().Where(c => c != RootCapabilities.None))
            Check(mapped.HasFlag(capability), $"capability {capability} is carried by at least one format");
        for (var i = 0; i < keys.Count; i++)
            for (var j = i + 1; j < keys.Count; j++)
                if (Equals(keys[i].Key, keys[j].Key)) Check(false, $"{keys[i].Name} and {keys[j].Name} share a model cache key");
        Check(keys.Select(k => k.Key).Distinct().Count() == keys.Count, $"{keys.Count} profiles get {keys.Count} distinct model cache keys");
        return failures == 0 ? 0 : 1;
    }
}
