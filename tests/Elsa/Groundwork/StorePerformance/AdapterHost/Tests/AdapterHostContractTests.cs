using System.Text.Json;
using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
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

    [Theory]
    [InlineData("bookmark-lookup", "list-by-stimulus-and-type", "list-by-stimulus-type")]
    [InlineData("queue-drain", "list-pending-scheduler-workflow-executions", "list-by-workflow-execution")]
    [InlineData("outbox-drain", "list-claimable")]
    public void Routed_capture_contract_covers_the_frozen_workload_routes(
        string workloadId,
        params string[] routeIdentities)
    {
        var workload = _catalog.Workloads[workloadId];

        Assert.Equal(routeIdentities, RoutedNativePlanCapture.RequiredRouteIdentities(workload));
    }

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
    /// The runner accepts an artifact directory that is already partly populated, so "document present,
    /// raw plans absent" is a reachable state rather than a hypothetical. Reconciling raw plans only on the
    /// child that happened to copy the document would leave the rest missing, and correctness would then
    /// fail naming the raw plan instead of the real gap.
    /// </summary>
    [Fact]
    public void A_missing_raw_plan_is_restored_even_when_the_document_is_already_present()
    {
        var staging = Path.Combine(_directory, "routed-staging");
        var rawPlan = "checkpoint-commit.sqlite.route-a.txt";
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, rawPlan), "SCAN TABLE groundwork_documents USING INDEX ix_a");
        var reference = NativePlanEvidenceStaging.ReferenceFor(WorkloadId, Provider);
        NativePlanEvidenceStaging.Write(staging, CreateDocument() with
        {
            Routes = [new NativeRouteEvidence("route-a", rawPlan, new string('c', 64), "index-scan", "ix_a", 1, true, true, 10, 1)]
        });
        // CreateRequest stages the routeless document and repoints the staging variable, so the routed
        // staging directory has to win afterwards rather than before.
        var request = CreateRequest(ProcessKind.Measured, 1) with
        {
            NativePlanContentSha256 = NativePlanEvidenceStaging.Sha256(Path.Combine(staging, reference))
        };
        Environment.SetEnvironmentVariable(NativePlanEvidenceStaging.StagingDirectoryVariable, staging);
        NativePlanEvidenceStaging.PublishInto(_directory, request);
        File.Delete(Path.Combine(_directory, rawPlan));

        NativePlanEvidenceStaging.PublishInto(_directory, request);

        Assert.True(File.Exists(Path.Combine(_directory, rawPlan)), "the raw provider plan was not reconciled");
    }

    /// <summary>
    /// Malformed and duplicated settings must surface as contract failures, which the CLI turns into a
    /// clean message and exit code 2. Anything else escapes the handler as an unhandled exception and the
    /// operator gets a stack trace instead of the typo.
    /// </summary>
    [Theory]
    [InlineData("journalMode")]
    [InlineData("journalMode=")]
    public void A_malformed_provider_setting_is_a_contract_failure(string setting) =>
        Assert.Throws<PerformanceContractException>(
            () => HostArguments.Settings(["--provider-setting", setting], "--provider-setting"));

    [Fact]
    public void A_duplicated_provider_setting_is_rejected_rather_than_silently_overwritten()
    {
        string[] args = ["--provider-setting", "journalMode=wal", "--provider-setting", "journalMode=delete"];

        var exception = Assert.Throws<PerformanceContractException>(() => HostArguments.Settings(args, "--provider-setting"));

        Assert.Contains("journalMode", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unregistered workload must be a hard failure. The harness's contract — "a missing adapter is a
    /// blocked run, never a simulated result" — only holds if the host refuses to improvise.
    ///
    /// Driven through <c>due-timer-selection</c> rather than a registered runtime leaf: the
    /// point of the fact is the empty-registry path, so it has to be asked of a workload that is genuinely
    /// unregistered.
    /// </summary>
    [Fact]
    public async Task An_unregistered_workload_is_a_blocked_run_not_a_fallback()
    {
        var unregistered = _catalog.Workloads["due-timer-selection"];
        Assert.DoesNotContain(unregistered.Id, BenchmarkAdapterFactory.RegisteredWorkloads);
        var context = new AdapterContext(CreateRequest(ProcessKind.Measured, 1), unregistered);

        var exception = await Assert.ThrowsAsync<PerformanceContractException>(
            async () => await BenchmarkAdapterFactory.CreateAsync(context, CancellationToken.None));

        Assert.Contains("blocked run", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The registry is the whole reason the matrix can run at all, and it is keyed by workload id alone —
    /// so an entry that was renamed, or registered under a provider-qualified key, would leave every
    /// <c>checkpoint-commit</c> run blocked while looking registered from the outside.
    /// </summary>
    [Fact]
    public void The_checkpoint_commit_leaf_is_registered()
    {
        Assert.Contains(WorkloadId, BenchmarkAdapterFactory.RegisteredWorkloads);
        Assert.Equal(RuntimeCheckpointCommitWorkload.WorkloadId, Workload.Id);
    }

    [Fact]
    public void The_bookmark_lookup_leaf_is_registered()
    {
        Assert.Contains(RuntimeBookmarkLookupWorkload.WorkloadId, BenchmarkAdapterFactory.RegisteredWorkloads);
    }

    [Fact]
    public void The_queue_drain_leaf_is_registered() =>
        Assert.Contains(RuntimeQueueDrainWorkload.WorkloadId, BenchmarkAdapterFactory.RegisteredWorkloads);

    [Fact]
    public void The_outbox_drain_leaf_is_registered() =>
        Assert.Contains(RuntimeOutboxDrainWorkload.WorkloadId, BenchmarkAdapterFactory.RegisteredWorkloads);

    /// <summary>
    /// The leaf must reject a provider the frozen contract does not admit before it opens a driver. Deferring
    /// the check would start a container — and, for a typo'd provider key, fail with an
    /// <c>ArgumentOutOfRangeException</c> from the driver factory rather than a contract message.
    /// </summary>
    [Fact]
    public async Task A_provider_outside_the_frozen_topology_contract_is_refused_without_opening_a_driver()
    {
        var request = CreateRequest(ProcessKind.Measured, 1) with { Provider = "oracle" };
        var context = new AdapterContext(request, Workload);

        var exception = await Assert.ThrowsAsync<PerformanceContractException>(
            async () => await BenchmarkAdapterFactory.CreateAsync(context, CancellationToken.None));

        Assert.Contains("oracle", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every timed sample is labelled with its operation id, and the Tier B ceiling is read off per
    /// operation — so an id that drifts from the frozen <c>operationSequence</c> would quietly attribute a
    /// ceiling to the wrong phase. Order matters too: the harness measures in declaration order.
    /// </summary>
    [Fact]
    public void The_timed_operations_match_the_frozen_operation_sequence()
    {
        Assert.Equal(Workload.OperationSequence, CheckpointCommitAdapter.OperationIds);
    }

    /// <summary>
    /// Warm-up runs at <c>InvokeAsync(-1L - i)</c> and measurement at <c>0, 1, 2, …</c>. Both write real rows
    /// keyed by the ordinal, so an identity function that collapsed the two namespaces would have warm-up
    /// commits colliding with measured ones — surfacing as a replay conflict rather than as the aliasing bug
    /// it is.
    /// </summary>
    [Fact]
    public void Warmup_and_measured_ordinals_produce_disjoint_identity_keys()
    {
        var warmup = Enumerable.Range(0, 50).Select(index => CheckpointCommitAdapter.IdentityKey(-1L - index)).ToArray();
        var measured = Enumerable.Range(0, 50).Select(index => CheckpointCommitAdapter.IdentityKey(index)).ToArray();

        Assert.Equal(warmup.Length, warmup.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(measured.Length, measured.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(warmup.Intersect(measured, StringComparer.Ordinal));
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
