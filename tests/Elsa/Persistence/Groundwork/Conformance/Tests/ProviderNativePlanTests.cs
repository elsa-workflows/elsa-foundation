using System.Globalization;
using System.Reflection;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Core.Scoping;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class ProviderNativePlanTests
{
    private const int AcceptanceDatasetSize = 100_000;
    private const int PageSize = 16;
    private const string ProviderMatrixOptIn = "ELSA_RUN_IDENTITY_NATIVE_PLAN_PROVIDERS";
    private const string TenantId = "provider-native-plan-tenant";
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Native_route_catalog_covers_every_current_scale_bearing_manifest_route()
    {
        var groups = await ScenarioGroupsAsync();
        var declared = groups
            .SelectMany(group => group.Manifest.StorageUnits)
            .SelectMany(unit => unit.PhysicalStorage?.BoundedQueries
                .Where(query => query.ExecutionClass == BoundedQueryExecutionClass.ScaleBearing)
                .Select(query => RouteKey(unit.Identity.Value, query.Identity)) ?? [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var covered = groups
            .SelectMany(group => group.Scenarios)
            .Select(scenario => RouteKey(scenario.Query.DocumentKind, scenario.Query.QueryIdentity))
            .Concat(AspNetCoreIdentityNativePlanTests.EvidenceRouteCatalog()
                .Select(route => RouteKey(route.DocumentKind, route.QueryIdentity)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            declared.SequenceEqual(covered, StringComparer.Ordinal),
            $"Missing native route scenarios: {string.Join(", ", declared.Except(covered, StringComparer.Ordinal))}. " +
            $"Unexpected native route scenarios: {string.Join(", ", covered.Except(declared, StringComparer.Ordinal))}.");
        Assert.Equal(65, declared.Length);
        Assert.DoesNotContain(
            RouteKey(SecretsStorageManifest.SecretDocumentKind, SecretsStorageManifest.SearchFilteredQuery),
            declared);
    }

    [Fact]
    [Trait("Category", "Sqlite")]
    public async Task Sqlite_proves_every_foundation_native_route_at_acceptance_scale() =>
        _ = await CaptureNativeRoutePlansAsync(new SqliteGroundworkProviderDriver());

    [SkippableFact]
    [Trait("Category", "PostgreSql")]
    public async Task PostgreSql_proves_every_foundation_native_route_at_acceptance_scale()
    {
        RequireProviderMatrixOptIn();
        _ = await CaptureNativeRoutePlansAsync(new PostgreSqlGroundworkProviderDriver());
    }

    [SkippableFact]
    [Trait("Category", "SqlServer")]
    public async Task SqlServer_proves_every_foundation_native_route_at_acceptance_scale()
    {
        RequireProviderMatrixOptIn();
        _ = await CaptureNativeRoutePlansAsync(new SqlServerGroundworkProviderDriver());
    }

    [SkippableFact]
    [Trait("Category", "MongoDb")]
    public async Task MongoDb_proves_every_foundation_native_route_at_acceptance_scale()
    {
        RequireProviderMatrixOptIn();
        _ = await CaptureNativeRoutePlansAsync(new MongoDbGroundworkProviderDriver());
    }

    internal static async Task<IReadOnlyList<GroundworkNativeRoutePlanResult>> CaptureNativeRoutePlansAsync(
        GroundworkProviderDriver driver)
    {
        await using (driver)
        {
            await driver.InitializeAsync();
            var results = new List<GroundworkNativeRoutePlanResult>();
            foreach (var group in await ScenarioGroupsAsync())
            {
                await driver.ResetPhysicalAsync([group.Source]);
                await using var client = await driver.OpenPhysicalClientAsync(
                    DocumentStoreAccess.Scoped(new StorageScope(TenantId)));
                var bounded = client.BoundedDocumentStore
                              ?? throw new InvalidOperationException(
                                  "The physical provider did not expose its admitted bounded-query runtime.");
                var explainer = client.PhysicalDocumentQueryExplainer
                                ?? throw new InvalidOperationException(
                                    "The physical provider did not expose native query explanations.");

                foreach (var scenario in group.Scenarios)
                {
                    var plan = explainer.ResolvePlan(
                        scenario.Query,
                        scenario.Query.ResultOperation);
                    if (scenario.Query.QueryIdentity == ElsaRuntimeStorageManifest.PageFaultedWorkflowExecutionsForAttentionQuery)
                    {
                        Assert.Equal(
                            ElsaRuntimeStorageManifest.WorkflowExecutionFaultedAttentionOrderIndex,
                            plan.IndexName?.Identifier);
                    }
                    var request = NativePlanRequest(
                        scenario,
                        plan,
                        driver.Descriptor.ProviderKey);
                    await driver.PrepareNativeRoutePlanDatasetAsync([request]);

                    var materialized = 0;
                    if (scenario.Query.ResultOperation == BoundedQueryResultOperation.Documents)
                    {
                        var queryResult = await bounded.QueryAsync(scenario.Query);
                        materialized = queryResult.Documents.Count;
                        Assert.True(
                            request.MatchingCardinality == queryResult.TotalCount,
                            $"Route '{scenario.Query.QueryIdentity}' expected {request.MatchingCardinality} matches " +
                            $"but the provider returned {queryResult.TotalCount}.");
                        Assert.Equal(
                            ExpectedMatchingIds(request, plan).Take(scenario.Query.Take!.Value),
                            queryResult.Documents.Select(document => document.Id));
                    }
                    else
                    {
                        Assert.Equal(BoundedQueryResultOperation.Count, scenario.Query.ResultOperation);
                        var count = await bounded.CountAsync(scenario.Query);
                        Assert.True(
                            request.MatchingCardinality == count,
                            $"Route '{scenario.Query.QueryIdentity}' expected {request.MatchingCardinality} matches " +
                            $"but the provider returned {count}.");
                    }

                    var explanation = await explainer.ExplainAsync(scenario.Query);
                    Assert.Equal(scenario.Query.DocumentKind, explanation.Plan.StorageUnit.Value);
                    Assert.Equal(scenario.Query.QueryIdentity, explanation.Plan.QueryIdentity);
                    Assert.Equal(plan.Fingerprint, explanation.Plan.Fingerprint);
                    Assert.True(explanation.Plan.IsScaleBearing);
                    AssertDeterministicIdentityTieBreaks(explanation.Plan);
                    Assert.Equal(
                        scenario.Declaration.SortFields.Select(field => (field.Path, field.Direction)),
                        explanation.Plan.Order
                            .Where(order => !order.IsIdentityTieBreak)
                            .Select(order => (order.Path, order.Direction)));
                    Assert.Equal(request.ExpectedCommandKinds, explanation.Commands.Select(command => command.Kind));

                    var result = await driver.CaptureNativeRoutePlanAsync(
                        request,
                        explanation,
                        materialized);
                    Assert.Equal(AcceptanceDatasetSize, result.PhysicalCardinality);
                    Assert.True(result.HasStorageScopePredicate);
                    Assert.True(result.HasRoutePredicate);
                    Assert.Equal(request.Limit, result.FiniteLimit);
                    Assert.Equal(
                        scenario.Query.ResultOperation == BoundedQueryResultOperation.Documents
                            ? Math.Min(
                                scenario.Query.Take!.Value,
                                request.MatchingCardinality)
                            : 0,
                        result.MaterializedCandidateCount);
                    AssertProviderAppliedCommandShape(result, plan);
                    Assert.Equal("provider-native-route-plan", result.Evidence.Kind);
                    Assert.DoesNotContain(TenantId, result.Evidence.Content, StringComparison.Ordinal);
                    Assert.DoesNotContain(
                        request.CandidateDocumentId,
                        result.Evidence.Content,
                        StringComparison.Ordinal);
                    Assert.All(result.Commands, command =>
                        Assert.Matches("^[0-9a-f]{64}$", command.NativePlanSha256));
                    results.Add(result);
                }
            }

            Assert.Equal(55, results.Count);
            Assert.Equal(
                results.Count,
                results.Select(result => RouteKey(
                        result.Request.DocumentKind,
                        result.Request.QueryIdentity))
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            return results;
        }
    }

    private static GroundworkNativeRoutePlanRequest NativePlanRequest(
        NativeRouteScenario scenario,
        PhysicalQueryPlan plan,
        string providerKey)
    {
        var table = ExplicitTableDefinition(
            scenario.Manifest.StorageUnits
                .Single(unit => unit.Identity.Value == scenario.Query.DocumentKind)
                .PhysicalStorage?.Policy
            ?? throw new InvalidOperationException(
                $"Storage unit '{scenario.Query.DocumentKind}' has no physical policy."));
        var projected = table.ProjectedColumns
            .Select(column => DefaultValue(column))
            .ToDictionary(value => value.Field, StringComparer.Ordinal);
        foreach (var comparison in scenario.Query.Clauses.SelectMany(clause => clause.Comparisons))
        {
            var column = table.ProjectedColumns.Single(candidate =>
                string.Equals(candidate.Path, comparison.Path, StringComparison.Ordinal));
            projected[column.LogicalName] = ComparisonValue(column, comparison);
        }

        var requiredPredicates = scenario.Query.Clauses
            .SelectMany(clause => clause.Comparisons)
            .Select(comparison => plan.Predicates.Single(predicate =>
                string.Equals(predicate.Path, comparison.Path, StringComparison.Ordinal)).Field.Identifier)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var routeField = requiredPredicates.First(field => projected.ContainsKey(field));
        var expectedCommands = new List<PhysicalDocumentQueryCommandKind>();
        if (plan.RequiresPrimaryLookup &&
            !string.Equals(providerKey, "mongodb", StringComparison.Ordinal))
            expectedCommands.Add(PhysicalDocumentQueryCommandKind.LinkedIdentityCollisionCheck);
        expectedCommands.Add(PhysicalDocumentQueryCommandKind.Count);
        if (scenario.Query.ResultOperation == BoundedQueryResultOperation.Documents)
        {
            expectedCommands.Add(PhysicalDocumentQueryCommandKind.Page);
            if (plan.RequiresPrimaryLookup &&
                string.Equals(providerKey, "mongodb", StringComparison.Ordinal))
                expectedCommands.Add(PhysicalDocumentQueryCommandKind.PrimaryHydration);
        }

        var sharedStorage = table.SharedStorage is null
            ? null
            : scenario.Manifest.SharedDocumentStorages.Single(storage =>
                storage.Binding == table.SharedStorage);
        var contentColumn = table.Envelope?.CanonicalJsonColumn
                            ?? sharedStorage?.Envelope.CanonicalJsonColumn
                            ?? "document";
        var primaryPhysicalName = plan.RequiresPrimaryLookup
            ? plan.PrimaryObject.Identifier
            : null;
        var varyingProjectedFields = table.Indexes
            .Where(index => index.IsUnique)
            .Select(index =>
            {
                var indexProjectedFields = index.Columns
                .Reverse()
                .Select(column => column.ColumnLogicalName)
                .Where(projected.ContainsKey)
                .ToArray();
                return indexProjectedFields.FirstOrDefault(field =>
                           !requiredPredicates.Contains(field, StringComparer.Ordinal))
                       ?? indexProjectedFields.FirstOrDefault();
            })
            .Where(field => field is not null)
            .Cast<string>()
            .ToList();
        varyingProjectedFields.AddRange(plan.Order
            .Where(order =>
                !order.IsIdentityTieBreak &&
                !requiredPredicates.Contains(order.Field.Identifier, StringComparer.Ordinal) &&
                projected.ContainsKey(order.Field.Identifier))
            .Select(order => order.Field.Identifier));
        if (scenario.Query.LatestPerKeyPath is not null)
        {
            varyingProjectedFields.Add(plan.Order.Single(order =>
                !order.IsIdentityTieBreak &&
                string.Equals(order.Path, scenario.Query.LatestPerKeyPath, StringComparison.Ordinal)).Field.Identifier);
        }
        var selectedIndex = table.Indexes.Single(index =>
            string.Equals(index.LogicalName, scenario.Declaration.IndexIdentity, StringComparison.Ordinal));
        var planPredicateFields = plan.Predicates
            .Select(predicate => predicate.Field.Identifier)
            .ToHashSet(StringComparer.Ordinal);
        var storageScopeColumn = table.Envelope?.StorageScopeColumn
                                 ?? sharedStorage?.Envelope.StorageScopeColumn
                                 ?? "storage_scope";
        var isUniquePointLookup = selectedIndex.IsUnique &&
                                  selectedIndex.Columns.All(column =>
                                      string.Equals(
                                          column.ColumnLogicalName,
                                          storageScopeColumn,
                                          StringComparison.Ordinal) ||
                                      planPredicateFields.Contains(column.ColumnLogicalName));
        return new GroundworkNativeRoutePlanRequest(
            scenario.Query.DocumentKind,
            scenario.Query.QueryIdentity,
            plan.LookupObject.Identifier,
            routeField,
            projected.Keys.Order(StringComparer.Ordinal).ToArray(),
            TenantId,
            projected[routeField].Value,
            scenario.Query.ResultOperation == BoundedQueryResultOperation.Documents
                ? scenario.Query.Take
                : null,
            AcceptanceDatasetSize,
            candidateDocumentId: "native-000000",
            candidateContentJson: "{}",
            candidateSchemaVersion: "1",
            projectedValues: projected.Values.ToArray(),
            requiredPredicateFields: requiredPredicates,
            expectedCommandKinds: expectedCommands,
            indexFields: [routeField],
            evidenceKind: "provider-native-route-plan",
            contentColumn: contentColumn,
            primaryPhysicalName: primaryPhysicalName,
            expectedIndexName: plan.IndexName?.Identifier
                               ?? throw new InvalidOperationException(
                                   $"Scale-bearing route '{scenario.Query.QueryIdentity}' selected no physical index."),
            matchingCardinality:
            scenario.Query.ResultOperation == BoundedQueryResultOperation.Documents && !isUniquePointLookup
                ? Math.Max(
                    scenario.Query.Take!.Value + 1,
                    AcceptanceDatasetSize / 100)
                : 1,
            varyingProjectedFields: varyingProjectedFields);
    }

    private static IEnumerable<string> ExpectedMatchingIds(
        GroundworkNativeRoutePlanRequest request,
        PhysicalQueryPlan plan)
    {
        var ids = Enumerable.Range(0, request.MatchingCardinality + 1)
            .Where(ordinal => ordinal != 1)
            .Select(ordinal => $"native-{ordinal:D6}")
            .ToArray();
        var firstVaryingOrder = plan.Order.FirstOrDefault(order =>
            request.VaryingProjectedFields.Contains(order.Field.Identifier, StringComparer.Ordinal));
        return firstVaryingOrder?.Direction == PhysicalSortDirection.Descending
            ? ids.Reverse()
            : ids;
    }

    private static void AssertProviderAppliedCommandShape(
        GroundworkNativeRoutePlanResult result,
        PhysicalQueryPlan plan)
    {
        var expectedOrder = plan.Order
            .Select(order => (
                FieldIdentifier: order.Field.Identifier,
                order.Direction,
                order.IsIdentityTieBreak))
            .ToArray();
        foreach (var command in result.Commands)
        {
            switch (command.Kind)
            {
                case PhysicalDocumentQueryCommandKind.LinkedIdentityCollisionCheck:
                case PhysicalDocumentQueryCommandKind.Count:
                case PhysicalDocumentQueryCommandKind.Any:
                    Assert.Equal(1, command.ProviderAppliedMaximumRows);
                    Assert.Empty(command.ProviderAppliedOrder);
                    break;
                case PhysicalDocumentQueryCommandKind.Page:
                {
                    var take = result.Request.Limit
                               ?? throw new InvalidOperationException(
                                   "A native Page command requires a logical finite limit.");
                    var expectedMaximum = plan.PagingSupport == QueryPagingSupport.Cursor &&
                                          take < int.MaxValue
                        ? take + 1
                        : take;
                    Assert.Equal(expectedMaximum, command.ProviderAppliedMaximumRows);
                    Assert.Equal(
                        expectedOrder,
                        command.ProviderAppliedOrder.Select(order => (
                            order.FieldIdentifier,
                            order.Direction,
                            order.IsIdentityTieBreak)));
                    break;
                }
                case PhysicalDocumentQueryCommandKind.First:
                    Assert.Equal(1, command.ProviderAppliedMaximumRows);
                    Assert.Equal(
                        expectedOrder,
                        command.ProviderAppliedOrder.Select(order => (
                            order.FieldIdentifier,
                            order.Direction,
                            order.IsIdentityTieBreak)));
                    break;
                case PhysicalDocumentQueryCommandKind.PrimaryHydration:
                    Assert.Equal(result.MaterializedCandidateCount, command.ProviderAppliedMaximumRows);
                    Assert.Empty(command.ProviderAppliedOrder);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(command.Kind),
                        command.Kind,
                        "Unknown provider-native command kind.");
            }
        }
    }

    private static void AssertDeterministicIdentityTieBreaks(PhysicalQueryPlan plan)
    {
        var tieBreaks = plan.Order
            .Where(order => order.IsIdentityTieBreak)
            .ToArray();
        Assert.InRange(tieBreaks.Length, 1, 2);
        Assert.Equal(PhysicalDocumentFieldPaths.StorageScope, tieBreaks[0].Path);
        Assert.Equal(PhysicalSortDirection.Ascending, tieBreaks[0].Direction);
        if (tieBreaks.Length == 2)
        {
            Assert.Equal(PhysicalDocumentFieldPaths.Id, tieBreaks[1].Path);
            Assert.Equal(PhysicalSortDirection.Ascending, tieBreaks[1].Direction);
        }
    }

    private static GroundworkNativeRouteProjectedValue DefaultValue(ProjectedColumnDefinition column) =>
        column.Type switch
        {
            PortablePhysicalType.String =>
                GroundworkNativeRouteProjectedValue.String(column.LogicalName, $"target-{column.LogicalName}"),
            PortablePhysicalType.Boolean =>
                GroundworkNativeRouteProjectedValue.Boolean(column.LogicalName, true),
            PortablePhysicalType.Int32 or PortablePhysicalType.Int64 or PortablePhysicalType.Decimal =>
                GroundworkNativeRouteProjectedValue.Int64(column.LogicalName, 1),
            PortablePhysicalType.DateTime =>
                GroundworkNativeRouteProjectedValue.DateTime(column.LogicalName, Now),
            PortablePhysicalType.Guid =>
                GroundworkNativeRouteProjectedValue.String(
                    column.LogicalName,
                    "11111111-1111-1111-1111-111111111111"),
            _ => throw new InvalidOperationException(
                $"Native-route seeding does not support projected type '{column.Type}' on '{column.LogicalName}'.")
        };

    private static GroundworkNativeRouteProjectedValue ComparisonValue(
        ProjectedColumnDefinition column,
        DocumentQueryComparison comparison)
    {
        var value = comparison.Values.FirstOrDefault(candidate => candidate is not null)
                    ?? DefaultValue(column).Value;
        return column.Type switch
        {
            PortablePhysicalType.String =>
                StringComparisonValue(column.LogicalName, value, comparison.Operator),
            PortablePhysicalType.Boolean =>
                BooleanComparisonValue(column.LogicalName, bool.Parse(value), comparison.Operator),
            PortablePhysicalType.Int32 or PortablePhysicalType.Int64 or PortablePhysicalType.Decimal =>
                NumberComparisonValue(
                    column.LogicalName,
                    long.Parse(value, CultureInfo.InvariantCulture),
                    comparison.Operator),
            PortablePhysicalType.DateTime =>
                DateComparisonValue(
                    column.LogicalName,
                    DateTimeOffset.Parse(
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind),
                    comparison.Operator),
            _ => DefaultValue(column)
        };
    }

    private static GroundworkNativeRouteProjectedValue StringComparisonValue(
        string field,
        string value,
        QueryComparisonOperator operation) =>
        operation switch
        {
            QueryComparisonOperator.Equal or QueryComparisonOperator.In =>
                GroundworkNativeRouteProjectedValue.String(field, value),
            QueryComparisonOperator.StartsWith =>
                GroundworkNativeRouteProjectedValue.String(field, $"{value}target", "nonmatching"),
            QueryComparisonOperator.NotEqual =>
                GroundworkNativeRouteProjectedValue.String(
                    field,
                    string.Equals(value, "native-target", StringComparison.Ordinal)
                        ? "native-other"
                        : "native-target",
                    value),
            _ => throw new InvalidOperationException(
                $"Native-route string oracle does not support '{operation}'.")
        };

    private static GroundworkNativeRouteProjectedValue BooleanComparisonValue(
        string field,
        bool value,
        QueryComparisonOperator operation) =>
        operation switch
        {
            QueryComparisonOperator.Equal or QueryComparisonOperator.In =>
                GroundworkNativeRouteProjectedValue.Boolean(field, value, !value),
            QueryComparisonOperator.NotEqual =>
                GroundworkNativeRouteProjectedValue.Boolean(field, !value, value),
            _ => throw new InvalidOperationException(
                $"Native-route Boolean oracle does not support '{operation}'.")
        };

    private static GroundworkNativeRouteProjectedValue NumberComparisonValue(
        string field,
        long value,
        QueryComparisonOperator operation)
    {
        var (target, noise) = operation switch
        {
            QueryComparisonOperator.Equal or QueryComparisonOperator.In => (value, checked(value + 1)),
            QueryComparisonOperator.NotEqual => (checked(value + 1), value),
            QueryComparisonOperator.GreaterThan => (checked(value + 1), value),
            QueryComparisonOperator.GreaterThanOrEqual => (value, checked(value - 1)),
            QueryComparisonOperator.LessThan => (checked(value - 1), value),
            QueryComparisonOperator.LessThanOrEqual => (value, checked(value + 1)),
            _ => throw new InvalidOperationException(
                $"Native-route number oracle does not support '{operation}'.")
        };
        return GroundworkNativeRouteProjectedValue.Int64(field, target, noise);
    }

    private static GroundworkNativeRouteProjectedValue DateComparisonValue(
        string field,
        DateTimeOffset value,
        QueryComparisonOperator operation)
    {
        var (target, noise) = operation switch
        {
            QueryComparisonOperator.Equal or QueryComparisonOperator.In => (value, value.AddTicks(1)),
            QueryComparisonOperator.NotEqual => (value.AddTicks(1), value),
            QueryComparisonOperator.GreaterThan => (value.AddTicks(1), value),
            QueryComparisonOperator.GreaterThanOrEqual => (value, value.AddTicks(-1)),
            QueryComparisonOperator.LessThan => (value.AddTicks(-1), value),
            QueryComparisonOperator.LessThanOrEqual => (value, value.AddTicks(1)),
            _ => throw new InvalidOperationException(
                $"Native-route date oracle does not support '{operation}'.")
        };
        return GroundworkNativeRouteProjectedValue.DateTime(field, target, noise);
    }

    private static async Task<IReadOnlyList<NativeRouteScenarioGroup>> ScenarioGroupsAsync()
    {
        var runtimeSource = new RuntimeGroundworkStorageManifestSource();
        var identitySource = new IdentityGroundworkStorageManifestSource();
        var secretsSource = new SecretsGroundworkStorageManifestSource();
        var distributedSource = new DistributedGroundworkStorageManifestSource();
        var runtime = (await runtimeSource.CreateDeclarationAsync()).Manifest;
        var identity = (await identitySource.CreateDeclarationAsync()).Manifest;
        var secrets = (await secretsSource.CreateDeclarationAsync()).Manifest;
        var distributed = (await distributedSource.CreateDeclarationAsync()).Manifest;

        return
        [
            Group(runtimeSource, runtime, RuntimeQueries()),
            Group(identitySource, identity,
                [
                    Documents(
                        IdentityStorageManifest.IdentityClaimMappingDocumentKind,
                        IdentityStorageManifest.ListClaimMappingsByProviderQuery,
                        [Equal(IdentityStorageManifest.ProviderLookupKeyField, "provider-key")],
                        take: 512,
                        skip: 0),
                    Documents(
                        IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                        IdentityStorageManifest.ListExpiredMutationReceiptsQuery,
                        [
                            LessThanOrEqual(
                                IdentityStorageManifest.MutationReceiptExpiresAtField,
                                Now.ToString("O", CultureInfo.InvariantCulture))
                        ],
                        [
                            new DocumentQueryOrder(
                                IdentityStorageManifest.MutationReceiptExpiresAtField)
                        ],
                        take: 256)
                ]),
            Group(secretsSource, secrets,
                [
                    Documents(
                        SecretsStorageManifest.SecretDocumentKind,
                        SecretsStorageManifest.ListFilteredQuery,
                        [
                            Equal(SecretsStorageManifest.TenantIdField, "tenant-a"),
                            Equal(SecretsStorageManifest.StatusField, "active"),
                            DocumentQueryClause.AnyOf(
                                DocumentQueryComparison.Equal(
                                    SecretsStorageManifest.HasNonExpiringActiveVersionField,
                                    bool.TrueString.ToLowerInvariant()),
                                DocumentQueryComparison.GreaterThan(
                                    SecretsStorageManifest.MaxActiveVersionExpiresAtField,
                                    Now.ToString("O", CultureInfo.InvariantCulture)))
                        ],
                        [new DocumentQueryOrder(SecretsStorageManifest.NormalizedNameField)])
                ]),
            Group(distributedSource, distributed, DistributedQueries())
        ];
    }

    private static IReadOnlyList<DocumentQuery> RuntimeQueries()
    {
        var detected = ((int)RuntimeInterruptionStatus.Detected).ToString(CultureInfo.InvariantCulture);
        var now = Now.ToString("O", CultureInfo.InvariantCulture);
        DocumentQueryOrder[] expiresAtAndReferenceIdOrder =
        [
            new(ElsaRuntimeStorageManifest.ExpiresAtField),
            new(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField)
        ];
        const string owner = "owner-a";
        return
        [
            Documents(
                ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind,
                ElsaRuntimeStorageManifest.PageActivityExecutionInspectionSummariesQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, "workflow-execution-id")],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryScheduledAtField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryActivityExecutionIdField)
                ]),
            Documents(
                ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind,
                ElsaRuntimeStorageManifest.FindLatestActivityExecutionHierarchyByWorkflowQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, "workflow-execution-id")],
                [
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                        PhysicalSortDirection.Descending),
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField,
                        PhysicalSortDirection.Descending)
                ]),
            Documents(
                ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind,
                ElsaRuntimeStorageManifest.PageActivityExecutionHierarchyByWorkflowQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, "workflow-execution-id")],
                [
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                        PhysicalSortDirection.Descending),
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField,
                        PhysicalSortDirection.Descending)
                ]),
            Documents(
                ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind,
                ElsaRuntimeStorageManifest.PageActivityExecutionHierarchyByScopeQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, "workflow-execution-id"),
                    Equal(ElsaRuntimeStorageManifest.ExecutionScopeIdField, "execution-scope-id"),
                    Equal(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyIsScopeRootField,
                        bool.FalseString.ToLowerInvariant()),
                    LessThanOrEqual(
                        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
                        "3")
                ],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.ActivityExecutionHierarchyActivityExecutionIdField)
                ]),
            Documents(
                ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
                ElsaRuntimeStorageManifest.PageActivityExecutionStatesByWorkflowExecutionQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, "workflow-execution-id")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.ActivityExecutionIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
                ElsaRuntimeStorageManifest.PageActivityExecutionStatesByParentQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, "workflow-execution-id"),
                    Equal(ElsaRuntimeStorageManifest.ParentActivityExecutionIdField, "parent-activity-execution-id")
                ],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.ActivityExecutionIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
                ElsaRuntimeStorageManifest.ListBookmarksByWorkflowExecutionQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, "workflow-execution-id")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.BookmarkIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
                ElsaRuntimeStorageManifest.ListBookmarksByStimulusAndTypeQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField,
                        "ec5d562ccdd26af6aed8db7035f0ffb5e1c18b998a4e3b0fbecc1d0e96efeca9")
                ],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.BookmarkIdField)
                ]),
            Documents(
                ElsaRuntimeStorageManifest.BookmarkStateDocumentKind,
                ElsaRuntimeStorageManifest.ListBookmarksByStimulusTypeQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.BookmarkStimulusTypeLookupKeyField,
                        "4e1f49a9c8ae8a158434e647c0c410352d718984a4fcd144d26cca3c394caa02")
                ],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.BookmarkIdField)
                ]),
            Documents(
                ElsaRuntimeStorageManifest.DurableValueStateDocumentKind,
                ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, "workflow-execution-id")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.DurableValueIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
                ElsaRuntimeStorageManifest.PageWorkflowExecutablesQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.CollectionField,
                        ElsaRuntimeStorageManifest.WorkflowExecutableCollection)
                ],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutableArtifactIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
                ElsaRuntimeStorageManifest.FindExecutableActivityTemplateByHashQuery,
                [Equal(ElsaRuntimeStorageManifest.TemplateHashField, "template-hash")]),
            Documents(
                ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
                ElsaRuntimeStorageManifest.PageExecutableActivityTemplatesQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.CollectionField,
                        ElsaRuntimeStorageManifest.ExecutableActivityTemplateCollection)
                ],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.ExecutableActivityTemplateIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
                ElsaRuntimeStorageManifest.PageWorkflowExecutableSourceReferencesQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.CollectionField,
                        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection)
                ],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
                ElsaRuntimeStorageManifest.PageWorkflowExecutableSourceReferencesByScopeQuery,
                [Equal(ElsaRuntimeStorageManifest.ScopeField, "scope")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
                ElsaRuntimeStorageManifest.PageWorkflowExecutableSourceReferencesByArtifactQuery,
                [Equal(ElsaRuntimeStorageManifest.ArtifactIdField, "artifact-id")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
                ElsaRuntimeStorageManifest.PageLiveWorkflowExecutableSourceReferencesQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.CollectionField,
                        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection),
                    Equal(ElsaRuntimeStorageManifest.IsRetiredField, bool.FalseString.ToLowerInvariant()),
                    GreaterThan(ElsaRuntimeStorageManifest.ExpiresAtField, now)
                ],
                expiresAtAndReferenceIdOrder),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
                ElsaRuntimeStorageManifest.PageLiveWorkflowExecutableSourceReferencesByScopeQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.ScopeField, "scope"),
                    Equal(ElsaRuntimeStorageManifest.IsRetiredField, bool.FalseString.ToLowerInvariant()),
                    GreaterThan(ElsaRuntimeStorageManifest.ExpiresAtField, now)
                ],
                expiresAtAndReferenceIdOrder),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
                ElsaRuntimeStorageManifest.BatchExpiredWorkflowExecutableSourceReferencesQuery,
                [LessThanOrEqual(ElsaRuntimeStorageManifest.ExpiresAtField, now)],
                expiresAtAndReferenceIdOrder),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
                ElsaRuntimeStorageManifest.BatchRetiredWorkflowExecutableSourceReferencesQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.IsRetiredField, bool.TrueString.ToLowerInvariant()),
                    GreaterThan(ElsaRuntimeStorageManifest.ExpiresAtField, now)
                ],
                expiresAtAndReferenceIdOrder),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
                ElsaRuntimeStorageManifest.FindLiveWorkflowExecutableSourceReferenceByArtifactQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.ArtifactIdField, "artifact-id"),
                    Equal(ElsaRuntimeStorageManifest.IsRetiredField, bool.FalseString.ToLowerInvariant()),
                    GreaterThan(ElsaRuntimeStorageManifest.ExpiresAtField, now)
                ],
                expiresAtAndReferenceIdOrder),
            Documents(
                ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
                ElsaRuntimeStorageManifest.PageExecutionLivenessStatesByWorkflowExecutionQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, "workflow-execution-id")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
                ElsaRuntimeStorageManifest.PageExecutionLivenessStatesQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.CollectionField,
                        ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind)
                ],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.ExecutionLivenessOperationalStateIdField)
                ]),
            Documents(
                ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
                ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, "workflow-execution-id")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField)]),
            Documents(
                ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
                ElsaRuntimeStorageManifest.ListPendingSchedulerWorkflowExecutionsQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.CollectionField,
                        ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind)
                ],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.SchedulerWorkOrderKeyField)
                ])
                .LatestPerKey(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
            Documents(
                ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
                ElsaRuntimeStorageManifest.PageDurableTimersByWorkflowExecutionQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, "workflow-execution-id")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.DurableTimerIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind,
                ElsaRuntimeStorageManifest.PageRecurringTriggerSchedulesByPublicationQuery,
                [Equal(ElsaRuntimeStorageManifest.RecurringTriggerSchedulePublicationIdField, "publication-id")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind,
                ElsaRuntimeStorageManifest.PageRecurringTriggerSchedulesByArtifactQuery,
                [Equal(ElsaRuntimeStorageManifest.ArtifactIdField, "artifact-id")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                ElsaRuntimeStorageManifest.PagePinnedExecutableArtifactIdsQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.CollectionField,
                        ElsaRuntimeStorageManifest.WorkflowExecutionStateCollection)
                ],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField),
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField)
                ])
                .LatestPerKey(ElsaRuntimeStorageManifest.WorkflowExecutionHistoryArtifactIdField),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
                ElsaRuntimeStorageManifest.ListTriggerBindingsByPublicationQuery,
                [Equal(ElsaRuntimeStorageManifest.PublicationIdField, "publication-id")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.TriggerBindingIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
                ElsaRuntimeStorageManifest.ListTriggerBindingsByArtifactQuery,
                [Equal(ElsaRuntimeStorageManifest.ArtifactIdField, "artifact-id")],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.TriggerBindingIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.DurableTimerDocumentKind,
                ElsaRuntimeStorageManifest.ListDueDurableTimersQuery,
                [LessThanOrEqual(ElsaRuntimeStorageManifest.DurableTimerDueTimeField, now)],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.DurableTimerDueTimeField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.DurableTimerIdField)
                ]),
            Documents(
                ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind,
                ElsaRuntimeStorageManifest.ListDueRecurringTriggerSchedulesQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField,
                        bool.TrueString.ToLowerInvariant()),
                    LessThanOrEqual(
                        ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField,
                        now)
                ],
                [
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField),
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField),
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField)
                ]),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
                ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusAndTypeQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusLookupKeyField,
                        "ec5d562ccdd26af6aed8db7035f0ffb5e1c18b998a4e3b0fbecc1d0e96efeca9"),
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                        bool.TrueString.ToLowerInvariant())
                ],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.TriggerBindingIdField)]),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
                ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusTypeQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeLookupKeyField,
                        "4e1f49a9c8ae8a158434e647c0c410352d718984a4fcd144d26cca3c394caa02"),
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
                        bool.TrueString.ToLowerInvariant())
                ],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.TriggerBindingIdField)]),
            Recovery(
                ElsaRuntimeStorageManifest.ListRecoveryDetectedQuery,
                [Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField, detected)],
                ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField,
                ElsaRuntimeStorageManifest.RecoveryInterruptedAtField),
            Recovery(
                ElsaRuntimeStorageManifest.ListRecoveryDetectedByLeaseOwnerQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField, detected),
                    Equal(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField, owner)
                ],
                ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField,
                ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField,
                ElsaRuntimeStorageManifest.RecoveryInterruptedAtField),
            Recovery(
                ElsaRuntimeStorageManifest.ListRecoveryDetectedByHeartbeatOwnerQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField, detected),
                    Equal(ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField, owner)
                ],
                ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField,
                ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField,
                ElsaRuntimeStorageManifest.RecoveryInterruptedAtField),
            Recovery(
                ElsaRuntimeStorageManifest.ListRecoveryDetectedOwnerlessQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField, detected),
                    Equal(
                        ElsaRuntimeStorageManifest.RecoveryHasOperationalOwnerField,
                        bool.FalseString)
                ],
                ElsaRuntimeStorageManifest.RecoveryInterruptionStatusField,
                ElsaRuntimeStorageManifest.RecoveryHasOperationalOwnerField,
                ElsaRuntimeStorageManifest.RecoveryInterruptedAtField),
            Recovery(
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryQuery,
                [LessThanOrEqual(ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField, now)],
                ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField),
            Recovery(
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryAndOwnerQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField, owner),
                    LessThanOrEqual(ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField, now)
                ],
                ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField,
                ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField),
            Recovery(
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseAcquisitionQuery,
                [LessThanOrEqual(ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField, now)],
                ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField),
            Recovery(
                ElsaRuntimeStorageManifest.ListRecoveryByLeaseAcquisitionAndOwnerQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField, owner),
                    LessThanOrEqual(ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField, now)
                ],
                ElsaRuntimeStorageManifest.RecoveryLeaseOwnerIdField,
                ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField),
            Recovery(
                ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatQuery,
                [LessThanOrEqual(ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField, now)],
                ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField),
            Recovery(
                ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatAndOwnerQuery,
                [
                    Equal(ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField, owner),
                    LessThanOrEqual(
                        ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField,
                        now)
                ],
                ElsaRuntimeStorageManifest.RecoveryHeartbeatOwnerIdField,
                ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                ElsaRuntimeStorageManifest.PageWorkflowExecutionsQuery,
                [
                    LessThanOrEqual(
                        ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                        Now.UtcTicks.ToString(CultureInfo.InvariantCulture)),
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField,
                        TenantId)
                ],
                [
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                        PhysicalSortDirection.Descending),
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField)
                ]),
            Documents(
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                ElsaRuntimeStorageManifest.PageFaultedWorkflowExecutionsForAttentionQuery,
                [
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowExecutionHistoryTenantIdField,
                        TenantId),
                    Equal(
                        ElsaRuntimeStorageManifest.WorkflowExecutionHistoryStatusField,
                        ((int)WorkflowExecutionStatus.Faulted).ToString(CultureInfo.InvariantCulture))
                ],
                [
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.WorkflowExecutionHistorySortTicksField,
                        PhysicalSortDirection.Descending),
                    new DocumentQueryOrder(
                        ElsaRuntimeStorageManifest.WorkflowExecutionHistoryWorkflowExecutionIdField)
                ]),
            Documents(
                ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
                ElsaRuntimeStorageManifest.PageAttentionIncidentsByStatusQuery,
                [Equal(
                    ElsaRuntimeStorageManifest.StatusField,
                    ((int)IncidentStatus.Open).ToString(CultureInfo.InvariantCulture))],
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.CreatedAtField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.IncidentIdField)
                ])
        ];
    }

    private static IReadOnlyList<DocumentQuery> DistributedQueries()
    {
        var now = Now.ToString("O", CultureInfo.InvariantCulture);
        return
        [
            Documents(
                DistributedRuntimeStorageManifest.ExecutionPlacementDocumentKind,
                DistributedGroundworkStorageManifest.ListOwnedPlacementsQuery,
                [
                    Equal(DistributedGroundworkStorageManifest.OwnerIdLookupKeyField, "owner-lookup"),
                    Equal(
                        DistributedGroundworkStorageManifest.OwnerIdComparisonKeyField,
                        "owner-comparison"),
                    DocumentQueryClause.Of(DocumentQueryComparison.GreaterThan(
                        DistributedGroundworkStorageManifest.ExpiresAtField,
                        now))
                ],
                [
                    new DocumentQueryOrder(
                        DistributedGroundworkStorageManifest.OwnerIdLookupKeyField),
                    new DocumentQueryOrder(DistributedGroundworkStorageManifest.ExpiresAtField),
                    new DocumentQueryOrder(
                        DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField)
                ]),
            Documents(
                DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind,
                DistributedGroundworkStorageManifest.LeaseVisibleCommandsQuery,
                [
                    Equal(
                        DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField,
                        "workflow-key"),
                    LessThanOrEqual(DistributedGroundworkStorageManifest.VisibleAtField, now)
                ],
                [new DocumentQueryOrder(DistributedGroundworkStorageManifest.SequenceField)]),
            Documents(
                    DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind,
                    DistributedGroundworkStorageManifest.ListPendingExecutionIdsQuery,
                    [
                        Equal(
                            DistributedGroundworkStorageManifest.CollectionField,
                            DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind),
                        LessThanOrEqual(
                            DistributedGroundworkStorageManifest.VisibleAtField,
                            now)
                    ],
                    [
                        new DocumentQueryOrder(
                            DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField),
                        new DocumentQueryOrder(
                            DistributedGroundworkStorageManifest.SequenceField,
                            PhysicalSortDirection.Descending)
                    ])
                .LatestPerKey(DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField),
            new DocumentQuery(
                DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind,
                DistributedGroundworkStorageManifest.CountPendingCommandsQuery,
                [
                    Equal(
                        DistributedGroundworkStorageManifest.WorkflowExecutionIdKeyField,
                        "workflow-key")
                ],
                resultOperation: BoundedQueryResultOperation.Count)
        ];
    }

    private static DocumentQuery Recovery(
        string identity,
        IReadOnlyList<DocumentQueryClause> clauses,
        params string[] order) =>
        Documents(
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            identity,
            clauses,
            order.Select(path => new DocumentQueryOrder(path)).ToArray());

    private static DocumentQuery Documents(
        string documentKind,
        string queryIdentity,
        IReadOnlyList<DocumentQueryClause> clauses,
        IReadOnlyList<DocumentQueryOrder>? order = null,
        int take = PageSize,
        int? skip = null) =>
        new(documentKind, queryIdentity, clauses, order, skip, take);

    private static DocumentQueryClause Equal(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value));

    private static DocumentQueryClause LessThanOrEqual(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(path, value));

    private static DocumentQueryClause GreaterThan(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.GreaterThan(path, value));

    private static DocumentQueryClause StartsWith(string path, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.StartsWith(path, value));

    private static NativeRouteScenarioGroup Group(
        IGroundworkStorageManifestSource source,
        StorageManifest manifest,
        IReadOnlyList<DocumentQuery> queries) =>
        new(
            source,
            manifest,
            queries.Select(query =>
            {
                var declaration = manifest.StorageUnits
                    .Single(unit => unit.Identity.Value == query.DocumentKind)
                    .PhysicalStorage?.BoundedQueries
                    .Single(candidate => candidate.Identity == query.QueryIdentity)
                    ?? throw new InvalidOperationException(
                        $"Route '{query.DocumentKind}/{query.QueryIdentity}' is not declared.");
                Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, declaration.ExecutionClass);
                return new NativeRouteScenario(manifest, declaration, query);
            }).ToArray());

    private static string RouteKey(string documentKind, string queryIdentity) =>
        $"{documentKind}/{queryIdentity}";

    private static void RequireProviderMatrixOptIn() =>
        Skip.IfNot(
            string.Equals(
                Environment.GetEnvironmentVariable(ProviderMatrixOptIn),
                "1",
                StringComparison.Ordinal),
            $"Set {ProviderMatrixOptIn}=1 to run the 100,000-record external-provider native-plan matrix.");

    private static PhysicalTableDefinition ExplicitTableDefinition(PhysicalStoragePolicy policy)
    {
        var property = policy.GetType().GetProperty(
            "Definition",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?? throw new InvalidOperationException(
                           $"Physical policy '{policy.GetType().Name}' does not expose a table definition.");
        return Assert.IsType<PhysicalTableDefinition>(property.GetValue(policy));
    }

    private sealed record NativeRouteScenarioGroup(
        IGroundworkStorageManifestSource Source,
        StorageManifest Manifest,
        IReadOnlyList<NativeRouteScenario> Scenarios);

    private sealed record NativeRouteScenario(
        StorageManifest Manifest,
        BoundedQueryDeclaration Declaration,
        DocumentQuery Query);
}
