using Kernel.Review5;

// Gate 2, fifth review: EV-1 (R5-M2), FR-2 (R5-M3), JR-1 (R5-M4) and PO-1 (R5-M6), each under the reviewed, repaired
// and negative rule sets, with mutations of the repaired set where they bite.
//   --case <id> --rules <set>   one run; exit 0 when every assertion holds, 1 when one fails, 3 when the run stops
//   --all                       every case and rule set; exit 0 only when every run matches the expected outcome
//   --list                      the cases, their rule sets and the expected outcome of each
return await Program.MainAsync(args);

static partial class Program
{
    public static string Name(EvRules rules) => rules switch
    {
        EvRules.Reviewed => "reviewed", EvRules.Repaired => "repaired", EvRules.Negative => "negative", EvRules.MatchAtNow => "match-at-now",
        EvRules.P0AtPageCommit => "p0-at-page-commit", EvRules.NoHorizon => "no-horizon", _ => "meet-waits-while-suspended",
    };

    public static string Name(Fr2Rules rules) => rules switch
    {
        Fr2Rules.Reviewed => "reviewed", Fr2Rules.Repaired => "repaired", Fr2Rules.Negative => "negative", Fr2Rules.SeenGate => "seen-gate",
        Fr2Rules.CreditMetadataOnly => "credit-metadata-only", Fr2Rules.PPageWhateverU => "p-page-whatever-u", Fr2Rules.ZeroCutCompletes => "zero-cut-completes",
        Fr2Rules.PagesWhileSuspended => "pages-while-suspended", _ => "held-gate-waits",
    };

    public static string Name(JrRules rules) => rules switch
    { JrRules.Reviewed => "reviewed", JrRules.ReviewedKind => "reviewed-kind", JrRules.Repaired => "repaired", JrRules.Negative => "negative", _ => "generation-blind" };

    public static string Name(PoRules rules) => rules switch { PoRules.Reviewed => "reviewed", PoRules.Repaired => "repaired", _ => "negative" };

    public static string Name(BaseRules rules) => rules switch
    { BaseRules.Reviewed => "reviewed", BaseRules.Repaired => "repaired", BaseRules.Negative => "negative", _ => "seen-gate-no-precedence" };

    // Case, rule set, expected result. The page case each run backs is in the README.
    private static readonly (string Case, string Rules, string Expected)[] Matrix =
    [
        ("ev1-a", "reviewed", "FAIL"), ("ev1-a", "repaired", "PASS"),
        ("ev1-a-after", "reviewed", "PASS"), ("ev1-a-after", "repaired", "PASS"),
        ("ev1-b", "reviewed", "FAIL"), ("ev1-b", "repaired", "PASS"), ("ev1-b", "negative", "FAIL"), ("ev1-b", "match-at-now", "FAIL"),
        ("ev1-b-paged", "reviewed", "FAIL"), ("ev1-b-paged", "repaired", "PASS"), ("ev1-b-paged", "negative", "FAIL"), ("ev1-b-paged", "p0-at-page-commit", "FAIL"),
        ("ev1-b-stepback", "reviewed", "FAIL"), ("ev1-b-stepback", "repaired", "STOPPED"), ("ev1-b-stepback", "no-horizon", "FAIL"),
        ("ev1-b-suspended", "reviewed", "PASS"), ("ev1-b-suspended", "repaired", "PASS"), ("ev1-b-suspended", "meet-waits-while-suspended", "FAIL"),
        .. from v in new[] { "replay", "replay-atomic" }
           from r in new[] { ("reviewed", "FAIL"), ("repaired", "PASS"), ("negative", "FAIL") } select ($"ev1-{v}", r.Item1, r.Item2),
        ("ev1-replay-consumed", "reviewed", "FAIL"), ("ev1-replay-consumed", "repaired", "STOPPED"), ("ev1-replay-consumed", "negative", "FAIL"),
        .. from v in new[] { "base", "hold", "suspend", "takeover", "hold-takeover", "bytes", "zero-cut" }
           from r in new[] { ("reviewed", "FAIL"), ("repaired", "PASS"), ("negative", "FAIL") } select ($"fr2-{v}", r.Item1, r.Item2),
        .. from v in new[] { "base", "hold", "suspend", "takeover", "hold-takeover", "bytes", "zero-cut" } select ($"fr2-{v}", "seen-gate", "PASS"),
        ("fr2-bytes", "credit-metadata-only", "FAIL"), ("fr2-hold-takeover", "p-page-whatever-u", "FAIL"), ("fr2-zero-cut", "zero-cut-completes", "FAIL"),
        ("fr2-suspend", "pages-while-suspended", "FAIL"), ("fr2-hold", "held-gate-waits", "FAIL"),
        ("fr2-r4b1-base", "reviewed", "PASS"), ("fr2-r4b1-base", "repaired", "PASS"), ("fr2-r4b1-base", "negative", "PASS"), ("fr2-r4b1-base", "seen-gate-no-precedence", "FAIL"),
        ("fr2-r4b1-held", "reviewed", "PASS"), ("fr2-r4b1-held", "repaired", "PASS"), ("fr2-r4b1-held", "negative", "FAIL"), ("fr2-r4b1-held", "seen-gate-no-precedence", "FAIL"),
        .. from v in new[] { "crash-after", "crash-after-keep", "obsolete" }
           from r in new[] { ("reviewed", "FAIL"), ("reviewed-kind", "FAIL"), ("repaired", "PASS"), ("negative", "FAIL") } select ($"jr1-{v}", r.Item1, r.Item2),
        ("jr1-obsolete", "generation-blind", "FAIL"),
        ("jr1-crash-before", "reviewed", "FAIL"), ("jr1-crash-before", "reviewed-kind", "FAIL"), ("jr1-crash-before", "repaired", "STOPPED"), ("jr1-crash-before", "negative", "FAIL"),
        .. from r in new[] { "reviewed", "reviewed-kind", "repaired", "negative" } select ("jr1-running", r, "PASS"),
        .. from v in new[] { "cohort", "open" }
           from r in new[] { ("reviewed", "FAIL"), ("repaired", "PASS"), ("negative", "FAIL") } select ($"po1-{v}", r.Item1, r.Item2),
        ("po1-ordinary", "reviewed", "PASS"), ("po1-ordinary", "repaired", "PASS"), ("po1-ordinary", "negative", "PASS"),
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
                var matches = e == report.Outcome;
                if (!matches) mismatches++;
                summary.Add($"{c,-30} {r,-26} actual {report.Outcome,-7}  expected {e,-7}  {(matches ? "as expected" : "MISMATCH")}");
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
            return report.Outcome switch { "PASS" => 0, "STOPPED" => 3, _ => 1 };
        }
        Console.Error.WriteLine("usage: --case <id> --rules <set> | --all | --list   (see --list for the cases and their rule sets)");
        return 2;
    }

    private static async Task<Report> RunAsync(string id, string rules)
    {
        Real.Reset();
        if (id.StartsWith("ev1-", StringComparison.Ordinal) && Ev1.Variants.Contains(id[4..]))
        {
            var set = rules switch
            {
                "reviewed" => EvRules.Reviewed, "repaired" => EvRules.Repaired, "negative" => EvRules.Negative, "match-at-now" => EvRules.MatchAtNow, "p0-at-page-commit" => EvRules.P0AtPageCommit, "no-horizon" => EvRules.NoHorizon, "meet-waits-while-suspended" => EvRules.MeetWaitsWhileSuspended,
                _ => throw new ArgumentException($"EV-1 has no rule set '{rules}'"),
            };
            return await new Ev1(id[4..], set).RunAsync();
        }
        if (id.StartsWith("fr2-", StringComparison.Ordinal) && Fr2Base.Variants.Contains(id[4..]))
        {
            var set = rules switch
            {
                "reviewed" => BaseRules.Reviewed, "repaired" => BaseRules.Repaired, "negative" => BaseRules.Negative, "seen-gate-no-precedence" => BaseRules.SeenGateNoPrecedence,
                _ => throw new ArgumentException($"FR-2's R4-B1 traces have no rule set '{rules}'"),
            };
            return await new Fr2Base(id[4..], set).RunAsync();
        }
        if (id.StartsWith("fr2-", StringComparison.Ordinal) && Fr2.Variants.Contains(id[4..]))
        {
            var set = rules switch
            {
                "reviewed" => Fr2Rules.Reviewed, "repaired" => Fr2Rules.Repaired, "negative" => Fr2Rules.Negative, "seen-gate" => Fr2Rules.SeenGate,
                "credit-metadata-only" => Fr2Rules.CreditMetadataOnly, "p-page-whatever-u" => Fr2Rules.PPageWhateverU, "zero-cut-completes" => Fr2Rules.ZeroCutCompletes,
                "pages-while-suspended" => Fr2Rules.PagesWhileSuspended, "held-gate-waits" => Fr2Rules.HeldGateWaits,
                _ => throw new ArgumentException($"FR-2 has no rule set '{rules}'"),
            };
            return await new Fr2(id[4..], set).RunAsync();
        }
        if (id.StartsWith("jr1-", StringComparison.Ordinal) && Jr1.Variants.Contains(id[4..]))
        {
            var set = rules switch
            {
                "reviewed" => JrRules.Reviewed, "reviewed-kind" => JrRules.ReviewedKind, "repaired" => JrRules.Repaired, "negative" => JrRules.Negative, "generation-blind" => JrRules.GenerationBlind,
                _ => throw new ArgumentException($"JR-1 has no rule set '{rules}'"),
            };
            return await new Jr1(id[4..], set).RunAsync();
        }
        if (id.StartsWith("po1-", StringComparison.Ordinal) && Po1.Variants.Contains(id[4..]))
        {
            var set = rules switch
            {
                "reviewed" => PoRules.Reviewed, "repaired" => PoRules.Repaired, "negative" => PoRules.Negative,
                _ => throw new ArgumentException($"PO-1 has no rule set '{rules}'"),
            };
            return await new Po1(id[4..], set).RunAsync();
        }
        throw new ArgumentException($"unknown case '{id}'; see --list");
    }
}
