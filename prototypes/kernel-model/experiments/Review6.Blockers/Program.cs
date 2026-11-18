using Kernel.Review6;

// Gate 2, sixth review: NT-1 (R6-B1) and PI-1 (R6-B2), each under the reviewed, repaired and negative rule sets, with
// mutations where they bite. Every write after setup goes through the real InMemoryStore.
//   --case <id> --rules <set>   one run; exit 1 when an assertion fails
//   --all                       every case and rule set; exit 0 only when every run matches the expected outcome
//   --list                      the cases, their rule sets and the expected outcome of each
return Program.Main2(args);

static partial class Program
{
    // Case, rule set, expected result. The page case each run backs is in the README.
    private static readonly (string Case, string Rules, string Expected)[] Matrix =
    [
        .. Three("nt1-growth", "PASS", "PASS", "PASS"), ("nt1-growth", "growth-undercount", "FAIL"),
        .. Three("nt1-witness", "FAIL", "PASS", "FAIL"),
        .. Three("nt1-at-bound", "FAIL", "PASS", "FAIL"), ("nt1-at-bound", "growth-undercount", "FAIL"),
        .. Three("nt1-thendelete-protected", "FAIL", "PASS", "FAIL"),
        .. Three("nt1-no-inboxes", "PASS", "PASS", "PASS"),
        .. Three("pi1-gap", "PASS", "PASS", "PASS"), ("pi1-gap", "built-key", "PASS"),
        .. Three("pi1-event-first", "FAIL", "PASS", "FAIL"), ("pi1-event-first", "built-key", "FAIL"), ("pi1-event-first", "match-without-incarnation", "FAIL"),
        .. Three("pi1-slow-consumer", "FAIL", "PASS", "FAIL"), ("pi1-slow-consumer", "built-key", "FAIL"),
        .. Three("pi1-duplicate", "PASS", "PASS", "PASS"), ("pi1-duplicate", "fresh-incarnation-on-duplicate", "FAIL"),
    ];

    private static IEnumerable<(string, string, string)> Three(string c, string reviewed, string repaired, string negative) =>
        [(c, "reviewed", reviewed), (c, "repaired", repaired), (c, "negative", negative)];

    public static int Main2(string[] args)
    {
        if (args.SequenceEqual(["--list"]))
        {
            foreach (var (c, r, e) in Matrix) Console.WriteLine($"{c} --rules {r}  expected {e}");
            return 0;
        }
        if (args.SequenceEqual(["--all"]))
        {
            var summary = new List<string>();
            var mismatches = 0;
            foreach (var (c, r, e) in Matrix)
            {
                var report = Run(c, r);
                Console.Write(report.Render());
                Console.WriteLine();
                var actual = report.Passed ? "PASS" : "FAIL";
                var matches = e == actual;
                if (!matches) mismatches++;
                summary.Add($"{c,-26} {r,-32} actual {actual}  expected {e}  {(matches ? "as expected" : "MISMATCH")}");
            }
            Console.WriteLine("== summary ==");
            foreach (var line in summary) Console.WriteLine(line);
            Console.WriteLine(mismatches == 0 ? $"{Matrix.Length}/{Matrix.Length} runs as expected" : $"{mismatches} of {Matrix.Length} runs differ from the expected outcome");
            return mismatches == 0 ? 0 : 1;
        }
        if (args.Length == 4 && args[0] == "--case" && args[2] == "--rules")
        {
            var report = Run(args[1], args[3]);
            Console.Write(report.Render());
            return report.Passed ? 0 : 1;
        }
        Console.Error.WriteLine("usage: --case <id> --rules <reviewed|repaired|negative|growth-undercount|built-key|match-without-incarnation|fresh-incarnation-on-duplicate> | --all | --list");
        return 2;
    }

    private static Report Run(string id, string rules)
    {
        Real.Reset();
        var family = id.Length > 4 ? id[..3] : "";
        var variant = id.Length > 4 ? id[4..] : "";
        var set = Rules.For(rules, family);
        return family switch
        {
            "nt1" when Nt1.Variants.Contains(variant) => new Nt1(variant, set).Run(),
            "pi1" when Pi1.Variants.Contains(variant) => new Pi1(variant, set).Run(),
            _ => throw new ArgumentException($"unknown case '{id}'; see --list"),
        };
    }
}
