using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class IamAdapterAdmissionTests
{
    [Theory]
    [InlineData("ef-aspnetcore-identity", "ef-identity-relational-schema")]
    [InlineData("groundwork-aspnetcore-identity", "entity-type-specific-physical-tables-current-identity-shape")]
    [InlineData("unratified-adapter", "unratified-form")]
    public void Candidate_iam_adapter_form_pair_is_rejected_without_a_ratified_mapping(
        string adapter,
        string physicalForm)
    {
        var error = Assert.Throws<PerformanceContractException>(() =>
            BenchmarkAdapterAdmission.RequireAdmitted(IamWorkload(), adapter, physicalForm));

        Assert.Contains("iam.adapter-form.ratification-required", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Matrix_plan_rejects_the_proposed_ef_profile_before_the_frozen_form_allowlist()
    {
        var workload = IamWorkload();
        var request = new MatrixRequest(
            "iam-admission-cohort",
            "iam-admission-set",
            workload.Id,
            workload.Version,
            "sqlite",
            "ef-aspnetcore-identity",
            "ef-identity-relational-schema",
            "100k",
            new string('a', 40),
            new string('b', 64),
            new Dictionary<string, string> { ["Microsoft.EntityFrameworkCore"] = "candidate" },
            new string('c', 64),
            new string('d', 64),
            "3.46.0",
            "file-backed-distinct-connections",
            new Dictionary<string, string> { ["journal_mode"] = "candidate" },
            workload.Input.Seed,
            workload.Input.FingerprintSha256,
            "candidate-native-plan",
            "iam-admission.native-plan.json",
            new string('e', 64));

        var error = Assert.Throws<PerformanceContractException>(() => MatrixPlan.Create(workload, request));

        Assert.Contains("iam.adapter-form.ratification-required", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("frozen workload/provider/form contract", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ef-aspnetcore-identity", "ef-identity-relational-schema")]
    [InlineData("groundwork-aspnetcore-identity", "entity-type-specific-physical-tables-current-identity-shape")]
    [InlineData("unratified-adapter", "unratified-form")]
    public async Task Candidate_iam_pair_is_rejected_before_adapter_preparation_or_provider_access(
        string adapterName,
        string physicalForm)
    {
        await using var adapter = new NeverPreparedAdapter();

        var error = await Assert.ThrowsAsync<PerformanceContractException>(() =>
            ProcessMeasurement.ExecuteAsync(
                IamWorkload(),
                Request(adapterName, physicalForm),
                BenchmarkProtocol.Acceptance,
                adapter,
                Path.Combine(Path.GetTempPath(), $"elsa646-iam-admission-{Guid.NewGuid():N}"),
                CancellationToken.None));

        Assert.Contains("iam.adapter-form.ratification-required", error.Message, StringComparison.Ordinal);
        Assert.False(adapter.PrepareCalled);
        Assert.False(adapter.ProviderAccessed);
    }

    [Theory]
    [InlineData("ef-aspnetcore-identity", "ef-identity-relational-schema")]
    [InlineData("groundwork-aspnetcore-identity", "entity-type-specific-physical-tables-current-identity-shape")]
    [InlineData("unratified-adapter", "unratified-form")]
    public async Task Candidate_iam_pair_is_rejected_before_child_launch_or_artifact_creation(
        string adapter,
        string physicalForm)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa646-iam-admission-{Guid.NewGuid():N}");
        var request = Request(adapter, physicalForm) with { ProcessKind = ProcessKind.Warmup, ProcessIndex = 0 };
        var plan = new MatrixPlan(IamWorkload(), BenchmarkProtocol.Acceptance, [request]);
        var childCalled = false;

        try
        {
            var error = await Assert.ThrowsAsync<PerformanceContractException>(() =>
                ProcessMatrixRunner.RunForTestAsync(
                    plan,
                    directory,
                    (_, outputDirectory, _) =>
                    {
                        childCalled = true;
                        Directory.CreateDirectory(outputDirectory);
                        File.WriteAllText(Path.Combine(outputDirectory, "unexpected-artifact"), "must-not-exist");
                        return Task.FromResult(0);
                    },
                    CancellationToken.None));

            Assert.Contains("iam.adapter-form.ratification-required", error.Message, StringComparison.Ordinal);
            Assert.False(childCalled);
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("ef-aspnetcore-identity", "ef-identity-relational-schema")]
    [InlineData("groundwork-aspnetcore-identity", "entity-type-specific-physical-tables-current-identity-shape")]
    [InlineData("unratified-adapter", "unratified-form")]
    public void Candidate_iam_pair_is_rejected_before_direct_artifact_creation(
        string adapter,
        string physicalForm)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa646-iam-admission-{Guid.NewGuid():N}");
        var artifact = new ProcessArtifact(
            0,
            Request(adapter, physicalForm),
            BenchmarkProtocol.Acceptance,
            false,
            null!,
            [],
            null!);

        try
        {
            var error = Assert.Throws<PerformanceContractException>(() => ArtifactStore.Write(directory, artifact));

            Assert.Contains("iam.adapter-form.ratification-required", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("ef-aspnetcore-identity", "ef-identity-relational-schema")]
    [InlineData("groundwork-aspnetcore-identity", "entity-type-specific-physical-tables-current-identity-shape")]
    [InlineData("unratified-adapter", "unratified-form")]
    public void Forged_complete_iam_comparison_cannot_bypass_the_gate(
        string adapter,
        string physicalForm)
    {
        var comparison = new ComparisonResult(
            1,
            new string('a', 64),
            "iam-normalized-lookup-update",
            "1.1.0",
            "sqlite",
            "100k",
            $"sqlite/ef-aspnetcore-identity/ef-identity-relational-schema",
            $"sqlite/{adapter}/{physicalForm}",
            true,
            true,
            [],
            [],
            null);

        var gate = GateEvaluator.Evaluate(GatePolicy.DefaultFor(GateClass.OrdinaryStore), comparison);

        Assert.Equal(PerformanceVerdict.Blocked, gate.Verdict);
        Assert.Contains("iam.adapter-form.ratification-required", gate.Reason, StringComparison.Ordinal);
    }

    private static PerformanceWorkload IamWorkload() =>
        WorkloadCatalog.Load(Repository.Root()).Workloads["iam-normalized-lookup-update"];

    private static RunRequest Request(string adapter, string physicalForm) => new(
        "iam-admission-cohort",
        "iam-admission-set",
        "iam-normalized-lookup-update",
        "1.1.0",
        "sqlite",
        adapter,
        physicalForm,
        "100k",
        new string('a', 40),
        new string('b', 64),
        new Dictionary<string, string> { [adapter] = "candidate" },
        new string('c', 64),
        new string('d', 64),
        "3.46.0",
        "file-backed-distinct-connections",
        new Dictionary<string, string> { ["journal_mode"] = "candidate" },
        IamWorkload().Input.Seed,
        IamWorkload().Input.FingerprintSha256,
        "candidate-native-plan",
        "iam-admission.native-plan.json",
        new string('e', 64),
        ProcessKind.Warmup,
        0);

    private sealed class NeverPreparedAdapter : IBenchmarkAdapter
    {
        public bool PrepareCalled { get; private set; }
        public bool ProviderAccessed { get; private set; }
        public IReadOnlyList<IBenchmarkOperation> Operations => [];

        public Task PrepareAsync(CancellationToken cancellationToken)
        {
            PrepareCalled = true;
            ProviderAccessed = true;
            return Task.CompletedTask;
        }

        public Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider access must not be attempted for an unratified IAM mapping.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
