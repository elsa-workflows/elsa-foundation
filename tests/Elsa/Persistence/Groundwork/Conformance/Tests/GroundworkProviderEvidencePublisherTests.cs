using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class GroundworkProviderEvidencePublisherTests
{
    [Fact]
    public async Task Publishes_a_complete_equivalent_matrix_with_hashes_from_written_artifacts_and_native_route_evidence()
    {
        var directory = TemporaryDirectory();
        try
        {
            var plans = MandatoryProviders.ToDictionary(
                provider => provider,
                provider => NativeRoutePlan(provider),
                StringComparer.Ordinal);
            var results = MandatoryProviders.Select(provider => Result(provider, plans[provider])).ToArray();
            var obligation = GroundworkLedgerObligation.QueryShape(
                "runtime-bookmark-state",
                "find-by-workflow-and-bookmark",
                "find-by-workflow-and-bookmark",
                "bounded-query");

            var publication = await GroundworkProviderEvidencePublisher.PublishAsync(directory, obligation, results, plans.Values);

            Assert.Equal(MandatoryProviders, publication.LedgerRecords.Select(record => record!["provider"]!.GetValue<string>()));
            Assert.Equal(
                results.Select(result => result.ResultDigest).Distinct(StringComparer.Ordinal).Single(),
                publication.LedgerRecords[0]!["resultHash"]!.GetValue<string>());
            Assert.Equal(8, publication.Artifacts.Count);
            Assert.Contains("\"nativeEvidence\"", Encoding.UTF8.GetString(publication.LedgerAttachmentJson), StringComparison.Ordinal);

            foreach (var record in publication.LedgerRecords.Select(node => node!.AsObject()))
            {
                var provider = record["provider"]!.GetValue<string>();
                var evidencePath = record["evidence"]!.GetValue<string>();
                var nativePath = record["nativeEvidence"]!.GetValue<string>();
                Assert.Equal("queryShape:find-by-workflow-and-bookmark", record["scenarioId"]!.GetValue<string>());
                Assert.Equal("bounded-query", record["sourceScenarioId"]!.GetValue<string>());
                Assert.Equal("find-by-workflow-and-bookmark", record["queryShape"]!.GetValue<string>());
                Assert.Equal("find-by-workflow-and-bookmark", record["nativeQueryIdentity"]!.GetValue<string>());
                Assert.Equal("fixture-document", record["documentKind"]!.GetValue<string>());
                Assert.Equal($"provider-driver/{provider}/runtime-bookmark-state/{ScenarioKey(obligation.ScenarioId)}", record["executionPath"]!.GetValue<string>());
                Assert.Equal($"evidence/{provider}/runtime-bookmark-state/{ScenarioKey(obligation.ScenarioId)}.json", evidencePath);
                Assert.Equal($"evidence/{provider}/runtime-bookmark-state/{ScenarioKey(obligation.ScenarioId)}.plan.txt", nativePath);
                Assert.Equal(HashFile(directory, evidencePath), record["evidenceSha256"]!.GetValue<string>());
                Assert.Equal(HashFile(directory, nativePath), record["nativeEvidenceSha256"]!.GetValue<string>());
                Assert.Equal(plans[provider].Evidence.Content, File.ReadAllText(Path.Combine(directory, nativePath)));

                var payload = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, evidencePath)))!.AsObject();
                Assert.Equal(1, payload["schemaVersion"]!.GetValue<int>());
                Assert.Equal(record["resultHash"]!.GetValue<string>(), payload["resultHash"]!.GetValue<string>());
                var observation = Assert.Single(payload["observations"]!.AsArray());
                Assert.Equal("winner-count", observation!["name"]!.GetValue<string>());
                Assert.Equal("same", observation["value"]!.GetValue<string>());
                Assert.DoesNotContain("evidenceSha256", payload.Select(pair => pair.Key));
                Assert.DoesNotContain("nativeEvidenceSha256", payload.Select(pair => pair.Key));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_a_matrix_without_one_equivalent_passing_result()
    {
        var directory = TemporaryDirectory();
        try
        {
            var results = MandatoryProviders
                .Select(provider => Result(provider, null, provider == "mongodb" ? "different" : "same"))
                .ToArray();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                GroundworkProviderEvidencePublisher.PublishAsync(
                    directory,
                    GroundworkLedgerObligation.OrdinaryRoundTrip("runtime-bookmark-state", "bounded-query"),
                    results));

            Assert.Contains("equivalent public-result digest", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_an_obligation_whose_declared_source_scenario_did_not_produce_the_results()
    {
        var directory = TemporaryDirectory();
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                GroundworkProviderEvidencePublisher.PublishAsync(
                    directory,
                    GroundworkLedgerObligation.OrdinaryRoundTrip("runtime-bookmark-state", "different-source"),
                    MandatoryProviders.Select(provider => Result(provider, null))));

            Assert.Contains("source scenario", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_query_results_without_their_real_four_provider_native_route_evidence()
    {
        var directory = TemporaryDirectory();
        try
        {
            var plans = MandatoryProviders.ToDictionary(
                provider => provider,
                provider => NativeRoutePlan(provider),
                StringComparer.Ordinal);
            var results = MandatoryProviders.Select(provider => Result(provider, plans[provider])).ToArray();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                GroundworkProviderEvidencePublisher.PublishAsync(
                    directory,
                    GroundworkLedgerObligation.QueryShape("runtime-bookmark-state", "find-by-workflow-and-bookmark", "find-by-workflow-and-bookmark", "bounded-query"),
                    results));

            Assert.Contains("exactly one real native route plan", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Maps_a_failure_window_to_its_validator_catalog_scenario_and_dimension()
    {
        var directory = TemporaryDirectory();
        try
        {
            var obligation = GroundworkLedgerObligation.FailureWindow(
                "runtime-bookmark-state",
                "before-provider-decision",
                "bounded-query");
            var publication = await GroundworkProviderEvidencePublisher.PublishAsync(
                directory,
                obligation,
                MandatoryProviders.Select(provider => Result(provider, null, failureWindow: "before-provider-decision")));

            foreach (var record in publication.LedgerRecords.Select(node => node!.AsObject()))
            {
                Assert.Equal("failureWindow:before-provider-decision", record["scenarioId"]!.GetValue<string>());
                Assert.Equal("before-provider-decision", record["failureWindow"]!.GetValue<string>());
                Assert.Equal(
                    $"provider-driver/{record["provider"]!.GetValue<string>()}/runtime-bookmark-state/{ScenarioKey(obligation.ScenarioId)}",
                    record["executionPath"]!.GetValue<string>());
                Assert.False(record.ContainsKey("queryShape"));
                Assert.False(record.ContainsKey("nativeEvidence"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_a_query_obligation_that_does_not_match_the_real_native_route_identity()
    {
        var directory = TemporaryDirectory();
        try
        {
            var plans = MandatoryProviders.ToDictionary(
                provider => provider,
                provider => NativeRoutePlan(provider),
                StringComparer.Ordinal);
            var results = MandatoryProviders.Select(provider => Result(provider, plans[provider])).ToArray();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                GroundworkProviderEvidencePublisher.PublishAsync(
                    directory,
                    GroundworkLedgerObligation.QueryShape("runtime-bookmark-state", "different-query", "different-query", "bounded-query"),
                    results,
                    plans.Values));

            Assert.Contains("does not match query obligation", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_query_evidence_with_mixed_native_document_kinds()
    {
        var directory = TemporaryDirectory();
        try
        {
            var plans = MandatoryProviders.ToDictionary(
                provider => provider,
                provider => NativeRoutePlan(provider, provider == "mongodb" ? "different-document" : "fixture-document"),
                StringComparer.Ordinal);
            var results = MandatoryProviders.Select(provider => Result(provider, plans[provider])).ToArray();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                GroundworkProviderEvidencePublisher.PublishAsync(
                    directory,
                    GroundworkLedgerObligation.QueryShape(
                        "runtime-bookmark-state",
                        "find-by-workflow-and-bookmark",
                        "find-by-workflow-and-bookmark",
                        "bounded-query"),
                    results,
                    plans.Values));

            Assert.Contains("matching document kind", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Maps_a_provider_neutral_query_shape_to_its_real_native_route_identity()
    {
        var directory = TemporaryDirectory();
        try
        {
            var plans = MandatoryProviders.ToDictionary(
                provider => provider,
                provider => NativeRoutePlan(provider),
                StringComparer.Ordinal);
            var results = MandatoryProviders.Select(provider => Result(provider, plans[provider])).ToArray();
            var obligation = GroundworkLedgerObligation.QueryShape(
                "runtime-bookmark-state",
                "list-by-workflow-bounded",
                "find-by-workflow-and-bookmark",
                "bounded-query");

            var publication = await GroundworkProviderEvidencePublisher.PublishAsync(
                directory,
                obligation,
                results,
                plans.Values);

            Assert.All(
                publication.LedgerRecords,
                record =>
                {
                    Assert.Equal(
                        "queryShape:list-by-workflow-bounded",
                        record!["scenarioId"]!.GetValue<string>());
                    Assert.Equal(
                        "list-by-workflow-bounded",
                        record["queryShape"]!.GetValue<string>());
                });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_a_failure_window_obligation_that_does_not_match_the_real_scenario_result()
    {
        var directory = TemporaryDirectory();
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                GroundworkProviderEvidencePublisher.PublishAsync(
                    directory,
                    GroundworkLedgerObligation.FailureWindow(
                        "runtime-bookmark-state",
                        "after-provider-decision",
                        "bounded-query"),
                    MandatoryProviders.Select(provider => Result(provider, null, failureWindow: "before-provider-decision"))));

            Assert.Contains("must match the real scenario failure window", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_concurrency_failure_and_restart_evidence_without_two_independent_clients()
    {
        var directory = TemporaryDirectory();
        try
        {
            foreach (var obligation in new[]
                     {
                         GroundworkLedgerObligation.ConcurrencySemantic(
                             "runtime-bookmark-state",
                             "expected-version-save",
                             "bounded-query"),
                         GroundworkLedgerObligation.FailureWindow(
                             "runtime-bookmark-state",
                             "before-provider-decision",
                             "bounded-query"),
                         GroundworkLedgerObligation.RestartScenario(
                             "runtime-bookmark-state",
                             "dispose-and-reopen-same-database",
                             "bounded-query")
                     })
            {
                var failureWindow = obligation.Kind is GroundworkLedgerObligationKind.FailureWindow
                    ? obligation.Value
                    : null;
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    GroundworkProviderEvidencePublisher.PublishAsync(
                        directory,
                        obligation,
                        MandatoryProviders.Select(provider => Result(
                            provider,
                            null,
                            failureWindow: failureWindow,
                            clients: 1))));

                Assert.Contains("at least two independent clients", exception.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Reuses_the_validator_current_paths_by_overwriting_a_new_verified_publication()
    {
        var directory = TemporaryDirectory();
        try
        {
            var first = await GroundworkProviderEvidencePublisher.PublishAsync(
                directory,
                GroundworkLedgerObligation.OrdinaryRoundTrip("runtime-bookmark-state", "bounded-query"),
                MandatoryProviders.Select(provider => Result(provider, null, providerVersion: "1.0.0")));
            var second = await GroundworkProviderEvidencePublisher.PublishAsync(
                directory,
                GroundworkLedgerObligation.OrdinaryRoundTrip("runtime-bookmark-state", "bounded-query"),
                MandatoryProviders.Select(provider => Result(provider, null, providerVersion: "1.0.1")));
            var repeated = await GroundworkProviderEvidencePublisher.PublishAsync(
                directory,
                GroundworkLedgerObligation.OrdinaryRoundTrip("runtime-bookmark-state", "bounded-query"),
                MandatoryProviders.Select(provider => Result(provider, null, providerVersion: "1.0.1")));

            var firstRecord = first.LedgerRecords[0]!.AsObject();
            var secondRecord = second.LedgerRecords[0]!.AsObject();
            Assert.Equal("ordinary-round-trip", secondRecord["scenarioId"]!.GetValue<string>());
            Assert.Equal(
                $"provider-driver/sqlite/runtime-bookmark-state/{ScenarioKey("ordinary-round-trip")}",
                secondRecord["executionPath"]!.GetValue<string>());
            Assert.False(secondRecord.ContainsKey("queryShape"));
            Assert.False(secondRecord.ContainsKey("concurrencySemantic"));
            Assert.False(secondRecord.ContainsKey("failureWindow"));
            Assert.False(secondRecord.ContainsKey("restartScenario"));
            Assert.Equal(firstRecord["evidence"]!.GetValue<string>(), secondRecord["evidence"]!.GetValue<string>());
            Assert.NotEqual(firstRecord["evidenceSha256"]!.GetValue<string>(), secondRecord["evidenceSha256"]!.GetValue<string>());
            Assert.Equal(
                HashFile(directory, secondRecord["evidence"]!.GetValue<string>()),
                secondRecord["evidenceSha256"]!.GetValue<string>());
            Assert.Equal(second.LedgerAttachmentJson, repeated.LedgerAttachmentJson);
            Assert.False(File.Exists(Path.Combine(directory, "current.json")));
            Assert.False(Directory.Exists(Path.Combine(directory, "generations")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Versioned_publication_preserves_current_paths_and_retains_exact_source_provenance()
    {
        var directory = TemporaryDirectory();
        try
        {
            const string version = "1.0.1-preview.2";
            var currentPath = Path.Combine(
                directory,
                "evidence/sqlite/runtime-bookmark-state",
                $"{ScenarioKey("ordinary-round-trip")}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
            await File.WriteAllTextAsync(currentPath, "immutable-current-evidence");
            var provenance = GroundworkProviderEvidenceProvenance.Create(
                new string('a', 40),
                new string('b', 40),
                "runtime-checkpoint-fence-preview81");
            var publication = await GroundworkProviderEvidencePublisher.PublishVersionedAsync(
                directory,
                version,
                provenance,
                GroundworkLedgerObligation.OrdinaryRoundTrip("runtime-bookmark-state", "bounded-query"),
                MandatoryProviders.Select(provider => Result(provider, null, providerVersion: version)));

            Assert.Equal("immutable-current-evidence", await File.ReadAllTextAsync(currentPath));
            foreach (var record in publication.LedgerRecords.Select(node => node!.AsObject()))
            {
                var provider = record["provider"]!.GetValue<string>();
                var evidencePath = record["evidence"]!.GetValue<string>();
                Assert.Equal(
                    $"versions/{version}/evidence/{provider}/runtime-bookmark-state/{ScenarioKey("ordinary-round-trip")}.json",
                    evidencePath);
                var payload = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, evidencePath)))!.AsObject();
                Assert.Equal(new string('a', 40), payload["provenance"]!["elsaCommit"]!.GetValue<string>());
                Assert.Equal(new string('b', 40), payload["provenance"]!["elsaTree"]!.GetValue<string>());
                Assert.Equal(
                    "runtime-checkpoint-fence-preview81",
                    payload["provenance"]!["runIdentity"]!.GetValue<string>());
                Assert.Single(payload["observations"]!.AsArray());
                Assert.True(JsonNode.DeepEquals(record["provenance"], payload["provenance"]));
                Assert.True(JsonNode.DeepEquals(record["observations"], payload["observations"]));
            }

            var attachment = await GroundworkProviderEvidencePublisher.WriteVersionedLedgerAttachmentAsync(
                directory,
                version,
                "runtime-evidence",
                [publication]);
            Assert.Equal(
                $"versions/{version}/ledger-attachments/runtime-evidence.json",
                attachment.RelativePath);
            Assert.Equal(HashFile(directory, attachment.RelativePath), attachment.Sha256);

            var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                GroundworkProviderEvidencePublisher.PublishVersionedAsync(
                    directory,
                    "1.0.2",
                    provenance,
                    GroundworkLedgerObligation.OrdinaryRoundTrip("runtime-bookmark-state", "bounded-query"),
                    MandatoryProviders.Select(provider => Result(provider, null, providerVersion: version))));
            Assert.Contains("namespace version", mismatch.Message, StringComparison.Ordinal);

            async Task AssertAttachmentRejectedAsync(Action<JsonObject> tamper, string messageFragment)
            {
                var records = publication.LedgerRecords.DeepClone().AsArray();
                tamper(records[0]!.AsObject());
                var tampered = publication with { LedgerRecords = records };
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    GroundworkProviderEvidencePublisher.WriteVersionedLedgerAttachmentAsync(
                        directory,
                        version,
                        "tampered-runtime-evidence",
                        [tampered]));
                Assert.Contains(messageFragment, exception.Message, StringComparison.Ordinal);
            }

            await AssertAttachmentRejectedAsync(
                record => record["providerVersion"] = "1.0.1-preview.3",
                "provider version");
            await AssertAttachmentRejectedAsync(
                record => record["evidence"] = "evidence/sqlite/runtime-bookmark-state/fixture.json",
                "evidence path");
            await AssertAttachmentRejectedAsync(
                record => record["nativeEvidence"] = "evidence/sqlite/runtime-bookmark-state/fixture.plan.txt",
                "native-evidence path");
            await AssertAttachmentRejectedAsync(
                record => record.Remove("provenance"),
                "source provenance");
            await AssertAttachmentRejectedAsync(
                record => record["provenance"]!["elsaTree"] = new string('c', 40),
                "one exact source provenance tuple");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Writes_a_deterministic_merge_ready_family_attachment_and_rejects_duplicate_keys()
    {
        var directory = TemporaryDirectory();
        try
        {
            var first = await GroundworkProviderEvidencePublisher.PublishAsync(
                directory,
                GroundworkLedgerObligation.OrdinaryRoundTrip("runtime-bookmark-state", "bounded-query"),
                MandatoryProviders.Select(provider => Result(provider, null)));
            var second = await GroundworkProviderEvidencePublisher.PublishAsync(
                directory,
                GroundworkLedgerObligation.ConcurrencySemantic(
                    "runtime-bookmark-state",
                    "expected-version-save",
                    "bounded-query"),
                MandatoryProviders.Select(provider => Result(provider, null)));

            var artifact = await GroundworkProviderEvidencePublisher.WriteLedgerAttachmentAsync(
                directory,
                "runtime-evidence",
                [second, first]);
            var path = Path.Combine(directory, artifact.RelativePath);
            var records = JsonNode.Parse(File.ReadAllText(path))!.AsArray();

            Assert.Equal(8, records.Count);
            Assert.Equal(
                records
                    .Select(record => record!["coverageEntryId"]!.GetValue<string>())
                    .Order(StringComparer.Ordinal),
                records.Select(record => record!["coverageEntryId"]!.GetValue<string>()));
            Assert.Equal(HashFile(directory, artifact.RelativePath), artifact.Sha256);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                GroundworkProviderEvidencePublisher.WriteLedgerAttachmentAsync(
                    directory,
                    "duplicate-evidence",
                    [first, first]));
            Assert.Contains("duplicate evidence key", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static readonly string[] MandatoryProviders = ["sqlite", "sqlserver", "postgresql", "mongodb"];
    private static readonly GroundworkCompositionFingerprint Composition =
        GroundworkCompositionFingerprint.Create("provider-evidence-publisher-contract:v1");
    private static string ScenarioKey(string scenarioId) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(scenarioId)))[..16];

    private static GroundworkScenarioResult Result(
        string provider,
        GroundworkNativeRoutePlanResult? route,
        string observationValue = "same",
        string providerVersion = "1.0.0",
        string? failureWindow = null,
        int clients = 2)
    {
        var descriptor = Descriptor(provider, providerVersion);
        var path = GroundworkExecutionPath.Create(provider, "bounded-query", Composition);
        return GroundworkScenarioResult.Create(
            "bounded-query",
            "runtime-bookmark-state",
            descriptor,
            Composition,
            path.Value,
            clients,
            [new GroundworkScenarioObservation("winner-count", observationValue)],
            GroundworkScenarioOutcome.Pass,
            failureWindow,
            nativeEvidence: route is null ? null : GroundworkNativePlanEvidence.Create(path, "bounded-query", route.Evidence));
    }

    private static GroundworkProviderDescriptor Descriptor(string provider, string version) => new(
        provider,
        $"groundwork-{provider}",
        version,
        new GroundworkProviderTopology(
            provider,
            provider switch
            {
                "sqlite" => "file-backed-distinct-connections",
                "sqlserver" => "real-sqlserver-container",
                "postgresql" => "real-postgresql-container",
                "mongodb" => "transaction-capable-replica-set",
                _ => throw new ArgumentOutOfRangeException(nameof(provider))
            },
            GroundworkTopologyCapabilities.PersistentStorage | GroundworkTopologyCapabilities.IndependentClients));

    private static GroundworkNativeRoutePlanResult NativeRoutePlan(string provider, string documentKind = "fixture-document")
    {
        var request = new GroundworkNativeRoutePlanRequest(
            documentKind,
            "find-by-workflow-and-bookmark",
            "fixture_documents",
            "normalized_key",
            ["normalized_key"],
            "scope-fixture",
            "target",
            1,
            100);
        return GroundworkNativeRoutePlanResult.Create(
            request,
            provider,
            100,
            "index-seek",
            "ix_fixture_documents_normalized_key",
            1,
            1,
            NativeCommands(provider));
    }

    private static IReadOnlyList<GroundworkNativeRouteCommandEvidence> NativeCommands(string provider) =>
    [
        NativeCommand(provider, 0, PhysicalDocumentQueryCommandKind.Count, PhysicalDocumentQueryCommandIdentities.Count),
        NativeCommand(provider, 1, PhysicalDocumentQueryCommandKind.Page, PhysicalDocumentQueryCommandIdentities.Page)
    ];

    private static GroundworkNativeRouteCommandEvidence NativeCommand(
        string provider,
        int ordinal,
        PhysicalDocumentQueryCommandKind kind,
        string identity) =>
        GroundworkNativeRouteCommandEvidence.Create(
            ordinal,
            new PhysicalDocumentQueryCommandExplanation(
                kind,
                identity,
                NativePlanFormat(provider),
                "SEARCH ix_fixture_documents_normalized_key",
                ["storage_scope", "normalized_key"],
                providerAppliedMaximumRows: 1,
                providerAppliedOrder: kind == PhysicalDocumentQueryCommandKind.Page
                    ?
                    [
                        new PhysicalDocumentQueryCommandOrder(
                            "normalized_key",
                            PhysicalSortDirection.Ascending,
                            isIdentityTieBreak: false),
                        new PhysicalDocumentQueryCommandOrder(
                            "id",
                            PhysicalSortDirection.Ascending,
                            isIdentityTieBreak: true)
                    ]
                    : null),
            "index-seek",
            ["ix_fixture_documents_normalized_key"]);

    private static string NativePlanFormat(string provider) => provider switch
    {
        "sqlite" => "sqlite-query-plan",
        "sqlserver" => "sqlserver-statistics-xml",
        "postgresql" => "postgresql-json",
        "mongodb" => "mongodb-json",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa-groundwork-provider-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string HashFile(string root, string relativePath) => Convert.ToHexStringLower(
        SHA256.HashData(File.ReadAllBytes(Path.Combine(root, relativePath))));
}
