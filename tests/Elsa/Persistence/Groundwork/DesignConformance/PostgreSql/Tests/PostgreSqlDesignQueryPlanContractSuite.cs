using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.PostgreSql;
using Groundwork.PostgreSql.Documents;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests;

/// <summary>
/// T040 on PostgreSQL: renders every required design route probe through the same composed manifest,
/// routes, and provider runtime the admitted session store uses, then captures the native
/// <c>EXPLAIN (FORMAT JSON)</c> verdict for each rendered command. A probe passes only when PostgreSQL
/// resolves the selective predicate through the route's certified index (an Index / Index Only /
/// Bitmap Index Scan naming that exact index) with no sequential scan of the storage table.
/// </summary>
/// <remarks>
/// On the tiny per-probe seed sets PostgreSQL's cost model can legitimately prefer a sequential scan
/// over an index, which would make the plan shape non-deterministic. To capture stable plan-shape
/// evidence the probe database is switched to <c>enable_seqscan = off</c> (applied with
/// <c>ALTER DATABASE ... SET</c> so every connection the physical store opens inherits it), forcing
/// the planner to reveal the indexed access path it would use at scale. This mirrors how plan
/// evidence is captured elsewhere and is documented here as the deterministic choice for this leaf.
/// </remarks>
[Collection(PostgreSqlDesignProviderCollection.Name)]
public sealed class PostgreSqlDesignQueryPlanContractSuite(PostgreSqlDesignProviderFixture container)
    : DesignQueryPlanContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IReadOnlyList<DesignQueryPlanEvidence>> CaptureCatalogPlansAsync(
        CancellationToken cancellationToken = default)
    {
        await using var fixture = await PostgreSqlDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
        await fixture.ValidateReadinessAsync(cancellationToken);
        await SeedRepresentativeRowsAsync(fixture, cancellationToken);
        await RefreshPlannerStatisticsAsync(fixture.PostgreSqlConnectionString, cancellationToken);

        // The probe store carries enable_seqscan=off as a per-connection libpq startup option baked
        // into its own connection string. Because that option is part of the Npgsql pool key it is
        // applied to every backend the store opens, deterministically forcing the planner to reveal
        // the certified indexed access path rather than a small-table sequential scan.
        var probeConnectionString = new NpgsqlConnectionStringBuilder(fixture.PostgreSqlConnectionString)
        {
            Options = "-c enable_seqscan=off"
        }.ConnectionString;

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var capabilities = await GroundworkProviderCapabilitySnapshotBuilder.ForSelectedSourcesAsync(
            PostgreSqlGroundworkCapabilities.Runtime(),
            new GroundworkProviderTopologySnapshot(
                PostgreSqlGroundworkCapabilities.Provider.Name,
                "postgresql-server",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            services.GetServices<IGroundworkStorageManifestSource>(),
            cancellationToken);
        var source = await services
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(capabilities, PostgreSqlGroundworkCapabilities.PhysicalNames, cancellationToken);
        var manifest = source.CreateManifest();
        var access = DocumentStoreAccess.Scoped(new StorageScope(DesignPersistenceFixtureData.ScopeA));

        var store = new PostgreSqlPhysicalDocumentStore(
            probeConnectionString,
            manifest,
            source.PhysicalTarget.Routes,
            access);

        var evidence = new List<DesignQueryPlanEvidence>();
        foreach (var probe in BuildProbes())
        {
            var route = source.PhysicalTarget.Routes.Single(candidate =>
                string.Equals(candidate.StorageUnit.Value, probe.Query.DocumentKind, StringComparison.Ordinal));
            var boundedStore = PostgreSqlPhysicalQueryRuntime.Create(store, manifest, route, source.PhysicalTarget.Provider);
            var explainer = (IPhysicalDocumentQueryExplainer)boundedStore;
            var explanation = await explainer.ExplainAsync(probe.Query, cancellationToken);

            // A selective probe is indexed only when (a) the compiled plan certified an index for the
            // route, (b) some explained command positively scans (Index Scan, Index Only Scan, or
            // Bitmap Index Scan) through that certified index OR another declared physical index of the
            // same route that shares the certified index's leading column, and (c) no command
            // sequentially scans the storage table. PostgreSQL reports these node kinds in
            // EXPLAIN (FORMAT JSON) with an "Index Name" field and a bare table scan as a "Seq Scan".
            //
            // Provider-semantic note: the design manifest declares overlapping physical indexes that
            // co-lead with the same field (for example definition-by-name over (name, id) and
            // definition-by-search over (name, id, description)). PostgreSQL's cost-based planner may
            // resolve a name lookup through either — both are genuine selective index scans on the same
            // leading column — so this leaf accepts any route index sharing the certified leading
            // column rather than pinning the exact certified index, while still rejecting a sequential
            // scan or a wrong-column/point-index scan.
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
                        node.IndexName is null ? node.NodeType : $"{node.NodeType}[{node.IndexName}]")))
                .ToList();
            var noSequentialScan = commandNodes.Count > 0 && commandNodes.All(command =>
                command.Nodes.All(node =>
                    !node.NodeType.Contains("Seq Scan", StringComparison.OrdinalIgnoreCase)));
            var usesCertifiedLeadingIndex = certifiedLeadingColumn is not null && commandNodes.Any(command =>
                command.Nodes.Any(node =>
                    node.NodeType.Contains("Index", StringComparison.OrdinalIgnoreCase) &&
                    node.IndexName is not null &&
                    acceptableIndexNames.Contains(node.IndexName)));
            var usesIndexedAccess = noSequentialScan && usesCertifiedLeadingIndex;

            evidence.Add(new DesignQueryPlanEvidence(
                probe.Query.DocumentKind,
                probe.Query.QueryIdentity,
                probe.Query.ResultOperation.ToString(),
                usesIndexedAccess,
                string.Join(" | ", details)));
        }

        return evidence;
    }

    /// <summary>
    /// Refreshes planner statistics on the probe database so the captured plan shape reflects the
    /// seeded rows. The indexed access path itself is forced deterministically by the probe store's
    /// <c>enable_seqscan=off</c> connection option rather than by table size.
    /// </summary>
    private static async Task RefreshPlannerStatisticsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var analyze = connection.CreateCommand();
        analyze.CommandText = "ANALYZE;";
        await analyze.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<PlanNode> ParsePlanNodes(string nativePlan)
    {
        var nodes = new List<PlanNode>();
        using var document = JsonDocument.Parse(nativePlan);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.TryGetProperty("Plan", out var plan))
                CollectPlanNodes(plan, nodes);
        }

        return nodes;
    }

    private static void CollectPlanNodes(JsonElement plan, List<PlanNode> nodes)
    {
        var nodeType = plan.TryGetProperty("Node Type", out var nodeTypeElement)
            ? nodeTypeElement.GetString() ?? string.Empty
            : string.Empty;
        var indexName = plan.TryGetProperty("Index Name", out var indexElement)
            ? indexElement.GetString()
            : null;
        nodes.Add(new PlanNode(nodeType, indexName));

        if (plan.TryGetProperty("Plans", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                CollectPlanNodes(child, nodes);
        }
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
        PostgreSqlDesignPersistenceContractFixture fixture,
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

    private sealed record PlanNode(string NodeType, string? IndexName);

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
