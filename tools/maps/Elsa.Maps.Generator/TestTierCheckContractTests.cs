namespace Elsa.Maps.Generator;

/// <summary>Contract cases for <see cref="TestTierCheck"/>; CI runs them before trusting the check (spec 263, SC-005).</summary>
public static class TestTierCheckContractTests
{
    private const string Fast = "t/Fast/Fast.Tests.csproj";
    private const string Host = "t/Host/Host.Tests.csproj";
    private const string Db = "t/Db/Db.Tests.csproj";
    private const string Big = "t/Big/Big.Tests.csproj";
    private const string Guard = "t/Guard/Guard.Tests.csproj";

    /// <summary>Runs every case; throws on the first one that does not hold.</summary>
    public static void Run()
    {
        Expect("a clean manifest passes", Validate(Manifest(), Facts()), null);

        Expect("an unlisted test project fails", Validate(Manifest(), Facts(extraProject: "t/New/New.Tests.csproj")),
            "t/New/New.Tests.csproj is a test project with no entry");
        Expect("an entry for a missing project fails",
            Validate(Manifest(projects: [.. Projects(), new TestTierProject { Project = "t/Gone/Gone.Tests.csproj", Tier = "pr" }]), Facts()),
            "is listed but is not a test project");
        Expect("Testcontainers in the PR tier fails",
            Validate(Manifest(projects: Projects(dbTier: "pr")), Facts()), "references Testcontainers");
        Expect("a child host of 2 minutes in the PR tier fails",
            Validate(Manifest(projects: Projects(hostTier: "pr", hostMedian: 2)), Facts()), "launches a child host and takes 2 minutes");
        Expect("a child host under 2 minutes may stay in the PR tier",
            Validate(Manifest(projects: Projects(hostTier: "pr", hostMedian: 1.9)), Facts()), null);
        Expect("a git-only launcher is not a child host", Validate(Manifest(), Facts()), null);
        Expect("a wrong recorded trait fails",
            Validate(Manifest(projects: Projects(fastTrait: "child-process")), Facts()), "records trait \"child-process\"");
        Expect("a stale host-launch exemption fails",
            Validate(Manifest(exemptions: [new TestTierExemption { Project = Fast, Reason = "x" }]), Facts()), "exemption for t/Fast/Fast.Tests.csproj is stale");
        Expect("an exempt dotnet launcher stays in the PR tier",
            Validate(Manifest(exemptions: [new TestTierExemption { Project = Guard, Reason = "SDK commands only" }]),
                Facts(guardLaunchesDotnet: true)), null);
        Expect("a split naming a missing class fails",
            Validate(Manifest(splitClasses: ["Big.Tests.Alpha", "Big.Tests.Ghost"]), Facts()), "names Big.Tests.Ghost, which is not a class");
        Expect("a split that divides a collection fails",
            Validate(Manifest(splitClasses: ["Big.Tests.Alpha"]), Facts()), "xUnit collection \"shared\"");
        Expect("a unit in no shard fails", Validate(Manifest(prUnits: [Fast]), Facts()), "is in no pr shard");
        Expect("a unit in two shards fails", Validate(Manifest(prUnits: [Fast, Fast, $"{Big}#big-a", $"{Big}#big-rest"]), Facts()), "in more than one pr shard");
        Expect("an integration unit left in the PR shards after the switch fails",
            Validate(Manifest(switchOn: true, prUnits: [Fast, $"{Big}#big-a", $"{Big}#big-rest", Host]), Facts()), "is in a pr shard but is not a pr-tier unit");
        Expect("while the switch is off the PR shards must also cover integration units",
            Validate(Manifest(switchOn: false, prUnits: [Fast, $"{Big}#big-a", $"{Big}#big-rest"]), Facts()), "t/Host/Host.Tests.csproj is in no pr shard");
        Expect("a container suite in no shard fails", Validate(Manifest(containerSuites: []), Facts()), "db is in no containers shard");

        // The scanners the facts come from.
        if (!TestTierCheck.LaunchesDotnet("""var s = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet");""") ||
            !TestTierCheck.LaunchesDotnet("using var p = Process.Start(new ProcessStartInfo(DotnetMuxer()));") ||
            TestTierCheck.LaunchesDotnet("""var s = new ProcessStartInfo("git") { ArgumentList = { "ls-files" } };""") ||
            TestTierCheck.LaunchesDotnet("""// mentions "dotnet" but starts nothing"""))
            throw new InvalidOperationException("Test tier contract: the dotnet-launch scan misclassifies a case.");

        var classes = TestTierCheck.ReadClasses([
            """
            namespace Big.Tests;

            [Collection(Name)]
            [Trait("x", "y")]
            public sealed class Alpha
            {
                public const string Name = "shared";
            }

            /// <summary>Shares Alpha's collection.</summary>
            [Collection(Alpha.Name)]
            public class Beta;

            public class Gamma
            {
                private const string Name = "not a collection";
            }
            """
        ]);
        if (classes.GetValueOrDefault("Big.Tests.Alpha") != "shared" || classes.GetValueOrDefault("Big.Tests.Beta") != "shared" ||
            !classes.ContainsKey("Big.Tests.Gamma") || classes["Big.Tests.Gamma"] is not null)
            throw new InvalidOperationException("Test tier contract: the class and collection scan misreads a case.");

        // The committed manifest against the tree.
        var repo = RepoContext.Discover();
        var manifest = TestTierManifest.Read(repo);
        var problems = TestTierCheck.Validate(manifest, TestTierCheck.Gather(repo, manifest));
        if (problems.Count > 0)
            throw new InvalidOperationException($"Test tier contract: the committed manifest has {problems.Count} problem(s); run test-tiers-check.");
        Console.WriteLine("Test tier contract cases passed.");
    }

    private static IReadOnlyList<string> Validate(TestTierManifest manifest, TestTierFacts facts) => TestTierCheck.Validate(manifest, facts);

    private static void Expect(string name, IReadOnlyList<string> problems, string? fragment)
    {
        var holds = fragment is null ? problems.Count == 0 : problems.Any(problem => problem.Contains(fragment, StringComparison.Ordinal));
        if (!holds)
            throw new InvalidOperationException(
                $"Test tier contract \"{name}\" failed. Problems: {(problems.Count == 0 ? "none" : string.Join(" | ", problems))}");
    }

    private static TestTierProject[] Projects(string dbTier = "integration", string hostTier = "integration", double hostMedian = 5, string? fastTrait = null) =>
    [
        new() { Project = Fast, Tier = "pr", Trait = fastTrait, MedianMinutes = 0.5 },
        new() { Project = Guard, Tier = "pr", MedianMinutes = 3 },
        new() { Project = Big, Tier = "pr", MedianMinutes = 8 },
        new() { Project = Host, Tier = hostTier, Trait = "child-process", MedianMinutes = hostMedian },
        new() { Project = Db, Tier = dbTier, Trait = "testcontainers" }
    ];

    private static TestTierManifest Manifest(
        TestTierProject[]? projects = null,
        bool switchOn = false,
        string[]? prUnits = null,
        string[]? splitClasses = null,
        string[]? containerSuites = null,
        TestTierExemption[]? exemptions = null)
    {
        projects ??= Projects();
        var hostInPr = projects.First(project => project.Project == Host).Tier == "pr";
        return new TestTierManifest
        {
            Schema = 1,
            IntegrationLeavesPrTier = switchOn,
            OwnJobs = new Dictionary<string, string> { [Guard] = "Guard job" },
            HostLaunchExemptions = exemptions ?? [],
            Projects = projects,
            Splits = new Dictionary<string, TestTierSplit>
            {
                [Big] = new() { Groups = [new() { Name = "big-a", Classes = splitClasses ?? ["Big.Tests.Alpha", "Big.Tests.Beta"] }, new() { Name = "big-rest", Rest = true }] }
            },
            Shards = new TestTierShards
            {
                Pr = [new() { Name = "pr-1", Units = prUnits ?? (switchOn || hostInPr
                    ? [Fast, $"{Big}#big-a", $"{Big}#big-rest", .. hostInPr ? new[] { Host } : []]
                    : [Fast, $"{Big}#big-a", $"{Big}#big-rest", Host]) }],
                Integration = hostInPr ? [] : [new() { Name = "int-1", Units = [Host] }],
                Containers = [new() { Name = "ef-1", Suites = containerSuites ?? ["db"] }]
            },
            ContainerSuites = [new EfSuite("db", Db, "")]
        };
    }

    private static TestTierFacts Facts(string? extraProject = null, bool guardLaunchesDotnet = false) => new(
        TestProjects: [Fast, Guard, Big, Host, Db, .. extraProject is null ? Array.Empty<string>() : [extraProject]],
        TestcontainersRoots: [Db],
        HostLaunchers: [Host, .. guardLaunchesDotnet ? new[] { Guard } : []],
        ClassesByProject: new Dictionary<string, IReadOnlyDictionary<string, string?>>
        {
            [Big] = new Dictionary<string, string?> { ["Big.Tests.Alpha"] = "shared", ["Big.Tests.Beta"] = "shared", ["Big.Tests.Gamma"] = null }
        });
}
