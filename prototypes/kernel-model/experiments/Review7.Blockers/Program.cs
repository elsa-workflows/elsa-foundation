using Kernel.Review7;

// Gate 2, seventh review: NT-2 (R7-B1) and MX-1 (R7-M1), each under the reviewed, repaired and negative rule sets, and
// MX-1 also under the mutation refusal-without-prefix. NT-2's writes after setup are CommitBatches through the real
// InMemoryStore.CommitAsync, whose Refuses hook holds the row cap. MX-1's go through the real DurableTurn.Publish, the
// real CommitAsync and the real Events.PublishAsync (PublishAsync, PublishedMatchesAsync, AddEventAsync).
//   --case <id> --rules <set>   one run; exit 1 when an assertion fails
//   --all                       every case and rule set; exit 0 only when every run matches the expected outcome
//   --list                      the cases, their rule sets and the expected outcome of each
return Program.Main2(args);

static partial class Program
{
    // Case, rule set, and the ids of the assertions expected to fail (none: the run passes). --all compares each run's
    // actual set of failed ids with its expected set, so an unrelated failure is a mismatch. The README maps each run
    // to its page case.
    private static readonly string[] NtHandles = ["growth-parked", "room-for-terminate", "child-terminate", "inboxes-kept-closed"];
    private static readonly string[] NtBound = ["room-for-terminate", "child-terminate", "inboxes-kept-closed", "p-cancelled"];
    private static readonly string[] MxShared = ["outside-refused", "turn-event", "payloads"];

    private static readonly (string Case, string Rules, string[] Failed)[] Matrix =
    [
        .. Three("nt2-handles", NtHandles, [], NtHandles),
        .. Three("nt2-at-bound", NtBound, [], NtBound),
        .. Three("nt2-thendelete-protected", NtBound, [], NtBound),
        .. Three("nt2-no-handles", [], [], []),
        .. Three("mx1-ticket-first", MxShared, [], MxShared), ("mx1-ticket-first", "refusal-without-prefix", MxShared),
        .. Three("mx1-outside-first", MxShared, [], MxShared), ("mx1-outside-first", "refusal-without-prefix", MxShared),
        .. Three("mx1-owner-change", MxShared, [], MxShared), ("mx1-owner-change", "refusal-without-prefix", MxShared),
        .. Three("mx1-distinct", [], [], []), ("mx1-distinct", "refusal-without-prefix", []),
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
                summary.Add($"{c,-26} {r,-24} actual {actual}  expected {expected}  {(matches ? "as expected" : "MISMATCH")}");
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
            "nt2" when Nt2.Variants.Contains(variant) => new Nt2(variant, set).Run(),
            "mx1" when Mx1.Variants.Contains(variant) => new Mx1(variant, set).Run(),
            _ => throw new ArgumentException($"unknown case '{id}'; see --list"),
        };
    }
}
