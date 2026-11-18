using Kernel.Review4;

// Gate 2, fourth review: FR-1 (R4-B1) and RT-1 (R4-B2), each under the reviewed, repaired and negative rule sets.
//   --case <id> --rules <set>   one run; exit 1 when an assertion fails
//   --all                       every case and rule set; exit 0 only when every run matches the expected outcome
//   --list                      the cases, their rule sets and the expected outcome of each
return await Program.MainAsync(args);

static partial class Program
{
    public static string Name(FrRules rules) => rules switch
    { FrRules.Reviewed => "reviewed", FrRules.Repaired => "repaired", FrRules.Negative => "negative", FrRules.HeldBlocks => "held-blocks", _ => "memory-precedence" };

    public static string Name(RtRules rules) => rules switch
    {
        RtRules.Reviewed => "reviewed", RtRules.Repaired => "repaired", RtRules.Negative => "negative", RtRules.CascadeAny => "cascade-any",
        RtRules.SuccessorAdmitted => "successor-admitted", _ => "successor-inherits",
    };

    // Case, rule set, expected result. Every case runs reviewed, repaired and negative; a mutation of the repaired set
    // runs where it bites. The page case each run backs is in the README.
    private static readonly (string Case, string Rules, string Expected)[] Matrix =
    [
        .. from v in new[] { "base", "takeover-before-complete", "takeover-after-complete" }
           from r in new[] { ("reviewed", "FAIL"), ("repaired", "PASS"), ("negative", "FAIL") } select ($"fr1-{v}", r.Item1, r.Item2),
        ("fr1-takeover-before-complete", "memory-precedence", "FAIL"), ("fr1-takeover-after-complete", "memory-precedence", "FAIL"),
        ("fr1-model-end", "reviewed", "PASS"), ("fr1-model-end", "repaired", "PASS"), ("fr1-model-end", "negative", "PASS"),
        ("fr1-held", "reviewed", "PASS"), ("fr1-held", "repaired", "PASS"), ("fr1-held", "negative", "PASS"), ("fr1-held", "held-blocks", "FAIL"),
        .. from v in new[] { "a", "a-g-deleted" }
           from r in new[] { ("reviewed", "FAIL"), ("repaired", "PASS"), ("negative", "FAIL") } select ($"rt1-{v}", r.Item1, r.Item2),
        ("rt1-b", "reviewed", "PASS"), ("rt1-b", "repaired", "PASS"), ("rt1-b", "negative", "PASS"),
        ("rt1-nonparent", "reviewed", "FAIL"), ("rt1-nonparent", "repaired", "PASS"), ("rt1-nonparent", "negative", "FAIL"), ("rt1-nonparent", "cascade-any", "FAIL"),
        ("rt1-successor", "reviewed", "PASS"), ("rt1-successor", "repaired", "PASS"), ("rt1-successor", "negative", "PASS"), ("rt1-successor", "successor-inherits", "FAIL"),
        ("rt1-successor-unreachable", "reviewed", "FAIL"), ("rt1-successor-unreachable", "repaired", "PASS"), ("rt1-successor-unreachable", "negative", "FAIL"),
        ("rt1-successor-gap", "reviewed", "FAIL"), ("rt1-successor-gap", "repaired", "PASS"), ("rt1-successor-gap", "negative", "FAIL"), ("rt1-successor-gap", "successor-admitted", "FAIL"),
    ];

    public static async Task<int> MainAsync(string[] args)
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
                var report = await RunAsync(c, r);
                Console.Write(report.Render());
                Console.WriteLine();
                var actual = report.Passed ? "PASS" : "FAIL";
                var matches = e == actual;
                if (!matches) mismatches++;
                summary.Add($"{c,-30} {r,-19} actual {actual}  expected {e}  {(matches ? "as expected" : "MISMATCH")}");
            }
            Console.WriteLine("== summary ==");
            foreach (var line in summary) Console.WriteLine(line);
            Console.WriteLine(mismatches == 0 ? $"{Matrix.Length}/{Matrix.Length} runs as expected" : $"{mismatches} of {Matrix.Length} runs differ from the expected outcome");
            return mismatches == 0 ? 0 : 1;
        }
        if (args.Length == 4 && args[0] == "--case" && args[2] == "--rules")
        {
            var report = await RunAsync(args[1], args[3]);
            Console.Write(report.Render());
            return report.Passed ? 0 : 1;
        }
        Console.Error.WriteLine("usage: --case <id> --rules <reviewed|repaired|negative|held-blocks|memory-precedence|cascade-any|successor-admitted|successor-inherits> | --all | --list");
        return 2;
    }

    private static async Task<Report> RunAsync(string id, string rules)
    {
        Real.Reset();
        if (id.StartsWith("fr1-", StringComparison.Ordinal) && Fr1.Variants.Contains(id[4..]))
        {
            var set = rules switch
            {
                "reviewed" => FrRules.Reviewed, "repaired" => FrRules.Repaired, "negative" => FrRules.Negative, "held-blocks" => FrRules.HeldBlocks, "memory-precedence" => FrRules.MemoryPrecedence,
                _ => throw new ArgumentException($"FR-1 has no rule set '{rules}'"),
            };
            return await new Fr1(id[4..], set).RunAsync();
        }
        if (id.StartsWith("rt1-", StringComparison.Ordinal) && Rt1.Variants.Contains(id[4..]))
        {
            var set = rules switch
            {
                "reviewed" => RtRules.Reviewed, "repaired" => RtRules.Repaired, "negative" => RtRules.Negative, "cascade-any" => RtRules.CascadeAny, "successor-admitted" => RtRules.SuccessorAdmitted, "successor-inherits" => RtRules.SuccessorInherits,
                _ => throw new ArgumentException($"RT-1 has no rule set '{rules}'"),
            };
            return new Rt1(id[4..], set).Run();
        }
        throw new ArgumentException($"unknown case '{id}'; see --list");
    }
}
