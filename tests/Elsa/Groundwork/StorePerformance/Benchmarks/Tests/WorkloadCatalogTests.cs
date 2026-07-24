using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
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
            Assert.Equal(expected.Version, actual.Version);
            Assert.Equal(expected.InputFingerprint, actual.Input.FingerprintSha256);
            Assert.Equal(expected.ResultDigest, actual.Correctness.ResultDigestSha256);
            Assert.Equal(expected.CoverageRows, actual.CoverageRows);
            Assert.Equal(expected.PhysicalForms, actual.PhysicalFormsFor646);
            Assert.Equal(["sqlite", "sqlserver", "postgresql", "mongodb"], actual.RequiredProviders);
            Assert.Equal(["mongodb", "postgresql", "sqlite", "sqlserver"], actual.RequiredProviderEvidence.Keys.Order(StringComparer.Ordinal));
        }
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
        fixture.Replace("\"version\": \"1.0.0\"", "\"version\": \"9.9.9\"");

        var error = Assert.Throws<WorkloadContractException>(() => WorkloadCatalog.Load(fixture.Root));

        Assert.Contains("does not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ExpectedWorkload(
        string Version,
        string InputFingerprint,
        string ResultDigest,
        string[] CoverageRows,
        string[] PhysicalForms);

    private static readonly IReadOnlyDictionary<string, ExpectedWorkload> Expected =
        new Dictionary<string, ExpectedWorkload>(StringComparer.Ordinal)
        {
            ["checkpoint-commit"] = new("1.0.0", "f59eef8b9359dc3623bbb42ce07c531f0f027170dc6d33e1788b1bd80dcdab93", "abaa23e9e4f3c9285f50a07f33d7569696ec4cfe1ac496575c12b45dbe78042a", ["runtime-activity-execution-state", "runtime-checkpoint-commit", "runtime-durable-value-state", "runtime-workflow-executable"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "checkpoint-unit-of-work-with-linked-outbox"]),
            ["bookmark-lookup"] = new("1.0.0", "c1b8a142e22e7c47449edc25c79cc2a83c5edb6dbbe4a884a730751038f3ae9a", "9f3d29edc4c3e64409f3fb9b64b4ec3e7d5e5064d8233be8afd92215ec3d680e", ["runtime-bookmark-state"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables"]),
            ["trigger-binding-stimulus-lookup"] = new("1.0.0", "cbd570ed8c80f996554853b1143fc34f634138b005858322d1a669dde2113b9a", "3c10eab69da70eccacc648780781ef57ad6499b91cb012465d154d6b1b7e9294", ["runtime-executable-source-reference", "runtime-trigger-binding"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "linked-executable-source-reference-index"]),
            ["recovery-scan"] = new("1.0.0", "7284c110669aaa3db7587893e9e31005af8c807d8323609aeb80cfd948d82b48", "06033b0de6f4784abc87772b63f5f9a561a5bfc40bc18b3f429b4e318fccd785", ["runtime-execution-liveness", "runtime-incident-state", "runtime-scheduler-state", "runtime-workflow-execution-state", "runtime-workflow-hold-state"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "recovery-candidate-index"]),
            ["queue-drain"] = new("1.0.0", "21d5dabec0cb604dae214af9bc20835b09ab5f87cfd3d697b946d8adb31d20fa", "bf82d193b202ff0c9e3be211009a3f10401cf1411aaac607db64a199657fb630", ["runtime-scheduler-poison", "runtime-scheduler-work-queue"], ["dedicated-scheduler-work-documents", "dedicated-scheduler-poison-documents", "shared-documents-with-linked-index-tables"]),
            ["outbox-drain"] = new("1.0.0", "3a6f44fea2a5905e3df316bd3585b13f6080588c75c6eab9598cced26c184eef", "0f4f678c6250ce1c951ad1e14218a1c38c61d6b15b947fa724c50859a4339934", ["runtime-post-commit-outbox"], ["dedicated-post-commit-outbox-documents", "shared-documents-with-linked-index-tables", "due-order-index"]),
            ["due-timer-selection"] = new("1.0.0", "86bef68c844d10b3cb02c8f65da33ba46ec4c27b9ba0090b9783cd5036f1ab0e", "002fe0f7e4808d7ec2b85f267f8188981c3fd8ed4beca7ad11100ddd6c8d2002", ["runtime-durable-timer"], ["dedicated-durable-timer-documents", "shared-documents-with-linked-index-tables", "due-order-index"]),
            ["recurring-schedule-selection"] = new("1.0.0", "ab6e1c276995da9e564f55cee00f243a13e16334c58c0d19cc146f2f757b3b5e", "af1d9aecbc7604ce39e33c553d9c1014be2377d81300aa4905e700beffcf7b17", ["runtime-publication-projection-state", "runtime-recurring-trigger-schedule"], ["dedicated-recurring-schedule-documents", "publication-projection-documents", "shared-documents-with-linked-index-tables"]),
            ["iam-normalized-lookup-update"] = new("1.1.0", "5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9", "32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc", ["iam-application", "iam-claim-mapping", "iam-credential", "iam-external-identity", "iam-provider-configuration-tenant", "iam-role", "iam-tenant-membership", "iam-user"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "entity-type-specific-physical-tables-current-identity-shape"]),
            ["secret-create-read-list"] = new("1.0.0", "339a6adc9ba6c34e85ce43eafd3e0b8b7b74f7ccbb7d52bd34efe1fbe394014c", "615f7bbd8e160dd34d38180d5def0e99d0b4225822e6ebee5ea31ed21bbabcdb", ["secrets-repository"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "entity-type-specific-physical-tables"]),
            ["placement-takeover"] = new("1.0.0", "9599391db271c63a41cced1409754b0bcb4e2bd8c316b70efa4d1583c310c92b", "25885819c5cb186adab6196a46d8e369e7b26992e0733e9525a3ca1eb2bf07c1", ["distributed-execution-placement"], ["dedicated-placement-lease-documents", "shared-documents-with-linked-index-tables", "placement-owner-expiry-index"]),
            ["command-send-lease-ack"] = new("1.0.0", "cb50baabaf83d0826dbb19d259be9d8fca9b4c8eaa9aea6ba7354a54c1835493", "9f8fa582159e0a796ecbe2d7bfb655cbebee428f9490fa83d4228e8e64f924eb", ["distributed-command-transport"], ["dedicated-command-transport-documents", "stream-head-documents", "shared-documents-with-linked-index-tables", "visibility-order-index"])
        };
}
