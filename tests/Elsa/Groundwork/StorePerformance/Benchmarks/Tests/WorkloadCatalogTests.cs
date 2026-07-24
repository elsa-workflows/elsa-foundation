using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class WorkloadCatalogTests
{
    [Fact]
    public void Loads_the_exact_twelve_versioned_spec_094_workloads()
    {
        var catalog = WorkloadCatalog.Load(Repository.Root());

        Assert.Equal(12, catalog.Workloads.Count);
        Assert.Equal(Expected.Keys.Order(StringComparer.Ordinal), catalog.Workloads.Keys.Order(StringComparer.Ordinal));
        foreach (var (id, expected) in Expected)
        {
            var actual = catalog.Workloads[id];
            Assert.Equal(expected.CoverageRows, actual.CoverageRows);
            Assert.Equal(expected.PhysicalForms, actual.PhysicalFormsFor646);
            Assert.Equal(["sqlite", "sqlserver", "postgresql", "mongodb"], actual.RequiredProviders);
            Assert.Equal(["mongodb", "postgresql", "sqlite", "sqlserver"], actual.RequiredProviderEvidence.Keys.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Ten_v1_1_successors_are_mechanically_reproducible_and_secret_remains_blocked()
    {
        var catalog = WorkloadCatalog.Load(Repository.Root());

        Assert.Equal(10, ReproducibleWorkloadScenarioCatalog.Successors.Count);
        foreach (var (id, scenario) in ReproducibleWorkloadScenarioCatalog.Successors)
        {
            var workload = catalog.Workloads[id];
            Assert.Equal("1.1.0", workload.Version);
            Assert.Equal(scenario.Seed, workload.Input.Seed);
            Assert.Equal(scenario.OperationSequence, workload.OperationSequence);
            Assert.Equal(scenario.ComputeInputFingerprint(), workload.Input.FingerprintSha256);
            Assert.Equal(scenario.ComputeResultDigest(), workload.Correctness.ResultDigestSha256);
            Assert.NotEmpty(scenario.CreateExpectedObservations());
        }

        var secret = catalog.Workloads[ReproducibleWorkloadScenarioCatalog.BlockedWorkloadId];
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.BlockedVersion, secret.Version);
        Assert.DoesNotContain(secret.Id, ReproducibleWorkloadScenarioCatalog.Successors.Keys);
        Assert.Contains("real EF Secret repository comparator", ReproducibleWorkloadScenarioCatalog.BlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_workload_file_with_an_unknown_or_missing_contract_property()
    {
        using var fixture = WorkloadFixture.CopyFromRepository();
        fixture.Replace("\"handoffTarget\": \"#646\"", "\"handoffTarget\": \"#646\", \"unreviewed\": true");

        var error = Assert.Throws<WorkloadContractException>(() => WorkloadCatalog.Load(fixture.Root));

        Assert.Contains("unknown", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_any_drift_from_the_frozen_spec_094_contract()
    {
        using var fixture = WorkloadFixture.CopyFromRepository();
        fixture.Replace("\"version\": \"1.1.0\"", "\"version\": \"9.9.9\"");

        var error = Assert.Throws<WorkloadContractException>(() => WorkloadCatalog.Load(fixture.Root));

        Assert.Contains("does not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ExpectedWorkload(string[] CoverageRows, string[] PhysicalForms);

    private static readonly IReadOnlyDictionary<string, ExpectedWorkload> Expected =
        new Dictionary<string, ExpectedWorkload>(StringComparer.Ordinal)
        {
            ["checkpoint-commit"] = new(["runtime-activity-execution-state", "runtime-checkpoint-commit", "runtime-durable-value-state", "runtime-workflow-executable"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "checkpoint-unit-of-work-with-linked-outbox"]),
            ["bookmark-lookup"] = new(["runtime-bookmark-state"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables"]),
            ["trigger-binding-stimulus-lookup"] = new(["runtime-executable-source-reference", "runtime-trigger-binding"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "linked-executable-source-reference-index"]),
            ["recovery-scan"] = new(["runtime-execution-liveness", "runtime-incident-state", "runtime-scheduler-state", "runtime-workflow-execution-state", "runtime-workflow-hold-state"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "recovery-candidate-index"]),
            ["queue-drain"] = new(["runtime-scheduler-poison", "runtime-scheduler-work-queue"], ["dedicated-scheduler-work-documents", "dedicated-scheduler-poison-documents", "shared-documents-with-linked-index-tables"]),
            ["outbox-drain"] = new(["runtime-post-commit-outbox"], ["dedicated-post-commit-outbox-documents", "shared-documents-with-linked-index-tables", "due-order-index"]),
            ["due-timer-selection"] = new(["runtime-durable-timer"], ["dedicated-durable-timer-documents", "shared-documents-with-linked-index-tables", "due-order-index"]),
            ["recurring-schedule-selection"] = new(["runtime-publication-projection-state", "runtime-recurring-trigger-schedule"], ["dedicated-recurring-schedule-documents", "publication-projection-documents", "shared-documents-with-linked-index-tables"]),
            ["iam-normalized-lookup-update"] = new(["iam-application", "iam-claim-mapping", "iam-credential", "iam-external-identity", "iam-provider-configuration-tenant", "iam-role", "iam-tenant-membership", "iam-user"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "entity-type-specific-physical-tables-current-identity-shape"]),
            ["secret-create-read-list"] = new(["secrets-repository"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "entity-type-specific-physical-tables"]),
            ["placement-takeover"] = new(["distributed-execution-placement"], ["dedicated-placement-lease-documents", "shared-documents-with-linked-index-tables", "placement-owner-expiry-index"]),
            ["command-send-lease-ack"] = new(["distributed-command-transport"], ["dedicated-command-transport-documents", "stream-head-documents", "shared-documents-with-linked-index-tables", "visibility-order-index"])
        };
}
