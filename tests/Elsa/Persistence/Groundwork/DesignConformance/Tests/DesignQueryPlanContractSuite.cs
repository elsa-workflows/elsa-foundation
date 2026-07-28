using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// One captured native-plan verdict for a declared design query route. The provider leaf renders
/// its native plan (for example SQLite <c>EXPLAIN QUERY PLAN</c>) and reports whether the probe's
/// selective access used the intended index rather than a full table/collection scan.
/// </summary>
public sealed record DesignQueryPlanEvidence(
    string DocumentKind,
    string QueryIdentity,
    string ResultOperation,
    bool UsesIndexedAccess,
    string Detail);

/// <summary>
/// T040: provider-plan assertions for every selected physical design entity table. The shared suite
/// pins the required probe matrix — every scale-bearing bounded route with a selective predicate —
/// and the assertion that each probe resolves through indexed access with no full scan. Provider
/// leaves supply the concrete plan capture; this suite is subclassed only where a native plan
/// surface exists (the temporary EF oracle has no Groundwork routes and does not subscribe).
/// </summary>
public abstract class DesignQueryPlanContractSuite
{
    /// <summary>Captures one native-plan verdict per required probe row.</summary>
    protected abstract Task<IReadOnlyList<DesignQueryPlanEvidence>> CaptureCatalogPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The complete required probe matrix: (document kind, declared query identity, result
    /// operation) for every scale-bearing design route in the bounded query catalog.
    /// </summary>
    public static readonly IReadOnlyList<(string DocumentKind, string QueryIdentity, string ResultOperation)> RequiredProbes =
    [
        ("workflowDefinition", "list-definitions-by-id", "Documents"),
        ("workflowDefinition", "list-definitions-by-name", "Documents"),
        ("workflowDefinition", "list-definitions-by-name", "Count"),
        ("workflowDefinition", "list-definitions-by-description", "Documents"),
        ("workflowDefinition", "search-definitions", "Documents"),
        ("workflowDefinitionVersion", "list-versions-by-definition", "Documents"),
        ("workflowDefinitionVersion", "find-version-by-definition-and-sort-key", "Any"),
        ("workflowDefinitionVersion", "find-latest-version", "First"),
        ("workflowDefinitionDraft", "list-drafts-by-definition", "Documents"),
        ("workflowDefinitionDraft", "find-current-draft-by-definition", "First"),
        ("workflowDefinitionVersionLayout", "find-layout-by-version", "First"),
        ("activityDefinition", "list-activity-definitions-by-id", "Documents"),
        ("activityDefinition", "list-activity-definitions-by-type-key", "Documents"),
        ("activityDefinition", "list-activity-definitions-by-category", "Documents"),
        ("activityDefinition", "list-activity-definitions-by-display-name", "Documents"),
        ("activityDefinition", "list-activity-definitions-by-description", "Documents"),
        ("activityDefinition", "search-activity-definitions", "Documents"),
        ("activityDefinitionVersion", "list-activity-definition-versions-by-definition", "Documents"),
        ("activityDefinitionVersion", "find-activity-definition-version-by-definition-and-sort-key", "First")
    ];

    [Fact]
    public async Task Every_scale_bearing_design_route_uses_indexed_access_with_no_full_scan()
    {
        var evidence = await CaptureCatalogPlansAsync();

        foreach (var (documentKind, queryIdentity, resultOperation) in RequiredProbes)
        {
            var row = evidence.SingleOrDefault(x =>
                x.DocumentKind == documentKind &&
                x.QueryIdentity == queryIdentity &&
                x.ResultOperation == resultOperation);

            Assert.True(row is not null, $"Missing plan evidence for {documentKind}/{queryIdentity}/{resultOperation}.");
            Assert.True(
                row!.UsesIndexedAccess,
                $"{documentKind}/{queryIdentity}/{resultOperation} did not use indexed access: {row.Detail}");
        }
    }
}
