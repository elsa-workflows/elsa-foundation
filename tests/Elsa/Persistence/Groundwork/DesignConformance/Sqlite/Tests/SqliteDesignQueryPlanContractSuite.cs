using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite;
using Groundwork.Sqlite.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>
/// T040 on SQLite: renders every required design route probe through the same composed manifest,
/// routes, and provider runtime the admitted session store uses, then captures the native
/// <c>EXPLAIN QUERY PLAN</c> verdict for each rendered command. A probe passes only when SQLite
/// resolves the selective predicate through an index (SEARCH/USING INDEX) rather than a bare table
/// scan.
/// </summary>
public sealed class SqliteDesignQueryPlanContractSuite : DesignQueryPlanContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IReadOnlyList<DesignQueryPlanEvidence>> CaptureCatalogPlansAsync(
        CancellationToken cancellationToken = default)
    {
        await using var fixture = await SqliteDesignPersistenceContractFixture.CreateAsync(_telemetry, cancellationToken);
        await fixture.ValidateReadinessAsync(cancellationToken);
        await SeedRepresentativeRowsAsync(fixture, cancellationToken);

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        var capabilities = await GroundworkProviderCapabilitySnapshotBuilder.ForSelectedSourcesAsync(
            SqliteGroundworkCapabilities.Runtime(),
            new GroundworkProviderTopologySnapshot(
                SqliteGroundworkCapabilities.Provider.Name,
                "sqlite-file",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            services.GetServices<IGroundworkStorageManifestSource>(),
            cancellationToken);
        var source = await services
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(capabilities, SqliteGroundworkCapabilities.PhysicalNames, cancellationToken);
        var manifest = source.CreateManifest();
        var access = DocumentStoreAccess.Scoped(new StorageScope(DesignPersistenceFixtureData.ScopeA));

        await using var connection = SqliteConnectionFactory.Create(fixture.SqliteConnectionString);
        await connection.OpenAsync(cancellationToken);
        var store = new SqlitePhysicalDocumentStore(connection, manifest, source.PhysicalTarget.Routes, access);

        var evidence = new List<DesignQueryPlanEvidence>();
        foreach (var probe in BuildProbes())
        {
            var route = source.PhysicalTarget.Routes.Single(candidate =>
                string.Equals(candidate.StorageUnit.Value, probe.Query.DocumentKind, StringComparison.Ordinal));
            var boundedStore = SqlitePhysicalQueryRuntime.Create(store, manifest, route, source.PhysicalTarget.Provider);
            var explainer = (IPhysicalDocumentQueryExplainer)boundedStore;
            var explanation = await explainer.ExplainAsync(probe.Query, cancellationToken);

            // A selective probe is indexed only when (a) the compiled plan certified an index for
            // the route, (b) some explained command positively SEARCHes using that exact index (or
            // performs a covering-index-only pass over it), and (c) no command contains a bare
            // table scan. SQLite reports indexed access as "SEARCH ... USING [COVERING] INDEX ..."
            // and heap-free passes as "SCAN ... USING COVERING INDEX ...".
            var plannedIndex = explanation.Plan.IndexName?.Identifier;
            var commandLines = explanation.Commands
                .Select(command => (command.Identity, Lines: command.NativePlan
                    .ReplaceLineEndings("\n")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
                .ToList();
            var details = commandLines
                .Select(command => $"{command.Identity} (plan index: {plannedIndex ?? "<none>"}): {string.Join(" / ", command.Lines)}")
                .ToList();
            var noBareScan = commandLines.Count > 0 && commandLines.All(command =>
                command.Lines.All(line =>
                    !line.StartsWith("SCAN ", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("USING", StringComparison.OrdinalIgnoreCase)));
            var usesPlannedIndex = plannedIndex is not null && commandLines.Any(command =>
                command.Lines.Any(line =>
                    (line.StartsWith("SEARCH ", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("USING COVERING INDEX", StringComparison.OrdinalIgnoreCase)) &&
                    line.Contains(plannedIndex, StringComparison.OrdinalIgnoreCase)));
            var usesIndexedAccess = noBareScan && usesPlannedIndex;

            evidence.Add(new DesignQueryPlanEvidence(
                probe.Query.DocumentKind,
                probe.Query.QueryIdentity,
                probe.Query.ResultOperation.ToString(),
                usesIndexedAccess,
                string.Join(" | ", details)));
        }

        return evidence;
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
        SqliteDesignPersistenceContractFixture fixture,
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
