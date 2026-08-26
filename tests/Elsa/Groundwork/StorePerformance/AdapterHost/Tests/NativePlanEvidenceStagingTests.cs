using System.Text.Json;
using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
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
        Assert.Empty(Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".native-plan.json", StringComparison.Ordinal)));
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
            NativePlanEvidenceStaging.ReferenceFor(workloadId, "sqlite"));
    }

    [Fact]
    public void A_zero_route_document_is_published_because_checkpoint_commit_declares_no_native_routes()
    {
        var reference = Stage(Document());

        var document = NativePlanEvidenceStaging.PublishInto(output, Request(reference));

        Assert.Empty(document.Routes);
        Assert.True(File.Exists(Path.Combine(output, reference)));
    }

    private string Stage(NativePlanEvidenceDocument document)
    {
        NativePlanEvidenceStaging.Write(staging, document);
        return NativePlanEvidenceStaging.ReferenceFor(document.WorkloadId, document.Provider);
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

    private static NativePlanEvidenceDocument Document(string? rawPlanReference = null) => new(
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
        Routes: rawPlanReference is null
            ? []
            : [new NativeRouteEvidence(
                RouteIdentity: "route",
                RawPlanReference: rawPlanReference,
                RawPlanSha256: new string('f', 64),
                PlanClassification: "index-seek",
                IndexName: "index",
                PhysicalCardinality: 1,
                HasStorageScopePredicate: true,
                HasRoutePredicate: true,
                FiniteLimit: 1,
                MaterializedCandidateCount: 1)]);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(NativePlanEvidenceStaging.StagingDirectoryVariable, null);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
