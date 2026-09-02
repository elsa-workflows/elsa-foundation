using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class DistributedCommandSendLeaseAckAdapterTests
{
    [Fact]
    public async Task Runs_frozen_correctness_and_prepares_public_transport_operations_over_sqlite()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-command-adapter-{Guid.NewGuid():N}");
        var output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        var database = Path.Combine(root, "command.db");
        var connectionString = $"Data Source={database}";

        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString);
            var request = Request() with
            {
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };
            var reference = NativePlanEvidenceStaging.ReferenceFor(
                request.WorkloadId,
                request.Provider,
                request.MeasurementSetId);
            request = request with { NativePlanEvidenceReference = reference };
            var evidenceDigest = StageEvidence(output, request);
            request = request with { NativePlanContentSha256 = evidenceDigest };

            await using var adapter = new DistributedCommandSendLeaseAckAdapter(request, connectionString, output);
            await adapter.PrepareAsync(CancellationToken.None);
            Assert.Throws<PerformanceContractException>(() => adapter.Operations);

            var correctness = await adapter.VerifyCorrectnessAsync(CancellationToken.None);

            Assert.Equal(DistributedCommandSendLeaseAckWorkload.ExpectedResultDigest, correctness.ObservedResultDigestSha256);
            Assert.Equal(observed.Version, correctness.ObservedProviderVersion);
            Assert.Equal(observed.Topology, correctness.ObservedProviderTopology);
            Assert.Equal(observed.Configuration, correctness.ObservedProviderConfiguration);
            Assert.Equal(evidenceDigest, correctness.NativePlan.ContentSha256);
            Assert.Equal(3, correctness.NativePlan.Routes.Count);
            Assert.Equal("groundwork-v2:IProviderCommandObserver", adapter.RoundTripObserver!.Instrumentation);
            Assert.True(adapter.RoundTripObserver.IsExact);
            Assert.True(adapter.RoundTripObserver.Snapshot() > 0);
            Assert.Equal(
                [
                    "send-concurrent-commands",
                    "lease-visible-bounded-batch",
                    "re-lease-current-batch",
                    "attempt-stale-acknowledgement",
                    "acknowledge-current-batch",
                    "reopen-and-count-pending"
                ],
                adapter.Operations.Select(operation => operation.Id));

            foreach (var operation in adapter.Operations)
            {
                await operation.PrepareInvocationAsync(-1, CancellationToken.None);
                var before = adapter.RoundTripObserver.Snapshot();
                await operation.InvokeAsync(-1, CancellationToken.None);
                Assert.True(adapter.RoundTripObserver.Snapshot() > before, operation.Id);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string StageEvidence(string staging, RunRequest request)
    {
        var routes = new[]
        {
            Route(request, "lease-visible-commands-by-execution", 8),
            Route(request, "list-visible-command-executions", 128),
            Route(request, "count-pending-commands-by-execution", 128)
        };
        foreach (var route in routes)
        {
            var rawPath = Path.Combine(staging, route.RawPlanReference);
            File.WriteAllText(
                rawPath,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    route = route.RouteIdentity,
                    providerPlan = $"EXPLAIN {route.RouteIdentity} USING {route.IndexName}"
                }));
        }

        var document = new NativePlanEvidenceDocument(
            SchemaVersion: 2,
            ComparisonCohortId: request.ComparisonCohortId,
            MeasurementSetId: request.MeasurementSetId,
            WorkloadId: request.WorkloadId,
            WorkloadVersion: request.WorkloadVersion,
            Provider: request.Provider,
            Adapter: request.Adapter,
            PhysicalForm: request.PhysicalForm,
            Scale: request.Scale,
            CommitSha: request.CommitSha,
            HarnessAssemblySha256: request.HarnessAssemblySha256,
            CompositionFingerprint: request.CompositionFingerprint,
            HostFingerprintSha256: request.HostFingerprintSha256,
            ProviderVersion: request.ProviderVersion,
            ProviderTopology: request.ProviderTopology,
            ProviderConfiguration: request.ProviderConfiguration,
            Seed: request.Seed,
            InputFingerprintSha256: request.InputFingerprintSha256,
            Identity: request.NativePlanIdentity,
            Routes: routes);
        return NativePlanEvidenceStaging.Write(staging, document);
    }

    private static NativeRouteEvidence Route(RunRequest request, string identity, int finiteLimit)
    {
        var rawReference = $"{request.WorkloadId}.{request.Provider}.{request.MeasurementSetId}.{identity}.raw.json";
        var raw = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            route = identity,
            providerPlan = $"EXPLAIN {identity} USING ix-command-transport"
        });
        return new NativeRouteEvidence(
            RouteIdentity: identity,
            RawPlanReference: rawReference,
            RawPlanSha256: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant(),
            PlanClassification: "bounded-index-seek",
            IndexName: "ix-command-transport",
            PhysicalCardinality: 8192,
            HasStorageScopePredicate: true,
            HasRoutePredicate: true,
            FiniteLimit: finiteLimit,
            MaterializedCandidateCount: 1);
    }

    private static RunRequest Request() => new(
        ComparisonCohortId: "cohort",
        MeasurementSetId: "set",
        WorkloadId: DistributedCommandSendLeaseAckWorkload.WorkloadId,
        WorkloadVersion: "1.1.0",
        Provider: "sqlite",
        ProviderTopology: "file-backed-distinct-connections",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal),
        Adapter: "groundwork-v2",
        PhysicalForm: DistributedCommandSendLeaseAckAdapter.PhysicalForm,
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('b', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Groundwork.Store"] = "0.4.0-preview.1"
        },
        CompositionFingerprint: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        ProviderVersion: "3.0.0",
        Seed: "spec094-command-send-lease-ack-v1.1",
        InputFingerprintSha256: DistributedCommandSendLeaseAckWorkload.ExpectedInputFingerprint,
        NativePlanIdentity: "command-test-provenance",
        NativePlanEvidenceReference: "command-send-lease-ack.sqlite.set.native-plan.json",
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: 1);
}
