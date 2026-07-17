using System.Reflection;
using System.Text.Json;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentityPerformanceWorkloadTests
{
    private const string WorkloadTypeName = "Elsa.Persistence.Groundwork.Conformance.Tests.AspNetCoreIdentityPerformanceWorkload";
    private const string EfBaselineTypeName = "Elsa.Persistence.Groundwork.Conformance.Tests.AspNetCoreIdentityEfContractBaseline";
    private const string WorkloadId = "iam-normalized-lookup-update";
    private const string ScenarioId = "identity-authority-baseline";
    private const string ExpectedResultDigest = "32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc";
    private const string ExpectedInputFingerprint = "5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9";

    [Fact]
    public void Workload_declares_deterministic_identity_and_public_digest_contract()
    {
        var workload = RequiredType(WorkloadTypeName);

        Assert.Equal(WorkloadId, RequiredConstant(workload, "WorkloadId"));
        Assert.Equal(ScenarioId, RequiredConstant(workload, "ScenarioId"));
        Assert.Equal(ExpectedResultDigest, RequiredConstant(workload, "ExpectedResultDigest"));
        Assert.Equal(ExpectedInputFingerprint, RequiredConstant(workload, "ExpectedInputFingerprint"));
        var executeMethods = workload.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == "ExecuteAsync")
            .ToArray();
        var execute = Assert.Single(executeMethods);
        Assert.Equal(typeof(AspNetCoreIdentityPhysicalWorkloadTarget), execute.GetParameters()[0].ParameterType);
        Assert.DoesNotContain(execute.GetParameters(), parameter => parameter.ParameterType == typeof(string));
        Assert.Null(typeof(AspNetCoreIdentityPhysicalWorkloadTarget).GetProperty("Provider"));
        Assert.DoesNotContain(executeMethods, method =>
            method.GetParameters().Length == 0 ||
            method.GetParameters()[0].ParameterType == typeof(CancellationToken));
    }

    [Fact]
    public async Task Workload_rejects_a_client_that_was_not_opened_by_its_owning_provider_driver()
    {
        await using var client = new GroundworkProviderClient(
            Guid.NewGuid(),
            new EmptyServiceProvider(),
            new InMemoryDocumentStore(IdentityStorageManifest.Create()),
            static () => ValueTask.CompletedTask);
        var target = new AspNetCoreIdentityPhysicalWorkloadTarget(client, []);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AspNetCoreIdentityPerformanceWorkload().ExecuteAsync(target).AsTask());

        Assert.Contains("not opened and owned", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Workload_input_fingerprint_is_derived_from_the_versioned_json_definition()
    {
        Assert.Equal(ExpectedInputFingerprint, AspNetCoreIdentityPerformanceWorkload.ComputeInputFingerprint());

        var path = Path.Combine(
            RepositoryRoot(),
            "specs",
            "094-harden-groundwork-stores",
            "workloads",
            "iam-secrets.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var workload = Assert.Single(json.RootElement.GetProperty("workloads").EnumerateArray());
        var input = workload.GetProperty("input");
        var definition = AspNetCoreIdentityObservableScenario.InputDefinition;

        Assert.Equal(definition.Seed, input.GetProperty("seed").GetString());
        Assert.Equal(ExpectedInputFingerprint, input.GetProperty("fingerprintSha256").GetString());
        Assert.Equal(definition.TenantCount, input.GetProperty("tenantCount").GetInt32());
        Assert.Equal(definition.CanonicalUserCount, input.GetProperty("canonicalUserCount").GetInt32());
        Assert.Equal(definition.NoiseUserCount, input.GetProperty("noiseUserCount").GetInt32());
        Assert.Equal(definition.RoleCount, input.GetProperty("roleCount").GetInt32());
        Assert.Equal(definition.UserRoleLinkCount, input.GetProperty("userRoleLinkCount").GetInt32());
        Assert.Equal(definition.ConcurrentContenders, input.GetProperty("concurrentContenders").GetInt32());
        Assert.Equal(
            definition.OperationSequence,
            workload.GetProperty("operationSequence").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    [Fact]
    public void Workload_declares_all_provider_prerequisites_and_native_route_evidence_requirements()
    {
        var workload = RequiredType(WorkloadTypeName);

        Assert.Equal(
            new[] { "sqlite", "sqlserver", "postgresql", "mongodb" },
            RequiredStringArray(workload, "RequiredProviders"));
        Assert.Contains("find-user-by-normalized-name", RequiredStringArray(workload, "RequiredNativeRoutes"));
        Assert.Contains("find-user-by-normalized-email", RequiredStringArray(workload, "RequiredNativeRoutes"));
        Assert.Contains("find-role-by-normalized-name", RequiredStringArray(workload, "RequiredNativeRoutes"));
        Assert.Contains("list-user-roles", RequiredStringArray(workload, "RequiredNativeRoutes"));
        Assert.Contains("list-role-users", RequiredStringArray(workload, "RequiredNativeRoutes"));
    }

    [Fact]
    public void Workload_rejects_native_evidence_below_the_ratified_physical_cardinality()
    {
        var evidence = NativeEvidence(99_999, 1);

        Assert.Throws<InvalidOperationException>(() =>
            AspNetCoreIdentityPerformanceWorkload.ValidateNativeRouteEvidence("sqlite", evidence));
    }

    [Fact]
    public void Workload_rejects_native_evidence_that_did_not_materialize_exactly_one_candidate()
    {
        var evidence = NativeEvidence(AspNetCoreIdentityPerformanceWorkload.NativeEvidenceAcceptanceCardinality, 0);

        Assert.Throws<InvalidOperationException>(() =>
            AspNetCoreIdentityPerformanceWorkload.ValidateNativeRouteEvidence("sqlite", evidence));
    }

    [Fact]
    public void Workload_result_reports_observations_and_native_evidence_without_fabricated_materialization_counts()
    {
        var result = typeof(AspNetCoreIdentityWorkloadResult);

        Assert.NotNull(result.GetProperty("ProviderIdentity"));
        Assert.NotNull(result.GetProperty("InputFingerprint"));
        Assert.NotNull(result.GetProperty("ResultDigest"));
        Assert.NotNull(result.GetProperty("ObservableOperations"));
        Assert.NotNull(result.GetProperty("ObservableResults"));
        Assert.NotNull(result.GetProperty("NativeRouteEvidence"));
        Assert.Null(result.GetProperty("MaterializedCandidateCounts"));
    }

    [Fact]
    public void Frozen_ef_baseline_is_an_explicit_non_executed_contract_snapshot()
    {
        var baseline = RequiredType(EfBaselineTypeName);

        Assert.Equal("frozen-ef-contract-snapshot", RequiredConstant(baseline, "BaselineIdentity"));
        Assert.Equal("not-executed", RequiredConstant(baseline, "ExecutionStatus"));
        Assert.Equal("#646", RequiredConstant(baseline, "ExecutionOwner"));
        Assert.Equal(WorkloadId, RequiredConstant(baseline, "WorkloadId"));
        Assert.Equal(ExpectedInputFingerprint, RequiredConstant(baseline, "ExpectedInputFingerprint"));
        Assert.Equal(ExpectedResultDigest, RequiredConstant(baseline, "ExpectedResultDigest"));
        Assert.Null(baseline.GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(baseline.GetProperty("MutatesEfSurface", BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void Workload_does_not_claim_a_local_timing_or_ef_equality_gate()
    {
        var workload = RequiredType(WorkloadTypeName);

        Assert.Null(workload.GetMethod("AssertCorrectnessBeforeTimingAsync", BindingFlags.Instance | BindingFlags.Public));
    }

    private static Type RequiredType(string fullName)
    {
        var type = typeof(AspNetCoreIdentityPerformanceWorkloadTests).Assembly.GetType(fullName, throwOnError: false);
        Assert.NotNull(type);
        return type!;
    }

    private static string RequiredConstant(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.True(field!.IsLiteral);
        return Assert.IsType<string>(field.GetRawConstantValue());
    }

    private static string[] RequiredStringArray(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<IEnumerable<string>>(property!.GetValue(null)).ToArray();
    }

    private static GroundworkNativeRoutePlanResult[] NativeEvidence(int physicalCardinality, int materializedCandidateCount) =>
        AspNetCoreIdentityObservableScenario.NativeRouteLimits.Select(route =>
        {
            var request = new GroundworkNativeRoutePlanRequest(
                "identity-test-document",
                route.Key,
                "identity_test_documents",
                "normalized_key",
                ["normalized_key"],
                "tenant-alpha",
                "route-target",
                route.Value,
                physicalCardinality);
            return GroundworkNativeRoutePlanResult.Create(
                request,
                "sqlite",
                physicalCardinality,
                "index-seek",
                "ix_identity_test_documents_normalized_key",
                route.Value,
                materializedCandidateCount,
                NativeCommands());
        }).ToArray();

    private static IReadOnlyList<GroundworkNativeRouteCommandEvidence> NativeCommands() =>
    [
        NativeCommand(0, PhysicalDocumentQueryCommandKind.Count, PhysicalDocumentQueryCommandIdentities.Count),
        NativeCommand(1, PhysicalDocumentQueryCommandKind.Page, PhysicalDocumentQueryCommandIdentities.Page)
    ];

    private static GroundworkNativeRouteCommandEvidence NativeCommand(
        int ordinal,
        PhysicalDocumentQueryCommandKind kind,
        string identity) =>
        GroundworkNativeRouteCommandEvidence.Create(
            ordinal,
            new PhysicalDocumentQueryCommandExplanation(
                kind,
                identity,
                "sqlite-query-plan",
                "SEARCH ix_identity_test_documents_normalized_key",
                ["normalized_key", "storage_scope"]),
            "index-search",
            ["ix_identity_test_documents_normalized_key"]);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

}
