using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

return await BenchmarkCli.RunAsync(args);

internal static class BenchmarkCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault() ?? "help";
            return command switch
            {
                "matrix" => await MatrixAsync(args[1..]),
                "compare" => Compare(args[1..]),
                "gate" => Gate(args[1..]),
                "host-fingerprint" => HostFingerprintCommand(),
                "workload-contracts" => WorkloadContractsCommand(),
                "help" or "--help" or "-h" => Help(),
                _ => Fail($"Unknown command '{command}'.")
            };
        }
        catch (PerformanceContractException exception) { return Fail(exception.Message); }
        catch (WorkloadContractException exception) { return Fail(exception.Message); }
    }

    private static async Task<int> MatrixAsync(string[] args)
    {
        var scale = args.FirstOrDefault() ?? throw new PerformanceContractException("matrix requires <scale>.");
        var workload = Option(args, "--workload") ?? throw new PerformanceContractException("matrix requires --workload.");
        var provider = Option(args, "--provider") ?? throw new PerformanceContractException("matrix requires --provider.");
        var adapter = Option(args, "--adapter") ?? throw new PerformanceContractException("matrix requires --adapter.");
        var form = Option(args, "--form") ?? throw new PerformanceContractException("matrix requires --form.");
        var output = Option(args, "--out") ?? throw new PerformanceContractException("matrix requires --out.");
        var repositoryRoot = SourceProvenance.FindRepositoryRoot();
        var catalog = WorkloadCatalog.Load(repositoryRoot);
        if (!catalog.Workloads.TryGetValue(workload, out var definition)) throw new PerformanceContractException($"Unknown frozen workload '{workload}'.");
        var packages = Options(args, "--package").Select(ParsePackage).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var providerConfiguration = Options(args, "--provider-setting").Select(ParseSetting).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var commitSha = Require(args, "--commit");
        SourceProvenance.RequireCleanHead(repositoryRoot, commitSha);
        if (!definition.RequiredProviderEvidence.TryGetValue(provider, out var providerTopology))
            throw new PerformanceContractException("The provider is not admitted by the frozen workload topology contract.");
        var request = new MatrixRequest(Require(args, "--cohort"), Require(args, "--measurement-set"), definition.Id, definition.Version, provider, adapter, form, scale, commitSha, SourceProvenance.HarnessAssemblySha256(), packages, Require(args, "--composition"), HostFingerprint.CaptureSha256(), Require(args, "--provider-version"), providerTopology, providerConfiguration, definition.Input.Seed, definition.Input.FingerprintSha256, Require(args, "--native-plan"), Require(args, "--native-plan-evidence"), Require(args, "--native-plan-sha256"));
        var plan = MatrixPlan.Create(definition, request);
        var child = Option(args, "--child-command") ?? throw new PerformanceContractException("matrix requires --child-command for a real provider adapter host; this foundation intentionally supplies no fake adapter.");
        await ProcessMatrixRunner.RunAsync(plan, child, output, CancellationToken.None);
        return 0;
    }

    private static int Compare(string[] args)
    {
        var output = Require(args, "--out");
        var comparison = Comparison.Compare(output, Require(args, "--oracle"), Require(args, "--target"), WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()));
        var result = ResultStore.Write(Option(args, "--result") ?? Path.Combine(output, "comparison.v1.json"), comparison);
        Console.WriteLine($"Comparison result: {result}");
        return comparison.Complete && comparison.CorrectnessEqual ? 0 : 2;
    }
    private static int Gate(string[] args)
    {
        var output = Require(args, "--out");
        var comparison = Comparison.Compare(output, Require(args, "--oracle"), Require(args, "--target"), WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()));
        ResultStore.Write(Option(args, "--comparison-result") ?? Path.Combine(output, "comparison.from-gate.v1.json"), comparison);
        var requestedClass = string.Equals(Option(args, "--class"), "runtime", StringComparison.OrdinalIgnoreCase) ? GateClass.RuntimeHotPath : GateClass.OrdinaryStore;
        var replacement = Option(args, "--replacement");
        var policy = !comparison.Complete || !comparison.CorrectnessEqual || replacement is null ? GatePolicy.DefaultFor(requestedClass) : GatePolicyFile.Load(replacement, comparison.WorkloadId, comparison.WorkloadVersion);
        if (replacement is not null && policy.GateClass != requestedClass && Option(args, "--class") is not null) return Fail("The reviewed replacement gate class does not match --class.");
        var verdict = GateEvaluator.Evaluate(policy, comparison);
        var result = ResultStore.Write(Option(args, "--result") ?? Path.Combine(output, "gate.v1.json"), new GateReport(verdict, replacement is null || !comparison.Complete || !comparison.CorrectnessEqual ? null : GatePolicyFile.Hash(replacement)));
        Console.WriteLine($"Gate result: {result}");
        return verdict.Verdict == PerformanceVerdict.Pass ? 0 : 2;
    }
    private static string Require(string[] args, string option) => Option(args, option) ?? throw new PerformanceContractException($"{args.FirstOrDefault() ?? "command"} requires {option}.");
    private static string? Option(string[] args, string name) { for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1]; return null; }
    private static IEnumerable<string> Options(string[] args, string name) { for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) yield return args[i + 1]; }
    private static KeyValuePair<string, string> ParsePackage(string value)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        return separator > 0 && separator < value.Length - 1 ? new KeyValuePair<string, string>(value[..separator], value[(separator + 1)..]) : throw new PerformanceContractException("--package must be name=version and must be repeated for every provider package.");
    }
    private static KeyValuePair<string, string> ParseSetting(string value)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        return separator > 0 && separator < value.Length - 1 ? new KeyValuePair<string, string>(value[..separator], value[(separator + 1)..]) : throw new PerformanceContractException("--provider-setting must be safe-name=safe-value and repeated for every material provider setting.");
    }
    private static int HostFingerprintCommand() { Console.WriteLine(HostFingerprint.CaptureSha256()); return 0; }
    private static int WorkloadContractsCommand() { Console.WriteLine(ReproducibleWorkloadScenarioCatalog.SerializeContractSummary()); return 0; }
    private static int Help() { Console.WriteLine("#646 store performance harness\n  host-fingerprint\n  workload-contracts\n  matrix <scale> --cohort <safe-id> --measurement-set <safe-id> --workload <id> --provider <provider> --provider-version <version> --provider-setting <name=value> --adapter <adapter> --form <physical-form> --commit <40-hex-sha> --composition <64-hex-fingerprint> --package <name=version> --native-plan <identity> --native-plan-evidence <safe-top-level.json> --native-plan-sha256 <64-hex-content-digest> --out <artifact-directory> --child-command <adapter-host>\n  compare --out <artifact-directory> --oracle <provider/adapter/form> --target <provider/adapter/form> [--result <comparison.json>]\n  gate --out <artifact-directory> --oracle <provider/adapter/form> --target <provider/adapter/form> [--class runtime|ordinary] [--replacement <reviewed-gate.v1.json>] [--comparison-result <comparison.json>] [--result <gate.json>]"); return 0; }
    private static int Fail(string message) { Console.Error.WriteLine($"error: {message}"); return 2; }
}
