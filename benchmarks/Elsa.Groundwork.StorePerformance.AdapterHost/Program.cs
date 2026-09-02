using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using System.Text.Json;

// The matrix runner spawns this host once per process in a cohort. Every failure path exits non-zero with
// the contract message on stderr: the runner treats a child that cannot honour its request as a blocked
// run, and a host that degraded to a partial result would publish numbers describing something else.
try
{
    var command = args.Length > 0 ? args[0] : "";
    return command switch
    {
        "probe-provider" => await ProbeProvider(args),
        "describe-matrix" => DescribeMatrix(),
        "capture-plan" => await CapturePlan(args),
        "verify-correctness" => await VerifyCorrectness(args),
        "run" => await Run(args),
        _ => throw new PerformanceContractException(
            $"Unknown adapter-host command '{command}'. Supported: describe-matrix, probe-provider, capture-plan, verify-correctness, run.")
    };
}
catch (PerformanceContractException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

static int DescribeMatrix()
{
    var repositoryRoot = SourceProvenance.FindRepositoryRoot();
    SourceProvenance.RequireCleanCurrentBuild(
        repositoryRoot,
        (typeof(MatrixCatalog).Assembly, "adapter host"),
        (typeof(SourceProvenance).Assembly, "benchmark harness"));
    var document = MatrixCatalog.Build(repositoryRoot);
    Console.WriteLine(JsonSerializer.Serialize(document, ArtifactStore.JsonOptions));
    return 0;
}

static RunRequest AdmitCurrentInvocation(string[] args, string command)
{
    var request = RunRequestWire.Parse(HostArguments.Require(args, command, "--request"));
    var repositoryRoot = SourceProvenance.FindRepositoryRoot();
    SourceProvenance.RequireCleanHead(repositoryRoot, request.CommitSha);
    SourceProvenance.RequireAssemblyRevision(typeof(MatrixCatalog).Assembly, request.CommitSha, "adapter host");
    SourceProvenance.RequireAssemblyRevision(typeof(SourceProvenance).Assembly, request.CommitSha, "benchmark harness");
    return request;
}

static void AdmitOutput(string[] args, string command) =>
    ArtifactOutputAdmission.RequireExternal(
        HostArguments.Require(args, command, "--out"),
        SourceProvenance.FindRepositoryRoot());

// Reads the provider's own identity off a live connection. ValidateCorrectness binds the observed
// provider configuration to the requested one entry for entry, so these values must be read rather than
// guessed — this is the command whose output the operator pastes into capture-plan and matrix.
static async Task<int> ProbeProvider(string[] args)
{
    var repositoryRoot = SourceProvenance.FindRepositoryRoot();
    SourceProvenance.RequireCleanCurrentBuild(
        repositoryRoot,
        (typeof(MatrixCatalog).Assembly, "adapter host"),
        (typeof(SourceProvenance).Assembly, "benchmark harness"));
    var provider = HostArguments.Require(args, "probe-provider", "--provider");
    var connectionString = ProviderConnections.RequireConnectionString(provider);
    var probe = await ProviderProbe.ReadAsync(provider, connectionString);
    Console.WriteLine($"provider={probe.Provider}");
    Console.WriteLine($"connection-type={probe.ConnectionType}");
    Console.WriteLine($"provider-version={probe.Version}");
    Console.WriteLine($"provider-topology={probe.Topology}");
    foreach (var (key, value) in probe.Configuration.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        Console.WriteLine($"provider-setting={key}={value}");
    return 0;
}

static async Task<int> CapturePlan(string[] args)
{
    var request = RunRequestWire.Parse(HostArguments.Require(args, "capture-plan", "--request"));
    var outputDirectory = HostArguments.Require(args, "capture-plan", "--out");
    // Admission is deliberately before connection lookup/probing and before staging can create a file.
    // Requests are untrusted JSON: ArtifactAdmission also runs ArtifactSafety.ValidateRequest, which
    // rejects malformed metadata, connection material, and unsafe evidence references at this boundary.
    outputDirectory = CapturePlanAdmission.Ensure(request, outputDirectory);
    AdmitCurrentInvocation(args, "capture-plan");
    var connectionString = ProviderConnections.RequireConnectionString(request.Provider);
    var observed = await ProviderProbe.ReadAsync(request.Provider, connectionString);

    if (string.Equals(request.WorkloadId, IamNormalizedLookupWorkload.WorkloadId, StringComparison.Ordinal))
    {
        var iamDigest = await IamNativePlanCapture.CaptureAsync(
            request,
            connectionString,
            outputDirectory,
            observed,
            CancellationToken.None);
        Console.WriteLine($"native-plan-evidence={request.NativePlanEvidenceReference}");
        Console.WriteLine($"native-plan-sha256={iamDigest}");
        Console.WriteLine($"native-plan-routes={IamNormalizedLookupWorkload.NativeRouteLimits.Count}");
        return 0;
    }

    if (string.Equals(request.WorkloadId, SecretCreateReadListWorkload.WorkloadId, StringComparison.Ordinal))
    {
        var secretDigest = await SecretNativePlanCapture.CaptureAsync(
            request,
            connectionString,
            outputDirectory,
            observed,
            CancellationToken.None);
        Console.WriteLine($"native-plan-evidence={request.NativePlanEvidenceReference}");
        Console.WriteLine($"native-plan-sha256={secretDigest}");
        Console.WriteLine("native-plan-routes=1");
        return 0;
    }

    if (string.Equals(request.WorkloadId, RuntimeRecoveryScanWorkload.WorkloadId, StringComparison.Ordinal))
    {
        var recoveryDigest = await RecoveryNativePlanCapture.CaptureAsync(
            request,
            connectionString,
            outputDirectory,
            observed,
            CancellationToken.None);
        Console.WriteLine($"native-plan-evidence={request.NativePlanEvidenceReference}");
        Console.WriteLine($"native-plan-sha256={recoveryDigest}");
        Console.WriteLine("native-plan-routes=4");
        return 0;
    }

    if (string.Equals(request.WorkloadId, DiagnosticsDurableHistoryWorkload.WorkloadId, StringComparison.Ordinal))
    {
        // Diagnostics has declared provider-native routes. It must never fall through to the
        // checkpoint provenance document, whose empty route list would make a capture look complete.
        var diagnosticsDigest = await DiagnosticsNativePlanCapture.CaptureAsync(
            request,
            connectionString,
            outputDirectory,
            observed,
            CancellationToken.None);
        Console.WriteLine($"native-plan-evidence={request.NativePlanEvidenceReference}");
        Console.WriteLine($"native-plan-sha256={diagnosticsDigest}");
        if (request.Adapter == EfDiagnosticsDurableHistoryAdapter.AdapterId)
            Console.WriteLine("native-plan-routes=0 (EF correctness-only; bounded diagnostics routes are blocked by the public EF query shape)");
        else
        {
            var capturedRoutes = DiagnosticsDurableHistoryWorkload.NativeRouteLimits.Keys.Count(route =>
                !string.IsNullOrWhiteSpace(DiagnosticsNativePlanContract.For(request.Adapter, route).IndexName));
            Console.WriteLine($"native-plan-routes={capturedRoutes}; blocked-routes={DiagnosticsDurableHistoryWorkload.NativeRouteLimits.Count - capturedRoutes}");
        }
        return 0;
    }

    if (!string.Equals(request.WorkloadId, RuntimeCheckpointCommitWorkload.WorkloadId, StringComparison.Ordinal))
        throw new PerformanceContractException(
            $"Native-plan capture dispatch is missing for admitted workload '{request.WorkloadId}'.");

    var reference = NativePlanEvidenceStaging.ReferenceFor(request.WorkloadId, request.Provider, request.MeasurementSetId);
    if (!string.Equals(reference, request.NativePlanEvidenceReference, StringComparison.Ordinal))
        throw new PerformanceContractException(
            $"Checkpoint evidence must use '{reference}' as --native-plan-evidence; received '{request.NativePlanEvidenceReference}'.");
    var digest = NativePlanEvidenceStaging.WriteCheckpoint(outputDirectory, request, observed);
    Console.WriteLine($"native-plan-evidence={reference}");
    Console.WriteLine($"native-plan-sha256={digest}");
    Console.WriteLine("native-plan-routes=0 (required by the frozen checkpoint-commit contract)");
    return 0;
}

// The measured path the matrix runner drives, writing the process artifact where the runner reads it
// after the child exits.
//
// The adapter prepares the workload-owned public operation phases after the correctness baseline succeeds;
// ProcessMeasurement then warms and times those same phases without rebuilding their private fixtures.
static async Task<int> Run(string[] args)
{
    AdmitOutput(args, "run");
    AdmitCurrentInvocation(args, "run");
    var admitted = SecretRunAdmission.ParseAndResolve(
        args,
        "run",
        ProviderConnections.RequireConnectionString);
    var (request, outputDirectory, connectionString, workload) = admitted;
    await using var adapter = BenchmarkAdapterRegistry.Create(request, connectionString, outputDirectory);
    var artifact = await ProcessMeasurement.ExecuteAsync(
        workload, request, BenchmarkProtocol.Acceptance, adapter, outputDirectory, CancellationToken.None);
    ArtifactStore.Write(outputDirectory, artifact);
    return 0;
}

// Runs only the correctness baseline: compose, commit the catalog-owned bundle through the public stores,
// and validate the digest and the evidence bindings. The `run` command additionally prepares and executes
// the workload-owned timed phases after this same baseline has passed.
static async Task<int> VerifyCorrectness(string[] args)
{
    AdmitOutput(args, "verify-correctness");
    AdmitCurrentInvocation(args, "verify-correctness");
    var admitted = SecretRunAdmission.ParseAndResolve(
        args,
        "verify-correctness",
        ProviderConnections.RequireConnectionString);
    var (request, outputDirectory, connectionString, workload) = admitted;

    await using var adapter = BenchmarkAdapterRegistry.Create(request, connectionString, outputDirectory);
    await adapter.PrepareAsync(CancellationToken.None);
    var correctness = await adapter.VerifyCorrectnessAsync(CancellationToken.None);
    ArtifactAdmission.ValidateCorrectness(workload, request, correctness, outputDirectory);

    Console.WriteLine($"provider={request.Provider}");
    Console.WriteLine($"result-digest={correctness.ObservedResultDigestSha256}");
    Console.WriteLine($"round-trips={adapter.RoundTripObserver?.Snapshot()}");
    Console.WriteLine($"instrumentation={adapter.RoundTripObserver?.Instrumentation}");
    return 0;
}
