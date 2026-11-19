using Kernel.Review6;

// Gate 2, sixth review: NT-1 (R6-B1) and PI-1 (R6-B2), each under the reviewed, repaired and negative rule sets, with
// mutations where they bite. NT-1's writes after setup are CommitBatches through the real InMemoryStore.CommitAsync.
// PI-1's go through the real PublishAsync, PublishedMatchesAsync, KeptMatchesAsync, AddEventAsync, CommitAsync and
// SweepAsync; its proposed batched sweeps (Pi1.EventSweep, Pi1.MessageSweep) remove rows from Store.EventRows and
// Store.Messages directly.
//   --case <id> --rules <set>   one run; exit 1 when an assertion fails
//   --all                       every case and rule set; exit 0 only when every run matches the expected outcome
//   --list                      the cases, their rule sets and the expected outcome of each
return Program.Main2(args);

static partial class Program
{
    // Case, rule set, and the ids of the assertions expected to fail (none: the run passes). --all compares each run's
    // actual set of failed ids with its expected set, so an unrelated failure is a mismatch. The README maps each run
    // to its page case.
    private static readonly string[] NtOutcome = ["room-for-terminate", "child-terminate", "inboxes-kept-closed"];
    private static readonly string[] NtCancelled = [.. NtOutcome, "p-cancelled"];
    private static readonly string[] Lost = ["old-done-retired", "payloads"];

    private static readonly (string Case, string Rules, string[] Failed)[] Matrix =
    [
        .. Three("nt1-growth", [], [], []), ("nt1-growth", "growth-undercount", ["within-bound", "witness-bound"]),
        .. Three("nt1-witness", NtOutcome, [], NtOutcome),
        .. Three("nt1-at-bound", NtCancelled, [], NtCancelled), ("nt1-at-bound", "growth-undercount", NtCancelled),
        .. Three("nt1-thendelete-protected", NtCancelled, [], NtCancelled),
        .. Three("nt1-no-inboxes", [], [], []),
        .. Three("pi1-gap", [], [], []), ("pi1-gap", "built-key", []),
        .. Three("pi1-event-first", Lost, [], Lost), ("pi1-event-first", "built-key", Lost), ("pi1-event-first", "match-without-incarnation", ["old-done-retired"]),
        .. Three("pi1-slow-consumer", ["payloads"], [], ["payloads"]), ("pi1-slow-consumer", "built-key", ["payloads"]),
        .. Three("pi1-duplicate", [], [], []), ("pi1-duplicate", "fresh-incarnation-on-duplicate", ["duplicate", "payloads"]),
    ];

    private static IEnumerable<(string, string, string[])> Three(string c, string[] reviewed, string[] repaired, string[] negative) =>
        [(c, "reviewed", reviewed), (c, "repaired", repaired), (c, "negative", negative)];

    private static string Outcome(IReadOnlyCollection<string> failed) => failed.Count == 0 ? "PASS" : $"FAIL {string.Join(",", failed)}";

    public static int Main2(string[] args)
    {
        if (args.SequenceEqual(["--list"]))
        {
            foreach (var (c, r, f) in Matrix) Console.WriteLine($"{c} --rules {r}  expected {Outcome(f)}");
            return 0;
        }
        if (args.SequenceEqual(["--all"]))
        {
            var summary = new List<string>();
            var mismatches = 0;
            foreach (var (c, r, f) in Matrix)
            {
                var report = Run(c, r);
                Console.Write(report.Render());
                Console.WriteLine();
                var actual = Outcome(report.FailedIds);
                var expected = Outcome(f);
                var matches = actual == expected;
                if (!matches) mismatches++;
                summary.Add($"{c,-26} {r,-32} actual {actual}  expected {expected}  {(matches ? "as expected" : "MISMATCH")}");
            }
            Console.WriteLine("== summary ==");
            foreach (var line in summary) Console.WriteLine(line);
            Console.WriteLine(mismatches == 0 ? $"{Matrix.Length}/{Matrix.Length} runs as expected" : $"{mismatches} of {Matrix.Length} runs differ from the expected outcome");
            return mismatches == 0 ? 0 : 1;
        }
        if (args.Length == 4 && args[0] == "--case" && args[2] == "--rules" && Matrix.Any(m => m.Case == args[1] && m.Rules == args[3]))
        {
            var report = Run(args[1], args[3]);
            Console.Write(report.Render());
            return report.Passed ? 0 : 1;
        }
        Console.Error.WriteLine("usage: --case <id> --rules <set>, a pair that --list shows | --all | --list");
        return 2;
    }

    private static Report Run(string id, string rules)
    {
        Real.Reset();
        // The id is exactly <family>-<variant>; Main2 admits only pairs listed in the matrix.
        var family = id.Length > 4 && id[3] == '-' ? id[..3] : "";
        var variant = family == "" ? "" : id[4..];
        var set = Rules.For(rules, family);
        return family switch
        {
            "nt1" when Nt1.Variants.Contains(variant) => new Nt1(variant, set).Run(),
            "pi1" when Pi1.Variants.Contains(variant) => new Pi1(variant, set).Run(),
            _ => throw new ArgumentException($"unknown case '{id}'; see --list"),
        };
    }
}
