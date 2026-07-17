using Elsa.Persistence.Groundwork.Testing;
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

        Assert.Contains("Count then Page", exception.Message, StringComparison.Ordinal);
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
        string rawNativePlan) =>
        GroundworkNativeRouteCommandEvidence.Create(
            ordinal,
            new PhysicalDocumentQueryCommandExplanation(
                kind,
                identity,
                "sqlite-query-plan",
                rawNativePlan,
                ["normalizedUserNameKey", "storage_scope"]),
            "index-search",
            ["ix_identity_users_scope_name"]);
}
