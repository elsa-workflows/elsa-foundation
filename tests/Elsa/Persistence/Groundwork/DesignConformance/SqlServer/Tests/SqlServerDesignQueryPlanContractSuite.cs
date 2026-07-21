using System.Xml.Linq;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.SqlServer;
using Groundwork.SqlServer.Documents;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests;

/// <summary>
/// T040 on SQL Server: renders every required design route probe through the same composed manifest,
/// routes, and provider runtime the admitted session store uses, then captures the native
/// <c>SET STATISTICS XML</c> showplan verdict for each rendered command. A probe passes only when SQL
/// Server resolves the selective predicate through the route's certified index (an Index Seek or
/// Index Scan naming that exact index) with no full scan of the storage table.
/// </summary>
/// <remarks>
/// SQL Server has no per-connection planner knob equivalent to PostgreSQL's <c>enable_seqscan=off</c>,
/// and faking a table's cardinality with <c>UPDATE STATISTICS ... WITH ROWCOUNT/PAGECOUNT</c> does not
/// repair histogram density, so on a two-row table the cost model legitimately prefers a clustered-index
/// scan and the plan shape would be non-deterministic. To capture stable plan-shape evidence this leaf
/// seeds a batch of noise aggregates carrying distinct indexed values before probing (the same
/// seed-rows approach the acceptance native-plan matrix uses at scale) and then refreshes statistics
/// with <c>sp_updatestats</c>. That makes each probe's selective predicate genuinely selective, so the
/// optimizer reveals the certified indexed access path rather than a trivially cheap small-table scan.
/// This is documented here as the deterministic forced-plan choice for this leaf.
/// </remarks>
[Collection(SqlServerDesignProviderCollection.Name)]
public sealed class SqlServerDesignQueryPlanContractSuite(SqlServerDesignProviderFixture container)
    : DesignQueryPlanContractSuite
{
    private const int NoiseAggregatesPerFamily = 200;

    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IReadOnlyList<DesignQueryPlanEvidence>> CaptureCatalogPlansAsync(
        CancellationToken cancellationToken = default)
    {
        await using var fixture = await SqlServerDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
        await fixture.ValidateReadinessAsync(cancellationToken);
        await SeedRepresentativeRowsAsync(fixture, cancellationToken);
        await SeedNoiseRowsAsync(fixture, cancellationToken);
        await RefreshPlannerStatisticsAsync(fixture.SqlServerConnectionString, cancellationToken);

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var capabilities = await GroundworkProviderCapabilitySnapshotBuilder.ForSelectedSourcesAsync(
            SqlServerGroundworkCapabilities.Runtime(),
            new GroundworkProviderTopologySnapshot(
                SqlServerGroundworkCapabilities.Provider.Name,
                "sqlserver",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            services.GetServices<IGroundworkStorageManifestSource>(),
            cancellationToken);
        var source = await services
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(capabilities, SqlServerGroundworkCapabilities.PhysicalNames, cancellationToken);
        var manifest = source.CreateManifest();
        var access = DocumentStoreAccess.Scoped(new StorageScope(DesignPersistenceFixtureData.ScopeA));

        var store = new SqlServerPhysicalDocumentStore(
            fixture.SqlServerConnectionString,
            manifest,
            source.PhysicalTarget.Routes,
            access);

        var evidence = new List<DesignQueryPlanEvidence>();
        foreach (var probe in BuildProbes())
        {
            var route = source.PhysicalTarget.Routes.Single(candidate =>
                string.Equals(candidate.StorageUnit.Value, probe.Query.DocumentKind, StringComparison.Ordinal));
            var boundedStore = SqlServerPhysicalQueryRuntime.Create(store, manifest, route, source.PhysicalTarget.Provider);
            var explainer = (IPhysicalDocumentQueryExplainer)boundedStore;
            var explanation = await explainer.ExplainAsync(probe.Query, cancellationToken);

            // A selective probe is indexed only when (a) the compiled plan certified an index for the
            // route, (b) some explained command positively seeks or scans (Index Seek or a nonclustered
            // Index Scan) through that certified index OR another declared physical index of the same
            // route that shares the certified index's leading column, and (c) no command performs a full
            // scan of the storage table (a Table Scan of a heap or a Clustered Index Scan). SQL Server
            // reports these node kinds in the SET STATISTICS XML showplan as RelOp/@PhysicalOp values,
            // with the accessed index carried on the nested Object/@Index attribute.
            //
            // Provider-semantic note: the design manifest declares overlapping physical indexes that
            // co-lead with the same field (for example definition-by-name over (name, id) and
            // definition-by-search over (name, id, description)). SQL Server's cost-based optimizer may
            // resolve a name lookup through either — both are genuine selective index accesses on the
            // same leading column — so this leaf accepts any route index sharing the certified leading
            // column rather than pinning the exact certified index, while still rejecting a full storage
            // scan or a wrong-column/point-index scan. Non-sargable Contains probes surface as a
            // nonclustered Index Scan over the covering index, which this same allowance accepts.
            var plannedIndex = explanation.Plan.IndexName?.Identifier;
            var indexLeadingColumns = route.Indexes.ToDictionary(
                index => index.Name.Identifier,
                index => index.Columns.OrderBy(column => column.Order).First().Column.LogicalName,
                StringComparer.Ordinal);
            var certifiedLeadingColumn = plannedIndex is not null && indexLeadingColumns.TryGetValue(plannedIndex, out var leading)
                ? leading
                : null;
            var acceptableIndexNames = certifiedLeadingColumn is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : indexLeadingColumns
                    .Where(entry => string.Equals(entry.Value, certifiedLeadingColumn, StringComparison.Ordinal))
                    .Select(entry => entry.Key)
                    .ToHashSet(StringComparer.Ordinal);

            var commandNodes = explanation.Commands
                .Select(command => (command.Identity, Nodes: ParsePlanNodes(command.NativePlan)))
                .ToList();
            var details = commandNodes
                .Select(command => $"{command.Identity} (plan index: {plannedIndex ?? "<none>"}; accepts: {string.Join(",", acceptableIndexNames)}): " +
                    string.Join(" / ", command.Nodes.Select(node =>
                        node.IndexName is null ? node.PhysicalOp : $"{node.PhysicalOp}[{node.IndexName}]")))
                .ToList();
            var noFullStorageScan = commandNodes.Count > 0 && commandNodes.All(command =>
                command.Nodes.All(node => !IsFullStorageScan(node.PhysicalOp)));
            var usesCertifiedLeadingIndex = certifiedLeadingColumn is not null && commandNodes.Any(command =>
                command.Nodes.Any(node =>
                    IsIndexedAccess(node.PhysicalOp) &&
                    node.IndexName is not null &&
                    acceptableIndexNames.Contains(node.IndexName)));
            var usesIndexedAccess = noFullStorageScan && usesCertifiedLeadingIndex;

            evidence.Add(new DesignQueryPlanEvidence(
                probe.Query.DocumentKind,
                probe.Query.QueryIdentity,
                probe.Query.ResultOperation.ToString(),
                usesIndexedAccess,
                string.Join(" | ", details)));
        }

        return evidence;
    }

    /// <summary>A full scan of the storage table itself: a heap Table Scan or a Clustered Index Scan.</summary>
    private static bool IsFullStorageScan(string physicalOp) =>
        physicalOp.Contains("Table Scan", StringComparison.OrdinalIgnoreCase) ||
        physicalOp.Contains("Clustered Index Scan", StringComparison.OrdinalIgnoreCase);

    /// <summary>A nonclustered index access path: an Index Seek or a (nonclustered) Index Scan.</summary>
    private static bool IsIndexedAccess(string physicalOp) =>
        !IsFullStorageScan(physicalOp) &&
        !physicalOp.Contains("Clustered", StringComparison.OrdinalIgnoreCase) &&
        physicalOp.Contains("Index", StringComparison.OrdinalIgnoreCase) &&
        (physicalOp.Contains("Seek", StringComparison.OrdinalIgnoreCase) ||
         physicalOp.Contains("Scan", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Refreshes statistics on the probe database so the seeded distinct indexed values produce
    /// selective histograms. The indexed access path itself follows from that selectivity rather than
    /// from any forced planner flag, which SQL Server does not expose per connection.
    /// </summary>
    private static async Task RefreshPlannerStatisticsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var updateStatistics = connection.CreateCommand();
        updateStatistics.CommandText = "EXEC sp_updatestats;";
        await updateStatistics.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<PlanNode> ParsePlanNodes(string nativePlan)
    {
        var nodes = new List<PlanNode>();
        var document = XDocument.Parse(nativePlan, LoadOptions.None);
        foreach (var relOp in document.Descendants().Where(element => element.Name.LocalName == "RelOp"))
        {
            var physicalOp = (string?)relOp.Attribute("PhysicalOp") ?? string.Empty;
            var indexName = relOp.Descendants()
                .Where(element => element.Name.LocalName == "Object")
                .Select(element => ((string?)element.Attribute("Index"))?.Trim('[', ']'))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            nodes.Add(new PlanNode(physicalOp, indexName));
        }

        return nodes;
    }

    private static IReadOnlyList<PlanProbe> BuildProbes()
    {
        const string definitionId = "wf-plan-alpha";
        const string activityId = "act-plan-http";
        var sortKey = Elsa.Primitives.Versioning.SemVer.ToSortKey("1.0.0");

        return
        [
            Probe(Documents("workflowDefinition", "list-definitions-by-id", 50,
                Clause(In("entity.id", [definitionId, "wf-plan-beta"])))),
            Probe(Documents("workflowDefinition", "list-definitions-by-name", 50,
                Clause(Equal("entity.name", "Plan alpha")))),
            Probe(Operation("workflowDefinition", "list-definitions-by-name", BoundedQueryResultOperation.Count,
                Clause(Equal("entity.name", "Plan alpha")))),
            Probe(Documents("workflowDefinition", "list-definitions-by-description", 50,
                Clause(Equal("entity.description", "Plans the order.")))),
            Probe(Documents("workflowDefinition", "search-definitions", 50,
                Clause(Contains("entity.name", "Plan")))),
            Probe(Documents("workflowDefinitionVersion", "list-versions-by-definition", 50,
                Clause(Equal("entity.definitionId", definitionId)))),
            Probe(Operation("workflowDefinitionVersion", "find-version-by-definition-and-sort-key", BoundedQueryResultOperation.Any,
                Clause(Equal("entity.definitionId", definitionId)),
                Clause(Equal("entity.semVerSortKey", sortKey)))),
            Probe(Operation("workflowDefinitionVersion", "find-latest-version", BoundedQueryResultOperation.First,
                Clause(Equal("entity.definitionId", definitionId)))),
            Probe(Documents("workflowDefinitionDraft", "list-drafts-by-definition", 50,
                Clause(Equal("entity.workflowDefinitionId", definitionId)))),
            Probe(Operation("workflowDefinitionDraft", "find-current-draft-by-definition", BoundedQueryResultOperation.First,
                Clause(Equal("entity.workflowDefinitionId", definitionId)))),
            Probe(Operation("workflowDefinitionVersionLayout", "find-layout-by-version", BoundedQueryResultOperation.First,
                Clause(Equal("entity.workflowDefinitionVersionId", $"{definitionId}-v1.0.0")))),
            Probe(Documents("activityDefinition", "list-activity-definitions-by-id", 50,
                Clause(In("entity.id", [activityId, "act-plan-other"])))),
            Probe(Documents("activityDefinition", "list-activity-definitions-by-type-key", 50,
                Clause(Equal("entity.activityTypeKey", "Fixture.Plan.Http")))),
            Probe(Documents("activityDefinition", "list-activity-definitions-by-category", 50,
                Clause(Equal("entity.category", "HTTP")))),
            Probe(Documents("activityDefinition", "list-activity-definitions-by-display-name", 50,
                Clause(Equal("entity.displayName", "Plan HTTP")))),
            Probe(Documents("activityDefinition", "list-activity-definitions-by-description", 50,
                Clause(Contains("entity.description", "plan")))),
            Probe(Documents("activityDefinition", "search-activity-definitions", 50,
                Clause(Contains("entity.displayName", "Plan")))),
            Probe(Documents("activityDefinitionVersion", "list-activity-definition-versions-by-definition", 50,
                Clause(Equal("entity.definitionId", activityId)))),
            Probe(Operation("activityDefinitionVersion", "find-activity-definition-version-by-definition-and-sort-key", BoundedQueryResultOperation.First,
                Clause(Equal("entity.definitionId", activityId)),
                Clause(Equal("entity.semVerSortKey", sortKey))))
        ];
    }

    /// <summary>Seeds one representative aggregate per probed document kind through public commands.</summary>
    private static async Task SeedRepresentativeRowsAsync(
        SqlServerDesignPersistenceContractFixture fixture,
        CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;

        var addWorkflow = services.GetRequiredService<Elsa.Workflows.Design.Persistence.Core.Contracts.IAddWorkflowDefinitionCommand>();
        foreach (var (id, name, description) in new[]
                 {
                     ("wf-plan-alpha", "Plan alpha", "Plans the order."),
                     ("wf-plan-beta", "Plan beta", "A sibling.")
                 })
        {
            await addWorkflow.Execute(
                DesignPersistenceFixtureData.OperationKey($"plan-seed:{id}"),
                DesignPersistenceFixtureData.WorkflowDefinitionAt(id, name, description),
                DesignPersistenceFixtureData.WorkflowDraftFor(id, $"{id}-draft"),
                cancellationToken);
        }

        await services.GetRequiredService<Elsa.Workflows.Design.Persistence.Core.Contracts.IMaterializeWorkflowDefinitionVersionCommand>().Execute(
            DesignPersistenceFixtureData.OperationKey("plan-seed:wf-version"),
            DesignPersistenceFixtureData.WorkflowVersionAt("wf-plan-alpha", "1.0.0", "wf-plan-alpha-v1.0.0"),
            cancellationToken);

        var addActivity = services.GetRequiredService<Elsa.Activities.Design.Persistence.Core.Contracts.IAddActivityDefinitionCommand>();
        await addActivity.Execute(
            DesignPersistenceFixtureData.OperationKey("plan-seed:activity"),
            DesignPersistenceFixtureData.ActivityDefinitionAt(
                "act-plan-http", "Fixture.Plan.Http", "HTTP", "Plan HTTP", "A plan probe."),
            DesignPersistenceFixtureData.ActivityVersion(id: "act-plan-http-v1", definitionId: "act-plan-http"),
            cancellationToken);
    }

    /// <summary>
    /// Seeds a batch of noise aggregates carrying distinct indexed values so every probed equality is
    /// genuinely selective (one match out of many). The noise deliberately avoids the "Plan" tokens the
    /// Contains probes search for, so those probes still match only the representative rows. This is the
    /// SQL Server forced-plan choice: with selective histograms the optimizer chooses the certified
    /// index instead of a trivially cheap small-table scan.
    /// </summary>
    private static async Task SeedNoiseRowsAsync(
        SqlServerDesignPersistenceContractFixture fixture,
        CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;

        var addWorkflow = services.GetRequiredService<Elsa.Workflows.Design.Persistence.Core.Contracts.IAddWorkflowDefinitionCommand>();
        var materializeVersion = services.GetRequiredService<Elsa.Workflows.Design.Persistence.Core.Contracts.IMaterializeWorkflowDefinitionVersionCommand>();
        var addActivity = services.GetRequiredService<Elsa.Activities.Design.Persistence.Core.Contracts.IAddActivityDefinitionCommand>();

        for (var i = 0; i < NoiseAggregatesPerFamily; i++)
        {
            var workflowId = $"wf-noise-{i:D4}";
            await addWorkflow.Execute(
                DesignPersistenceFixtureData.OperationKey($"plan-noise-wf:{workflowId}"),
                DesignPersistenceFixtureData.WorkflowDefinitionAt(workflowId, $"Noise workflow {i:D4}", $"Noise body {i:D4}."),
                DesignPersistenceFixtureData.WorkflowDraftFor(workflowId, $"{workflowId}-draft"),
                cancellationToken);
            await materializeVersion.Execute(
                DesignPersistenceFixtureData.OperationKey($"plan-noise-wf-version:{workflowId}"),
                DesignPersistenceFixtureData.WorkflowVersionAt(workflowId, "1.0.0", $"{workflowId}-v1.0.0"),
                cancellationToken);

            var activityId = $"act-noise-{i:D4}";
            await addActivity.Execute(
                DesignPersistenceFixtureData.OperationKey($"plan-noise-activity:{activityId}"),
                DesignPersistenceFixtureData.ActivityDefinitionAt(
                    activityId, $"Fixture.Noise.{i:D4}", $"CAT{i:D4}", $"Noise {i:D4}", $"Noise body {i:D4}."),
                DesignPersistenceFixtureData.ActivityVersion(id: $"{activityId}-v1", definitionId: activityId),
                cancellationToken);
        }
    }

    private sealed record PlanNode(string PhysicalOp, string? IndexName);

    private sealed record PlanProbe(DocumentQuery Query);

    private static PlanProbe Probe(DocumentQuery query) => new(query);

    private static DocumentQuery Documents(string kind, string identity, int take, params DocumentQueryClause[] clauses) =>
        new(kind, identity, clauses, take: take);

    private static DocumentQuery Operation(
        string kind,
        string identity,
        BoundedQueryResultOperation operation,
        params DocumentQueryClause[] clauses) =>
        new(kind, identity, clauses, resultOperation: operation);

    private static DocumentQueryClause Clause(DocumentQueryComparison comparison) => DocumentQueryClause.Of(comparison);

    private static DocumentQueryComparison Equal(string path, string value) => DocumentQueryComparison.Equal(path, value);

    private static DocumentQueryComparison In(string path, IEnumerable<string?> values) => DocumentQueryComparison.In(path, values);

    private static DocumentQueryComparison Contains(string path, string value) => DocumentQueryComparison.Contains(path, value);
}
