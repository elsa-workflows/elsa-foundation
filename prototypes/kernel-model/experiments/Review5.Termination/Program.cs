using Kernel.Review5;

// Gate 2, fifth review: RT-2 (R5-B1, R5-m3), RT-3 (R5-B2) and RT-4 (R5-M5), each under the reviewed, repaired and
// negative rule sets, with mutations where they bite. Every write goes through the real InMemoryStore.CommitAsync.
//   --case <id> --rules <set>   one run; exit 1 when an assertion fails
//   --all                       every case and rule set; exit 0 only when every run matches the expected outcome
//   --list                      the cases, their rule sets and the expected outcome of each
//   --p-cascade <n>             (first) the page size; the expected matrix is stated for the default, 1,000
return Program.Main2(args);

static partial class Program
{
    public static int PCascade = 1000;

    // Case, rule set, expected result. Every case runs reviewed, repaired and negative; a mutation of the repaired set
    // runs where it bites. The page case each run backs is in the README.
    private static readonly (string Case, string Rules, string Expected)[] Matrix =
    [
        .. Three("rt2-witness", "FAIL", "PASS", "FAIL"),
        .. Three("rt2-pending", "PASS", "PASS", "PASS"),
        .. Three("rt2-done-kept", "FAIL", "PASS", "FAIL"),
        .. Three("rt2-absent", "FAIL", "PASS", "FAIL"), ("rt2-absent", "presence-from-memory", "PASS"),
        .. Three("rt2-mixed", "FAIL", "PASS", "FAIL"),
        .. Three("rt2-settle-after", "PASS", "PASS", "PASS"),
        .. Three("rt2-duplicate", "FAIL", "PASS", "FAIL"),
        .. Three("rt2-takeover", "FAIL", "PASS", "FAIL"), ("rt2-takeover", "presence-from-memory", "FAIL"),
        .. Three("rt2-open-drain-mixed", "FAIL", "PASS", "FAIL"),
        .. Three("rt2-live-pending", "PASS", "PASS", "PASS"),
        .. Three("rt2-generations", "PASS", "PASS", "PASS"), ("rt2-generations", "foreign-derivation", "FAIL"), ("rt2-generations", "generation-blind", "FAIL"),
        .. Three("rt2-off-registry", "PASS", "PASS", "PASS"), ("rt2-off-registry", "accept-off-registry", "FAIL"),
        .. Three("rt3-live-p", "PASS", "PASS", "PASS"),
        .. Three("rt3-live-p-plus-1", "PASS", "PASS", "PASS"),
        .. Three("rt3-live-10001", "FAIL", "PASS", "PASS"), ("rt3-live-10001", "page-above-cap", "FAIL"),
        .. Three("rt3-marker-10001", "FAIL", "PASS", "PASS"), ("rt3-marker-10001", "page-above-cap", "FAIL"),
        .. Three("rt3-open-drain-10001", "FAIL", "PASS", "PASS"),
        .. Three("rt3-jobs-marker", "FAIL", "PASS", "PASS"),
        .. Three("rt3-takeover", "FAIL", "PASS", "FAIL"), ("rt3-takeover", "no-next-control", "FAIL"),
        .. Three("rt3-crash-landed", "FAIL", "PASS", "FAIL"),
        .. Three("rt3-crash-lost", "FAIL", "PASS", "FAIL"),
        .. Three("rt3-delete-discharging", "FAIL", "PASS", "PASS"), ("rt3-delete-discharging", "delete-drops-discharging", "FAIL"),
        .. Three("rt4-running-unrelated", "FAIL", "PASS", "FAIL"), ("rt4-running-unrelated", "binding-running-only", "PASS"),
        .. Three("rt4-ended-registry", "FAIL", "PASS", "FAIL"), ("rt4-ended-registry", "binding-running-only", "FAIL"),
        .. Three("rt4-genuine-child", "PASS", "PASS", "PASS"),
        .. Three("rt4-operator", "PASS", "PASS", "PASS"),
    ];

    private static IEnumerable<(string, string, string)> Three(string c, string reviewed, string repaired, string negative) =>
        [(c, "reviewed", reviewed), (c, "repaired", repaired), (c, "negative", negative)];

    public static int Main2(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--p-cascade")
        {
            PCascade = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
            if (PCascade < 1) { Console.Error.WriteLine("--p-cascade must be positive"); return 2; }
            args = args[2..];
        }
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
                summary.Add($"{c,-24} {r,-24} actual {actual}  expected {e}  {(matches ? "as expected" : "MISMATCH")}");
            }
            Console.WriteLine("== summary ==");
            Console.WriteLine($"P_cascade {PCascade}, send cap {Harness.SendCap}");
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
        Console.Error.WriteLine("usage: [--p-cascade <n>] --case <id> --rules <reviewed|repaired|negative|presence-from-memory|foreign-derivation|generation-blind|accept-off-registry|page-above-cap|no-next-control|delete-drops-discharging|binding-running-only> | --all | --list");
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
            "rt2" when Rt2.Variants.Contains(variant) => new Rt2(variant, set).Run(),
            "rt3" when Rt3.Variants.Contains(variant) => new Rt3(variant, set).Run(),
            "rt4" when Rt4.Variants.Contains(variant) => new Rt4(variant, set).Run(),
            _ => throw new ArgumentException($"unknown case '{id}'; see --list"),
        };
    }
}
