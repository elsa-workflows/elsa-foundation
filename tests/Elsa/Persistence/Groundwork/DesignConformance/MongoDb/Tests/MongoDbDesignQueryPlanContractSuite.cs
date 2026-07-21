using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.MongoDb;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.MongoDb;
using Groundwork.MongoDb.Documents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests;

/// <summary>
/// T054 on MongoDB: renders every required design route probe through the same composed manifest,
/// routes and provider runtime the admitted session store uses, then captures the native MongoDB
/// <c>explain</c> (<c>verbosity: queryPlanner</c>) verdict for each rendered command. A probe passes
/// only when the <em>winning</em> plan resolves the selective predicate through an <c>IXSCAN</c> over
/// the route's certified index (or a declared route index sharing the leading key) with no
/// <c>COLLSCAN</c> of the storage collection.
/// </summary>
/// <remarks>
/// The physical store is opened over the exact per-fixture database that the schema tool already
/// applied. MongoDB's multi-planner selects the indexed access path for these purpose-built route
/// collections even on the tiny per-probe seed sets (the same behaviour Groundwork's own provider
/// tests rely on), so no planner override is needed to capture a stable winning-plan shape. Each
/// explained command carries its native MongoDB explain document as a JSON <c>NativePlan</c> string;
/// the analysis descends into the <c>winningPlan</c> subtree only, so sibling <c>rejectedPlans</c>
/// never contaminate the verdict, and it reads the classic <c>stage</c>/<c>indexName</c> fields that
/// MongoDB reports for both the classic and slot-based (<c>queryPlan</c>) executor shapes.
/// </remarks>
[Collection(MongoDbDesignProviderCollection.Name)]
public sealed class MongoDbDesignQueryPlanContractSuite(MongoDbDesignProviderFixture container)
    : DesignQueryPlanContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IReadOnlyList<DesignQueryPlanEvidence>> CaptureCatalogPlansAsync(
        CancellationToken cancellationToken = default)
    {
        await using var fixture = await MongoDbDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
        await fixture.ValidateReadinessAsync(cancellationToken);
        await SeedRepresentativeRowsAsync(fixture, cancellationToken);

        var connectionString = fixture.MongoDbConnectionString;
        var databaseName = fixture.MongoDbDatabaseName;

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;

        // Compose the same physical source the admitted runtime store uses: the transaction-capable
        // replica-set topology, the transaction-capable capability report, and the MongoDB name
        // normalizer plus target compiler. This yields the exact manifest/provider/name policy the
        // schema tool already applied, so opening a physical handle admits without drift.
        var topology = await new MongoDbGroundworkRuntimeAdmission().InspectReplicaSetAsync(
            connectionString,
            databaseName,
            cancellationToken);
        var capabilities = await GroundworkProviderCapabilitySnapshotBuilder.ForSelectedSourcesAsync(
            MongoDbGroundworkCapabilities.RuntimeForTransactionCapableDeployment(),
            topology,
            services.GetServices<IGroundworkStorageManifestSource>(),
            cancellationToken);
        var source = await services
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(
                capabilities,
                MongoDbPhysicalNameNormalizer.Instance,
                cancellationToken,
                MongoDbGroundworkPhysicalSchemaTargetCompiler.Instance);

        var access = DocumentStoreAccess.Scoped(new StorageScope(DesignPersistenceFixtureData.ScopeA));
        await using var handle = await MongoDbDocumentStoreFactory.OpenPhysicalAsync(
            connectionString,
            databaseName,
            source.CreateManifest(),
            source.PhysicalTarget.Provider,
            access,
            source.CreateNamePolicy(),
            cancellationToken: cancellationToken);
        var store = handle.Store;

        var evidence = new List<DesignQueryPlanEvidence>();
        foreach (var probe in BuildProbes())
        {
            var route = source.PhysicalTarget.Routes.Single(candidate =>
                string.Equals(candidate.StorageUnit.Value, probe.Query.DocumentKind, StringComparison.Ordinal));
            var explanation = await store.ExplainAsync(probe.Query, cancellationToken);

            // A selective probe is indexed only when (a) the compiled plan certified an index for the
            // route, (b) some explained command's winning plan positively scans (IXSCAN) through that
            // certified index OR another declared physical index of the same route that shares the
            // certified index's leading column, and (c) no command's winning plan collection-scans.
            // Reading the winning-plan subtree in isolation keeps rejected candidate plans (which
            // MongoDB lists as siblings under queryPlanner) out of the verdict.
            //
            // Provider-semantic note: the design manifest declares overlapping physical indexes that
            // co-lead with the same field (for example definition-by-name and definition-by-search both
            // leading on name). MongoDB's cost-based planner may resolve a name lookup through either —
            // both are genuine selective index scans on the same leading column — so this leaf accepts
            // any route index sharing the certified leading column rather than pinning the exact
            // certified index, while still rejecting a collection scan or a wrong-column index scan.
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

            var commandShapes = explanation.Commands
                .Select(command => (command.Identity, Shape: InspectWinningPlan(command.NativePlan)))
                .ToList();
            var details = commandShapes
                .Select(command => $"{command.Identity} (plan index: {plannedIndex ?? "<none>"}; accepts: {string.Join(",", acceptableIndexNames)}): " +
                    $"stages {string.Join("/", command.Shape.Stages)}; indexes {(command.Shape.IndexNames.Count == 0 ? "<none>" : string.Join(",", command.Shape.IndexNames))}")
                .ToList();
            var noCollectionScan = commandShapes.Count > 0 && commandShapes.All(command =>
                !command.Shape.Stages.Contains("COLLSCAN"));
            var usesCertifiedLeadingIndex = certifiedLeadingColumn is not null && commandShapes.Any(command =>
                command.Shape.Stages.Contains("IXSCAN") &&
                command.Shape.IndexNames.Any(acceptableIndexNames.Contains));
            var usesIndexedAccess = noCollectionScan && usesCertifiedLeadingIndex;

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
    /// Descends into the first <c>winningPlan</c> subtree of a native MongoDB explain document and
    /// collects the classic <c>stage</c> node names and <c>indexName</c> values MongoDB reports for
    /// both the classic and slot-based executor shapes, ignoring sibling <c>rejectedPlans</c>.
    /// </summary>
    private static WinningPlanShape InspectWinningPlan(string nativePlan)
    {
        var stages = new List<string>();
        var indexNames = new List<string>();
        using var document = JsonDocument.Parse(nativePlan);
        FindWinningPlan(document.RootElement, stages, indexNames);
        return new WinningPlanShape(stages, indexNames);

        static void FindWinningPlan(JsonElement element, List<string> stages, List<string> indexNames)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("winningPlan"))
                            Collect(property.Value, stages, indexNames);
                        else
                            FindWinningPlan(property.Value, stages, indexNames);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        FindWinningPlan(item, stages, indexNames);
                    break;
            }
        }

        static void Collect(JsonElement element, List<string> stages, List<string> indexNames)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("stage") && property.Value.ValueKind == JsonValueKind.String)
                            stages.Add(property.Value.GetString()!);
                        else if (property.NameEquals("indexName") && property.Value.ValueKind == JsonValueKind.String)
                            indexNames.Add(property.Value.GetString()!);
                        Collect(property.Value, stages, indexNames);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        Collect(item, stages, indexNames);
                    break;
            }
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
        MongoDbDesignPersistenceContractFixture fixture,
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

    private sealed record WinningPlanShape(IReadOnlyList<string> Stages, IReadOnlyList<string> IndexNames);

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
