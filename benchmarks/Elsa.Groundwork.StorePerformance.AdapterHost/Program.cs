using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

// The matrix runner spawns this host once per process in a cohort. Every failure path exits non-zero with
// the contract message on stderr: the runner treats a child that cannot honour its request as a blocked
// run, and a host that degraded to a partial result would publish numbers describing something else.
try
{
    var command = args.Length > 0 ? args[0] : "";
    return command switch
    {
        "probe-provider" => ProbeProvider(args),
        "verify-correctness" => await VerifyCorrectness(args),
        "run" => await Run(args),
        _ => throw new PerformanceContractException(
            $"Unknown adapter-host command '{command}'. Supported: probe-provider, verify-correctness, run.")
    };
}
catch (PerformanceContractException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

// Reads the provider's own identity off a live connection. ValidateCorrectness binds the observed
// provider configuration to the requested one entry for entry, so these values must be read rather than
// guessed — this is the command whose output the operator pastes into capture-plan and matrix.
static int ProbeProvider(string[] args)
{
    var provider = HostArguments.Require(args, "probe-provider", "--provider");
    var connectionString = ProviderConnections.RequireConnectionString(provider);
    using var connection = ProviderConnections.Open(provider, connectionString);
    Console.WriteLine($"provider={provider}");
    Console.WriteLine($"connection-opened={connection.GetType().Name}");
    return 0;
}

// The measured path the matrix runner drives, writing the process artifact where the runner reads it
// after the child exits.
//
// Today this reaches ProcessMeasurement and stops at adapter.Operations, which refuses: the checkpoint
// leaf has no measured operation sequence yet. That is deliberate, and the non-zero exit says so, rather
// than the host inventing a sequence whose artifact would look well-formed and describe the wrong bundle.
static async Task<int> Run(string[] args)
{
    var (request, outputDirectory, connectionString) = ParseRun(args, "run");
    var workload = RequireWorkload(request.WorkloadId);
    await using var adapter = new CheckpointCommitAdapter(request, connectionString, outputDirectory);
    var artifact = await ProcessMeasurement.ExecuteAsync(
        workload, request, BenchmarkProtocol.Acceptance, adapter, outputDirectory, CancellationToken.None);
    ArtifactStore.Write(outputDirectory, artifact);
    return 0;
}

// Runs only the correctness baseline: compose, commit the catalog-owned bundle through the public stores,
// and validate the digest and the evidence bindings. It exists because ProcessMeasurement enumerates
// adapter.Operations for warmup processes as well as measured ones, so there is no way to reach
// correctness through `run` while the measured sequence is absent.
//
// This is the command that exercises the v2 seam end to end against a real provider, and it is what should
// be run on all four providers before anyone builds the timed operations on top of it.
static async Task<int> VerifyCorrectness(string[] args)
{
    var (request, outputDirectory, connectionString) = ParseRun(args, "verify-correctness");
    var workload = RequireWorkload(request.WorkloadId);
    ArtifactAdmission.ValidateRequest(workload, request);

    await using var adapter = new CheckpointCommitAdapter(request, connectionString, outputDirectory);
    await adapter.PrepareAsync(CancellationToken.None);
    var correctness = await adapter.VerifyCorrectnessAsync(CancellationToken.None);
    ArtifactAdmission.ValidateCorrectness(workload, request, correctness, outputDirectory);

    Console.WriteLine($"provider={request.Provider}");
    Console.WriteLine($"result-digest={correctness.ObservedResultDigestSha256}");
    Console.WriteLine($"round-trips={adapter.RoundTripObserver?.Snapshot()}");
    Console.WriteLine($"instrumentation={adapter.RoundTripObserver?.Instrumentation}");
    return 0;
}

// The catalog is loaded from the frozen spec directory and keyed by id; there is no single-workload
// accessor, and a missing id must fail here rather than surfacing as a KeyNotFoundException three frames
// deeper inside admission.
static PerformanceWorkload RequireWorkload(string workloadId)
{
    var catalog = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot());
    return catalog.Workloads.TryGetValue(workloadId, out var workload)
        ? workload
        : throw new PerformanceContractException($"Workload '{workloadId}' is not in the frozen catalog.");
}

static (RunRequest Request, string OutputDirectory, string ConnectionString) ParseRun(string[] args, string command)
{
    var request = RunRequestWire.Parse(HostArguments.Require(args, command, "--request"));
    var outputDirectory = HostArguments.Require(args, command, "--out");
    return (request, outputDirectory, ProviderConnections.RequireConnectionString(request.Provider));
}
