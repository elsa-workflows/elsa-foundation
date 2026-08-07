using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

return await AdapterHostCli.RunAsync(args);

/// <summary>
/// The adapter child host for the #646 store-performance matrix.
///
/// The matrix runner spawns this executable four times per measurement set with a fixed argument list
/// (<c>run --request &lt;json&gt; --out &lt;dir&gt;</c>), so <c>--child-command</c> must point at the built
/// apphost binary, never at <c>dotnet run</c>: a rebuild mid-cohort would change the harness assembly
/// digest that every child re-verifies, and every child after the first would fail closed.
/// </summary>
internal static class AdapterHostCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return (args.FirstOrDefault() ?? "help") switch
            {
                "run" => await RunChildAsync(args[1..]),
                "capture-plan" => CapturePlan(args[1..]),
                "help" or "--help" or "-h" => Help(),
                var command => Fail($"Unknown command '{command}'.")
            };
        }
        catch (PerformanceContractException exception) { return Fail(exception.Message); }
        catch (WorkloadContractException exception) { return Fail(exception.Message); }
    }

    private static async Task<int> RunChildAsync(string[] args)
    {
        var request = RunRequestWire.Parse(HostArguments.Require(args, "run", "--request"));
        var output = HostArguments.Require(args, "run", "--out");
        var workload = Workload(request.WorkloadId);

        // Published before the adapter is created: ValidateCorrectness reads the evidence file off disk and
        // binds it to the request, so a child that measured first and staged afterwards would burn a full
        // measurement window before discovering a provenance mismatch.
        NativePlanEvidenceStaging.PublishInto(output, request);

        await using var adapter = await BenchmarkAdapterFactory.CreateAsync(new AdapterContext(request, workload), CancellationToken.None);
        var artifact = await ProcessMeasurement.ExecuteAsync(
            workload,
            request,
            BenchmarkProtocol.Acceptance,
            adapter,
            output,
            CancellationToken.None);
        ArtifactStore.Write(output, artifact);
        Console.WriteLine($"Wrote {ArtifactStore.PathFor(output, request)}");
        return 0;
    }

    /// <summary>
    /// Captures the native-plan evidence document the matrix will commit to via
    /// <c>--native-plan-evidence</c> and <c>--native-plan-sha256</c>. Routeless workloads need only the
    /// provenance binding; a workload with <c>requiredNativeRoutes</c> needs real captured provider plans,
    /// which is a separate leaf and is refused here rather than faked.
    /// </summary>
    private static int CapturePlan(string[] args)
    {
        const string Command = "capture-plan";
        var workload = Workload(HostArguments.Require(args, Command, "--workload"));
        var provider = HostArguments.Require(args, Command, "--provider");
        if (workload.RequiredNativeRoutes.Count > 0)
            return Fail(
                $"Workload '{workload.Id}' requires native-route evidence for {workload.RequiredNativeRoutes.Count} route(s) " +
                $"({string.Join(", ", workload.RequiredNativeRoutes)}), which needs a provider plan-capture leaf. " +
                "Only routeless workloads can be staged by this command today.");
        if (!workload.RequiredProviderEvidence.TryGetValue(provider, out var topology))
            return Fail($"Provider '{provider}' is not admitted by the frozen topology contract for '{workload.Id}'.");

        var identity = HostArguments.Require(args, Command, "--identity");
        var output = HostArguments.Require(args, Command, "--out");
        var document = new NativePlanEvidenceDocument(
            2,
            HostArguments.Require(args, Command, "--cohort"),
            HostArguments.Require(args, Command, "--measurement-set"),
            workload.Id,
            workload.Version,
            provider,
            HostArguments.Require(args, Command, "--adapter"),
            HostArguments.Require(args, Command, "--form"),
            HostArguments.Require(args, Command, "--scale"),
            HostArguments.Require(args, Command, "--commit"),
            SourceProvenance.HarnessAssemblySha256(),
            HostArguments.Require(args, Command, "--composition"),
            HostFingerprint.CaptureSha256(),
            HostArguments.Require(args, Command, "--provider-version"),
            topology,
            HostArguments.Settings(args, "--provider-setting"),
            workload.Input.Seed,
            workload.Input.FingerprintSha256,
            identity,
            []);

        var digest = NativePlanEvidenceStaging.Write(output, document);
        var reference = NativePlanEvidenceStaging.ReferenceFor(workload.Id, provider);
        Console.WriteLine($"Staged {Path.Combine(output, reference)}");
        Console.WriteLine();
        Console.WriteLine("Pass these to matrix verbatim, and export the staging directory for the children:");
        Console.WriteLine($"  --native-plan {identity} --native-plan-evidence {reference} --native-plan-sha256 {digest}");
        Console.WriteLine($"  export {NativePlanEvidenceStaging.StagingDirectoryVariable}={output}");
        return 0;
    }

    private static PerformanceWorkload Workload(string workloadId) =>
        WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads.TryGetValue(workloadId, out var workload)
            ? workload
            : throw new PerformanceContractException($"Unknown frozen workload '{workloadId}'.");

    private static int Help()
    {
        Console.WriteLine(
            """
            #646 store-performance adapter child host

              capture-plan --workload <id> --provider <provider> --cohort <safe-id> --measurement-set <safe-id>
                           --adapter <adapter> --form <physical-form> --scale <scale> --commit <40-hex>
                           --composition <64-hex> --provider-version <version> --provider-setting <name=value>
                           --identity <native-plan-identity> --out <staging-directory>

              run --request <run-request-json> --out <artifact-directory>
                  Spawned by `matrix --child-command`; not normally invoked by hand. Reads the staged
                  native-plan evidence from $ELSA_BENCH_NATIVE_PLAN_STAGING when the artifact directory
                  does not already hold it.

            Both the artifact directory and the staging directory must live OUTSIDE the worktree: every
            child re-verifies a clean HEAD with --untracked-files=all, so a stray file inside the repository
            aborts the run.

            Build this host and the harness in the SAME configuration. Each child re-verifies the harness
            assembly digest, and a Debug host cannot satisfy a Release matrix.
            """);
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 2;
    }
}
