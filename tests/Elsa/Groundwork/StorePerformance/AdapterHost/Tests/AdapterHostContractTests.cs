using System.Text.Json;
using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

/// <summary>
/// Offline contract tests for the adapter child host. Every fact here is deliberately reachable without a
/// database, a container or a matrix run: each one guards a surface that otherwise fails closed minutes
/// into a real cohort, on a machine that has already started four provider containers.
/// </summary>
public sealed class AdapterHostContractTests : IDisposable
{
    private const string WorkloadId = "checkpoint-commit";
    private const string Provider = "sqlite";

    private readonly WorkloadCatalog _catalog = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot());
    private readonly string _directory = Directory.CreateTempSubdirectory("elsa-adapter-host-tests").FullName;

    private PerformanceWorkload Workload => _catalog.Workloads[WorkloadId];

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// Asserted on the serialized form rather than with record equality: <c>RunRequest</c> carries two
    /// dictionaries, and a positional record compares those by reference, so <c>Assert.Equal</c> on the
    /// records would fail for a perfectly correct round trip. Byte equality of the re-serialized JSON is
    /// also the stronger claim — it proves no field was dropped, reordered or coerced.
    /// </summary>
    [Fact]
    public void Run_request_round_trips_through_the_harness_wire_format()
    {
        var request = CreateRequest(ProcessKind.Measured, 2);
        var json = RunRequestWire.Serialize(request);

        var parsed = RunRequestWire.Parse(json);

        Assert.Equal(json, RunRequestWire.Serialize(parsed));
        Assert.Equal(ProcessKind.Measured, parsed.ProcessKind);
        Assert.Equal(2, parsed.ProcessIndex);
        Assert.Equal(request.ProviderConfiguration, parsed.ProviderConfiguration);
        Assert.Equal(request.PackageVersions, parsed.PackageVersions);
    }

    /// <summary>
    /// The single nastiest wire trap: <c>ArtifactStore.JsonOptions</c> registers no string-enum converter,
    /// so a host that assumed <c>"Warmup"</c>/<c>"Measured"</c> strings would fail to parse — or, if it
    /// were lenient, silently mistake a warmup child for a measured one and emit timed samples that the
    /// harness rejects only after the whole set has run.
    /// </summary>
    [Theory]
    [InlineData(ProcessKind.Warmup, 0)]
    [InlineData(ProcessKind.Measured, 1)]
    public void Process_kind_travels_as_a_number_not_a_string(ProcessKind kind, int index)
    {
        using var document = JsonDocument.Parse(RunRequestWire.Serialize(CreateRequest(kind, index)));

        var processKind = document.RootElement.GetProperty("ProcessKind");

        Assert.Equal(JsonValueKind.Number, processKind.ValueKind);
        Assert.Equal((int)kind, processKind.GetInt32());
    }

    [Fact]
    public void Duplicate_properties_in_a_run_request_are_rejected()
    {
        var json = RunRequestWire.Serialize(CreateRequest(ProcessKind.Measured, 1));
        var duplicated = json.Insert(json.IndexOf('{') + 1, "\"Provider\": \"sqlite\", \"Provider\": \"postgresql\",");

        Assert.Throws<PerformanceContractException>(() => RunRequestWire.Parse(duplicated));
    }

    /// <summary>
    /// The highest-value offline fact. <c>ValidateCorrectness</c> binds roughly eighteen provenance fields
    /// between the request and the staged evidence document, and it demands that document even when the
    /// workload declares no native routes — the routes are only part of the binding. Proving a routeless
    /// document is accepted here is what lets `checkpoint-commit` be the first end-to-end slice without
    /// the whole route-capture and raw-plan-sanitization subsystem existing yet.
    /// </summary>
    [Fact]
    public void Routeless_staged_evidence_satisfies_correctness_admission()
    {
        Assert.Empty(Workload.RequiredNativeRoutes);
        var request = CreateRequest(ProcessKind.Measured, 1);
        var published = NativePlanEvidenceStaging.PublishInto(_directory, request);

        ArtifactAdmission.ValidateCorrectness(Workload, request, CreateEvidence(request, published), _directory);
    }

    [Fact]
    public void Evidence_whose_digest_does_not_match_the_matrix_commitment_is_refused()
    {
        var request = CreateRequest(ProcessKind.Measured, 1) with { NativePlanContentSha256 = new string('b', 64) };

        var exception = Assert.Throws<PerformanceContractException>(() => NativePlanEvidenceStaging.PublishInto(_directory, request));

        Assert.Contains("--native-plan-sha256", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Four children share one artifact directory and the runner re-validates every file after each of
    /// them, so publishing must be idempotent: the second child has to find the first child's bytes, not
    /// rewrite them.
    /// </summary>
    [Fact]
    public void Publishing_evidence_twice_leaves_the_bytes_untouched()
    {
        var request = CreateRequest(ProcessKind.Measured, 1);
        NativePlanEvidenceStaging.PublishInto(_directory, request);
        var path = Path.Combine(_directory, request.NativePlanEvidenceReference);
        var first = File.ReadAllBytes(path);

        NativePlanEvidenceStaging.PublishInto(_directory, CreateRequest(ProcessKind.Measured, 2));

        Assert.Equal(first, File.ReadAllBytes(path));
    }

    /// <summary>
    /// An unregistered workload must be a hard failure. The harness's contract — "a missing adapter is a
    /// blocked run, never a simulated result" — only holds if the host refuses to improvise.
    /// </summary>
    [Fact]
    public async Task An_unregistered_workload_is_a_blocked_run_not_a_fallback()
    {
        var context = new AdapterContext(CreateRequest(ProcessKind.Measured, 1), Workload);

        var exception = await Assert.ThrowsAsync<PerformanceContractException>(
            async () => await BenchmarkAdapterFactory.CreateAsync(context, CancellationToken.None));

        Assert.Contains("blocked run", exception.Message, StringComparison.Ordinal);
    }

    private RunRequest CreateRequest(ProcessKind kind, int index)
    {
        var reference = NativePlanEvidenceStaging.ReferenceFor(WorkloadId, Provider);
        return new RunRequest(
            "tierb-001",
            "groundwork-shared-linked",
            Workload.Id,
            Workload.Version,
            Provider,
            "groundwork",
            "shared-documents-with-linked-index-tables",
            "100k",
            new string('0', 40),
            SourceProvenance.HarnessAssemblySha256(),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Groundwork.Sqlite"] = "0.0.1-preview.103" },
            new string('a', 64),
            HostFingerprint.CaptureSha256(),
            "3.50.4",
            Workload.RequiredProviderEvidence[Provider],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["journalMode"] = "wal", ["synchronous"] = "full" },
            Workload.Input.Seed,
            Workload.Input.FingerprintSha256,
            "checkpoint-commit-sqlite-routeless",
            reference,
            StagedDigest(reference),
            kind,
            index);
    }

    /// <summary>
    /// Stages the document the way <c>capture-plan</c> does, then hands back its digest so the request can
    /// commit to it exactly as the operator does on the matrix command line.
    /// </summary>
    private string StagedDigest(string reference)
    {
        var staging = Path.Combine(_directory, "staging");
        var path = Path.Combine(staging, reference);
        if (!File.Exists(path))
        {
            NativePlanEvidenceStaging.Write(staging, CreateDocument());
            Environment.SetEnvironmentVariable(NativePlanEvidenceStaging.StagingDirectoryVariable, staging);
        }
        return NativePlanEvidenceStaging.Sha256(path);
    }

    private NativePlanEvidenceDocument CreateDocument() => new(
        2,
        "tierb-001",
        "groundwork-shared-linked",
        Workload.Id,
        Workload.Version,
        Provider,
        "groundwork",
        "shared-documents-with-linked-index-tables",
        "100k",
        new string('0', 40),
        SourceProvenance.HarnessAssemblySha256(),
        new string('a', 64),
        HostFingerprint.CaptureSha256(),
        "3.50.4",
        Workload.RequiredProviderEvidence[Provider],
        new Dictionary<string, string>(StringComparer.Ordinal) { ["journalMode"] = "wal", ["synchronous"] = "full" },
        Workload.Input.Seed,
        Workload.Input.FingerprintSha256,
        "checkpoint-commit-sqlite-routeless",
        []);

    private CorrectnessEvidence CreateEvidence(RunRequest request, NativePlanEvidenceDocument document) => new(
        Workload.Correctness.ResultDigestSha256,
        request.ProviderVersion,
        request.ProviderTopology,
        request.ProviderConfiguration,
        new NativePlanEvidence(document.Identity, request.NativePlanEvidenceReference, request.NativePlanContentSha256, document.Routes));
}
