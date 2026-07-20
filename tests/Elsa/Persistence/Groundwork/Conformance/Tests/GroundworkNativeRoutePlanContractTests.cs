using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class GroundworkNativeRoutePlanContractTests
{
    [Fact]
    public void Result_refuses_fabricated_acceptance_cardinality()
    {
        var request = CreateRequest();

        var exception = Assert.Throws<InvalidOperationException>(() => GroundworkNativeRoutePlanResult.Create(
            request,
            "sqlite",
            physicalCardinality: request.AcceptanceCardinality - 1,
            "index-search",
            "ix_identity_users_scope_name",
            request.Limit,
            materializedCandidateCount: 1,
            CreateCommands()));

        Assert.Contains("does not match required acceptance cardinality", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evidence_exposes_plan_facts_without_scope_or_route_values()
    {
        var request = CreateRequest();

        var result = GroundworkNativeRoutePlanResult.Create(
            request,
            "sqlite",
            request.AcceptanceCardinality,
            "index-search",
            "ix_identity_users_scope_name",
            request.Limit,
            materializedCandidateCount: 1,
            CreateCommands());

        Assert.Equal("identity-native-route-plan", result.Evidence.Kind);
        Assert.DoesNotContain(request.StorageScope, result.Evidence.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(request.RouteValue, result.Evidence.Content, StringComparison.Ordinal);
        Assert.Contains("physical-cardinality=100000", result.Evidence.Content, StringComparison.Ordinal);
        Assert.Contains("finite-limit=1", result.Evidence.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_requires_route_field_to_be_physically_projected()
    {
        var exception = Assert.Throws<ArgumentException>(() => new GroundworkNativeRoutePlanRequest(
            "identityUser",
            "find-user-by-normalized-name",
            "identity_users",
            "normalizedUserNameKey",
            ["normalizedEmailKey"],
            "scope-secret",
            "route-secret",
            limit: 1,
            acceptanceCardinality: 100_000));

        Assert.Contains("route field must be part", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Command_evidence_persists_only_allowlisted_metadata_and_a_plan_digest()
    {
        var request = CreateRequest();
        const string rawNativePlan = "SEARCH ix_identity_users_scope_name WHERE scope-secret AND route-secret";
        var commands = CreateCommands(rawNativePlan);

        var result = GroundworkNativeRoutePlanResult.Create(
            request,
            "sqlite",
            request.AcceptanceCardinality,
            "index-search",
            "ix_identity_users_scope_name",
            request.Limit,
            materializedCandidateCount: 1,
            commands);

        Assert.DoesNotContain(rawNativePlan, result.Evidence.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(request.StorageScope, result.Evidence.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(request.RouteValue, result.Evidence.Content, StringComparison.Ordinal);
        Assert.Contains("command-0=Count|count|sqlite-query-plan|index-search|ix_identity_users_scope_name", result.Evidence.Content, StringComparison.Ordinal);
        Assert.All(commands, command => Assert.Matches("^[0-9a-f]{64}$", command.NativePlanSha256));
    }

    [Fact]
    public void Result_rejects_zero_command_native_evidence()
    {
        var request = CreateRequest();

        var exception = Assert.Throws<InvalidOperationException>(() => GroundworkNativeRoutePlanResult.Create(
            request,
            "sqlite",
            request.AcceptanceCardinality,
            "index-search",
            "ix_identity_users_scope_name",
            request.Limit,
            materializedCandidateCount: 1,
            []));

        Assert.Contains("exact ordered command contract", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_accepts_count_only_evidence_without_a_materialization_limit()
    {
        var request = new GroundworkNativeRoutePlanRequest(
            "executionCommandTransport",
            "count-pending-commands-by-execution",
            "execution_command_transport",
            "workflowExecutionIdKey",
            ["workflowExecutionIdKey"],
            "scope-secret",
            "route-secret",
            limit: null,
            acceptanceCardinality: 100_000,
            expectedCommandKinds: [PhysicalDocumentQueryCommandKind.Count],
            evidenceKind: "provider-native-route-plan");

        var result = GroundworkNativeRoutePlanResult.Create(
            request,
            "sqlite",
            request.AcceptanceCardinality,
            "index-search",
            "ix_execution_command_transport_scope_execution",
            finiteLimit: null,
            materializedCandidateCount: 0,
            [CreateCommand(
                0,
                PhysicalDocumentQueryCommandKind.Count,
                PhysicalDocumentQueryCommandIdentities.Count,
                "SEARCH ix_execution_command_transport_scope_execution",
                "workflowExecutionIdKey",
                "ix_execution_command_transport_scope_execution")]);

        Assert.Null(result.FiniteLimit);
        Assert.Equal("provider-native-route-plan", result.Evidence.Kind);
        Assert.Contains("finite-limit=none", result.Evidence.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Dataset_preserves_typed_explicit_projection_values()
    {
        var request = new GroundworkNativeRoutePlanRequest(
            "secret",
            "list-filtered",
            "secrets",
            "normalizedName",
            ["normalizedName", "status", "hasNonExpiringActiveVersion"],
            "scope-secret",
            "route-secret",
            limit: 10,
            acceptanceCardinality: 100_000,
            projectedValues:
            [
                GroundworkNativeRouteProjectedValue.String("normalizedName", "route-secret"),
                GroundworkNativeRouteProjectedValue.String("status", "active"),
                GroundworkNativeRouteProjectedValue.Boolean("hasNonExpiringActiveVersion", true)
            ],
            requiredPredicateFields: ["status"]);

        var dataset = GroundworkNativeRouteDataset.Create([request]);

        Assert.Equal(
            GroundworkNativeRouteProjectedValueKind.Boolean,
            dataset.ProjectedValues["hasNonExpiringActiveVersion"].Kind);
        Assert.Equal("true", dataset.ProjectedValues["hasNonExpiringActiveVersion"].Value);
    }

    [Fact]
    public void Compiled_index_selection_rejects_an_unrelated_admitted_index()
    {
        var request = new GroundworkNativeRoutePlanRequest(
            "secret",
            "list-filtered",
            "secrets",
            "status",
            ["status"],
            "scope-secret",
            "active",
            limit: 10,
            acceptanceCardinality: 100_000,
            expectedIndexName: "ix_secret_filtered");
        var commands = new[]
        {
            CreateCommand(
                0,
                PhysicalDocumentQueryCommandKind.Count,
                PhysicalDocumentQueryCommandIdentities.Count,
                "SEARCH ix_secret_name",
                "status",
                "ix_secret_name")
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GroundworkNativeRoutePlanResult.SelectCompiledIndex(
                request,
                ["ix_secret_filtered", "ix_secret_name"],
                commands));

        Assert.Contains("did not use compiled index 'ix_secret_filtered'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("count\nscope-secret", "sqlite-query-plan", "index-search", "ix_identity_users_scope_name", "storage_scope")]
    [InlineData("count", "sqlite-query-plan\nroute-secret", "index-search", "ix_identity_users_scope_name", "storage_scope")]
    [InlineData("count", "sqlite-query-plan", "index-search\nscope-secret", "ix_identity_users_scope_name", "storage_scope")]
    [InlineData("count", "sqlite-query-plan", "index-search", "ix_identity_users_scope_name\nroute-secret", "storage_scope")]
    [InlineData("count", "sqlite-query-plan", "index-search", "ix_identity_users_scope_name", "storage_scope\nscope-secret")]
    public void Command_evidence_rejects_non_allowlisted_metadata(
        string identity,
        string format,
        string classification,
        string indexName,
        string predicateField)
    {
        var explanation = new PhysicalDocumentQueryCommandExplanation(
            PhysicalDocumentQueryCommandKind.Count,
            identity,
            format,
            "sensitive raw plan",
            [predicateField, "normalizedUserNameKey"]);

        Assert.ThrowsAny<Exception>(() => GroundworkNativeRouteCommandEvidence.Create(
            0,
            explanation,
            classification,
            [indexName]));
    }

    private static GroundworkNativeRoutePlanRequest CreateRequest() => new(
        "identityUser",
        "find-user-by-normalized-name",
        "identity_users",
        "normalizedUserNameKey",
        ["normalizedUserNameKey", "normalizedEmailKey"],
        "scope-secret",
        "route-secret",
        limit: 1,
        acceptanceCardinality: 100_000);

    private static IReadOnlyList<GroundworkNativeRouteCommandEvidence> CreateCommands(
        string rawNativePlan = "SEARCH ix_identity_users_scope_name") =>
    [
        CreateCommand(
            0,
            PhysicalDocumentQueryCommandKind.Count,
            PhysicalDocumentQueryCommandIdentities.Count,
            rawNativePlan),
        CreateCommand(
            1,
            PhysicalDocumentQueryCommandKind.Page,
            PhysicalDocumentQueryCommandIdentities.Page,
            rawNativePlan)
    ];

    private static GroundworkNativeRouteCommandEvidence CreateCommand(
        int ordinal,
        PhysicalDocumentQueryCommandKind kind,
        string identity,
        string rawNativePlan,
        string predicateField = "normalizedUserNameKey",
        string indexName = "ix_identity_users_scope_name") =>
        GroundworkNativeRouteCommandEvidence.Create(
            ordinal,
            new PhysicalDocumentQueryCommandExplanation(
                kind,
                identity,
                "sqlite-query-plan",
                rawNativePlan,
                [predicateField, "storage_scope"],
                providerAppliedMaximumRows: 1,
                providerAppliedOrder: kind is
                    PhysicalDocumentQueryCommandKind.Page or
                    PhysicalDocumentQueryCommandKind.First
                    ?
                    [
                        new PhysicalDocumentQueryCommandOrder(
                            predicateField,
                            PhysicalSortDirection.Ascending,
                            isIdentityTieBreak: false),
                        new PhysicalDocumentQueryCommandOrder(
                            "id",
                            PhysicalSortDirection.Ascending,
                            isIdentityTieBreak: true)
                    ]
                    : null),
            "index-search",
            [indexName]);
}
