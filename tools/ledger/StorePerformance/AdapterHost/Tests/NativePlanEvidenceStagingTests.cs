using System.Text.Json;
using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

/// <summary>
/// Proves that a staged native-plan document cannot reach outside the staging and artifact roots.
///
/// The document is deserialized from disk, so every `RawPlanReference` it carries is untrusted input.
/// `ArtifactAdmission.ValidateCorrectness` does reject unsafe references — but only after `PublishInto`
/// has returned, so relying on it would mean the copy already happened by the time the reference is
/// refused. These tests assert the refusal happens *before* any filesystem effect, which is the property
/// that actually matters and the one a reader cannot confirm by looking at the later validation.
/// </summary>
public sealed class NativePlanEvidenceStagingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"adapter-host-staging-{Guid.NewGuid():N}");
    private readonly string staging;
    private readonly string output;

    public NativePlanEvidenceStagingTests()
    {
        staging = Path.Combine(root, "staging");
        output = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable(NativePlanEvidenceStaging.StagingDirectoryVariable, staging);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("../../outside.json")]
    [InlineData("nested/inside.json")]
    [InlineData("/etc/passwd.txt")]
    public void A_traversing_raw_plan_reference_is_refused_before_anything_is_copied(string rawPlanReference)
    {
        var escapee = Path.Combine(root, "outside.txt");
        File.WriteAllText(escapee, "must not be copied anywhere");

        var reference = Stage(Document(rawPlanReference));

        Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceStaging.PublishInto(output, Request(reference)));

        // The refusal has to precede the effect: a rejection that happens after the copy is not a rejection.
        Assert.DoesNotContain(
            Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories),
            path => !path.EndsWith(".native-plan.json", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(output, "outside.txt")));
        Assert.True(File.Exists(escapee), "the source outside the roots must be untouched");
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("/rooted")]
    public void A_document_whose_own_identity_would_escape_the_directory_is_refused(string workloadId)
    {
        // ReferenceFor composes the file name from the document's own fields, so those are an input path
        // too — not only the route references it carries.
        Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceStaging.ReferenceFor(workloadId, "sqlite", "set"));
    }

    [Fact]
    public void A_zero_route_document_is_published_because_checkpoint_commit_declares_no_native_routes()
    {
        var reference = Stage(Document());

        var document = NativePlanEvidenceStaging.PublishInto(output, Request(reference));

        Assert.Empty(document.Routes);
        Assert.True(File.Exists(Path.Combine(output, reference)));
    }

    [Fact]
    public void A_rawless_structured_route_is_published_without_a_raw_plan_artifact()
    {
        var reference = Stage(Document(
            rawPlanReference: "",
            structuredEvidence: StructuredEvidence(),
            routeIdentity: "structured-log-replay",
            rawPlanSha256: ""));

        var document = NativePlanEvidenceStaging.PublishInto(output, StructuredRequest(reference));

        Assert.Single(document.Routes);
        Assert.Equal("", document.Routes[0].RawPlanReference);
        Assert.Equal("", document.Routes[0].RawPlanSha256);
        Assert.NotNull(document.Routes[0].StructuredEvidence);
        Assert.Single(Directory.EnumerateFiles(output));
    }

    [Fact]
    public void A_rawless_recent_structured_route_is_published_without_a_raw_plan_artifact()
    {
        var reference = Stage(Document(
            rawPlanReference: "",
            structuredEvidence: StructuredEvidence(),
            routeIdentity: "structured-log-recent",
            rawPlanSha256: ""));

        var document = NativePlanEvidenceStaging.PublishInto(output, StructuredRequest(reference));

        Assert.Single(document.Routes);
        Assert.Equal("structured-log-recent", document.Routes[0].RouteIdentity);
        Assert.Empty(document.Routes[0].RawPlanReference);
        Assert.Empty(document.Routes[0].RawPlanSha256);
        Assert.NotNull(document.Routes[0].StructuredEvidence);
        Assert.Single(Directory.EnumerateFiles(output));
    }

    [Fact]
    public void A_structured_route_with_only_one_optional_raw_plan_field_is_rejected()
    {
        const string rawPlanReference = "structured-native-plan.txt";
        File.WriteAllText(Path.Combine(staging, rawPlanReference), "provider plan");
        var reference = Stage(Document(
            rawPlanReference,
            StructuredEvidence(),
            routeIdentity: "structured-log-replay",
            rawPlanSha256: ""));

        Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceStaging.PublishInto(output, StructuredRequest(reference)));
        Assert.False(File.Exists(Path.Combine(output, rawPlanReference)));
    }

    [Fact]
    public void An_unmigrated_route_still_requires_and_copies_its_raw_plan()
    {
        const string rawPlanReference = "legacy-native-plan.txt";
        File.WriteAllText(Path.Combine(staging, rawPlanReference), "provider plan");
        var reference = Stage(Document(rawPlanReference));

        NativePlanEvidenceStaging.PublishInto(output, Request(reference));

        Assert.True(File.Exists(Path.Combine(output, rawPlanReference)));
    }

    [Fact]
    public void A_null_route_is_rejected_as_a_performance_contract_error()
    {
        var reference = Stage(Document() with { Routes = new NativeRouteEvidence[] { null! } });

        Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceStaging.PublishInto(output, Request(reference)));
    }

    [Fact]
    public void A_null_route_list_is_rejected_as_a_performance_contract_error()
    {
        var reference = Stage(Document() with { Routes = null! });

        Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceStaging.PublishInto(output, Request(reference)));
    }

    [Fact]
    public void Checkpoint_document_records_that_zero_routes_are_the_frozen_contract()
    {
        var reference = Stage(Document());
        var request = Request(reference) with
        {
            ProviderVersion = "3.46.0",
            ProviderTopology = "file-backed-distinct-connections",
            ProviderConfiguration = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mode"] = "ReadWriteCreate",
                ["cache"] = "Shared",
                ["pooling"] = "True",
                ["journal_mode"] = "wal",
                ["synchronous"] = "1"
            }
        };
        var observed = new ProviderProbe.Result(
            "sqlite",
            "SqliteProviderConnection",
            request.ProviderVersion,
            request.ProviderTopology,
            request.ProviderConfiguration);

        var document = NativePlanEvidenceStaging.CreateCheckpointDocument(request, observed);

        Assert.Equal(2, document.SchemaVersion);
        Assert.Equal(NativePlanEvidenceStaging.NoNativeRoutesContract, document.RouteContract);
        Assert.Empty(document.Routes);
    }

    [Fact]
    public void Measurement_set_is_part_of_the_evidence_reference_and_cannot_overwrite_another_set()
    {
        var first = Document();
        var second = first with { MeasurementSetId = "set-2" };

        NativePlanEvidenceStaging.Write(staging, first);
        NativePlanEvidenceStaging.Write(staging, second);

        var firstReference = NativePlanEvidenceStaging.ReferenceFor(first.WorkloadId, first.Provider, first.MeasurementSetId);
        var secondReference = NativePlanEvidenceStaging.ReferenceFor(second.WorkloadId, second.Provider, second.MeasurementSetId);
        Assert.NotEqual(firstReference, secondReference);
        Assert.Equal(first.MeasurementSetId, NativePlanEvidenceStaging.Read(Path.Combine(staging, firstReference)).MeasurementSetId);
        Assert.Equal(second.MeasurementSetId, NativePlanEvidenceStaging.Read(Path.Combine(staging, secondReference)).MeasurementSetId);
    }

    private string Stage(NativePlanEvidenceDocument document)
    {
        NativePlanEvidenceStaging.Write(staging, document);
        return NativePlanEvidenceStaging.ReferenceFor(document.WorkloadId, document.Provider, document.MeasurementSetId);
    }

    private RunRequest Request(string reference)
    {
        var digest = NativePlanEvidenceStaging.Sha256(Path.Combine(staging, reference));
        return new RunRequest(
            ComparisonCohortId: "cohort",
            MeasurementSetId: "set",
            WorkloadId: "checkpoint-commit",
            WorkloadVersion: "1.0.0",
            Provider: "sqlite",
            ProviderVersion: "1.0.0",
            ProviderTopology: "single-node",
            ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal),
            Adapter: "groundwork-v2",
            PhysicalForm: "checkpoint-unit-of-work-with-linked-outbox",
            Scale: "small",
            CommitSha: new string('a', 40),
            CompositionFingerprint: new string('b', 64),
            HarnessAssemblySha256: new string('c', 64),
            HostFingerprintSha256: new string('d', 64),
            PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal),
            Seed: "seed",
            InputFingerprintSha256: new string('e', 64),
            NativePlanIdentity: "identity",
            NativePlanEvidenceReference: reference,
            NativePlanContentSha256: digest,
            ProcessKind: ProcessKind.Measured,
            ProcessIndex: 0);
    }

    private RunRequest StructuredRequest(string reference) =>
        Request(reference) with
        {
            WorkloadId = DiagnosticsDurableHistoryWorkload.WorkloadId,
            Adapter = DiagnosticsNativePlanContract.GroundworkAdapter
        };

    private static NativePlanEvidenceDocument Document(
        string? rawPlanReference = null,
        StructuredExecutionEvidence? structuredEvidence = null,
        string routeIdentity = "route",
        string? rawPlanSha256 = null) => new(
        SchemaVersion: 1,
        ComparisonCohortId: "cohort",
        MeasurementSetId: "set",
        WorkloadId: "checkpoint-commit",
        WorkloadVersion: "1.0.0",
        Provider: "sqlite",
        Adapter: "groundwork-v2",
        PhysicalForm: "checkpoint-unit-of-work-with-linked-outbox",
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('c', 64),
        CompositionFingerprint: new string('b', 64),
        HostFingerprintSha256: new string('d', 64),
        ProviderVersion: "1.0.0",
        ProviderTopology: "single-node",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal),
        Seed: "seed",
        InputFingerprintSha256: new string('e', 64),
        Identity: "identity",
        Routes: rawPlanReference is null && structuredEvidence is null
            ? []
            : [new NativeRouteEvidence(
                RouteIdentity: routeIdentity,
                RawPlanReference: rawPlanReference ?? "",
                RawPlanSha256: rawPlanSha256 ?? new string('f', 64),
                PlanClassification: "index-seek",
                IndexName: "index",
                PhysicalCardinality: 1,
                HasStorageScopePredicate: true,
                HasRoutePredicate: true,
                FiniteLimit: 1,
                MaterializedCandidateCount: 1)
            {
                StructuredEvidence = structuredEvidence
            }]);

    private static StructuredExecutionEvidence StructuredEvidence() => new(
        SchemaVersion: 1,
        Provider: "SQLite",
        ProviderVersion: "1.0.0",
        Operation: "BoundedQuery",
        CommandKind: "Read",
        Role: "Statement",
        Identity: new(
            CaptureId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            InvocationId: Guid.Parse("00000000-0000-0000-0000-000000000002"),
            CommandId: Guid.Parse("00000000-0000-0000-0000-000000000003"),
            StatementId: Guid.Parse("00000000-0000-0000-0000-000000000004"),
            CommandOrdinal: 0,
            StatementOrdinal: 0),
        Target: new(
            LogicalUnitId: "elsa-structured-logs",
            PhysicalTargetId: Guid.Parse("00000000-0000-0000-0000-000000000005"),
            ScopeBinding: "Predicate"),
        Outcome: "Succeeded",
        FailureCategory: null,
        ShapeAvailability: "Collected",
        BoundedQuery: null,
        Plan: new(
            Availability: "Collected",
            Provenance: "EstimatedExplain",
            ChoseExpectedIndex: true,
            ExpectedLogicalIndex: "index",
            ChosenPhysicalIndexId: Guid.Parse("00000000-0000-0000-0000-000000000006"),
            FailureCategory: null,
            CollectionCommandCount: 1,
            Nodes: []));

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(NativePlanEvidenceStaging.StagingDirectoryVariable, null);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
