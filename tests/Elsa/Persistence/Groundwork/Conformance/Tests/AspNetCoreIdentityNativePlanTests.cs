using System.Security.Claims;
using System.Reflection;
using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentityNativePlanTests
{
    private const int AcceptanceDatasetSize = 100_000;
    private const string ProviderMatrixOptIn = "ELSA_RUN_IDENTITY_NATIVE_PLAN_PROVIDERS";

    [Fact]
    public void Identity_manifest_declares_physical_bounded_routes_for_every_normalized_and_relationship_route()
    {
        var manifest = IdentityStorageManifest.Create();
        var requiredRoutes = RequiredRoutes();

        Assert.Equal(10, requiredRoutes.Count);

        foreach (var route in requiredRoutes)
        {
            var unit = manifest.StorageUnits.Single(candidate => candidate.Identity.Value == route.DocumentKind);
            var physical = unit.PhysicalStorage ?? throw new InvalidOperationException($"Unit '{route.DocumentKind}' is not physicalized.");
            var table = ExplicitTableDefinition(physical.Policy);
            var boundedQuery = physical.BoundedQueries.SingleOrDefault(candidate => candidate.Identity == route.QueryIdentity)
                               ?? throw new InvalidOperationException($"Route '{route.QueryIdentity}' is not a physical bounded query.");
            var index = table.Indexes.SingleOrDefault(candidate => candidate.LogicalName == boundedQuery.IndexIdentity)
                        ?? throw new InvalidOperationException($"Route '{route.QueryIdentity}' does not have a physical index.");

            Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, boundedQuery.ExecutionClass);
            Assert.Contains(index.Columns, column => column.ColumnLogicalName == "storage_scope" && column.Order == 0);
            Assert.Contains(index.Columns, column => column.ColumnLogicalName == route.Field && column.Order == 1);
            Assert.Contains(table.ProjectedColumns, column => column.LogicalName == route.Field && column.Type == PortablePhysicalType.String);
        }
    }

    [Fact]
    [Trait("Category", "Sqlite")]
    public async Task Sqlite_proves_every_identity_native_route_against_real_acceptance_scale_data() =>
        _ = await CaptureNativeRoutePlansAsync(new SqliteGroundworkProviderDriver());

    [SkippableFact]
    [Trait("Category", "PostgreSql")]
    public async Task PostgreSql_proves_every_identity_native_route_against_real_acceptance_scale_data()
    {
        RequireProviderMatrixOptIn();
        _ = await CaptureNativeRoutePlansAsync(new PostgreSqlGroundworkProviderDriver());
    }

    [SkippableFact]
    [Trait("Category", "SqlServer")]
    public async Task SqlServer_proves_every_identity_native_route_against_real_acceptance_scale_data()
    {
        RequireProviderMatrixOptIn();
        _ = await CaptureNativeRoutePlansAsync(new SqlServerGroundworkProviderDriver());
    }

    [SkippableFact]
    [Trait("Category", "MongoDb")]
    public async Task MongoDb_proves_every_identity_native_route_against_real_acceptance_scale_data()
    {
        RequireProviderMatrixOptIn();
        _ = await CaptureNativeRoutePlansAsync(new MongoDbGroundworkProviderDriver());
    }

    internal static async Task<AspNetCoreIdentityNativePlanEvidence> CaptureNativeRoutePlansAsync(
        GroundworkProviderDriver driver)
    {
        await using (driver)
        {
            await driver.InitializeAsync(CancellationToken.None);
            await driver.ResetPhysicalAsync([new IdentityGroundworkStorageManifestSource()], CancellationToken.None);
            var scenarios = NativeRouteScenarios();
            var requests = NativePlanRequests(scenarios);
            await driver.PrepareNativeRoutePlanDatasetAsync(requests, CancellationToken.None);

            var results = new List<GroundworkNativeRoutePlanResult>(requests.Count);
            await using (var evidenceClient = await driver.OpenPhysicalClientAsync(
                             DocumentStoreAccess.Scoped(new StorageScope(AspNetCoreIdentityObservableScenario.TenantId)),
                             CancellationToken.None))
            {
                var boundedStore = evidenceClient.BoundedDocumentStore
                                   ?? throw new InvalidOperationException("The physical provider did not expose its admitted bounded-query runtime.");
                var explainer = evidenceClient.PhysicalDocumentQueryExplainer
                                ?? throw new InvalidOperationException("The physical provider did not expose native query explanations.");
                var recorder = new RecordingBoundedDocumentStore(boundedStore);
                var stores = new NativeIdentityStores(evidenceClient.DocumentStore, recorder);
                foreach (var scenario in scenarios)
                {
                    recorder.Clear();
                    var materializedCandidateCount = await scenario.ExecuteAsync(stores, CancellationToken.None);
                    Assert.True(
                        materializedCandidateCount == 1,
                        $"Route '{scenario.Route.QueryIdentity}' materialized {materializedCandidateCount} candidates; expected one.");
                    var query = Assert.Single(recorder.Queries.Where(candidate =>
                        string.Equals(candidate.QueryIdentity, scenario.Route.QueryIdentity, StringComparison.Ordinal)));
                    AssertCapturedQuery(scenario.Route, scenario.RouteValue, query);

                    var explanation = await explainer.ExplainAsync(query, CancellationToken.None);
                    Assert.Equal(query.QueryIdentity, explanation.Plan.QueryIdentity);
                    Assert.Collection(
                        explanation.Commands,
                        command => Assert.Equal(PhysicalDocumentQueryCommandKind.Count, command.Kind),
                        command => Assert.Equal(PhysicalDocumentQueryCommandKind.Page, command.Kind));
                    var request = requests.Single(candidate =>
                        string.Equals(candidate.QueryIdentity, scenario.Route.QueryIdentity, StringComparison.Ordinal));
                    results.Add(await driver.CaptureNativeRoutePlanAsync(
                        request,
                        explanation,
                        materializedCandidateCount,
                        CancellationToken.None));
                }

                Assert.Equal(RequiredRoutes().Count, results.Count);
                Assert.Equal(6, results.Select(result => result.Request.PhysicalName).Distinct(StringComparer.Ordinal).Count());
                Assert.All(results, result =>
                {
                    Assert.Equal(AcceptanceDatasetSize, result.PhysicalCardinality);
                    Assert.True(result.HasStorageScopePredicate);
                    Assert.True(result.HasRoutePredicate);
                    Assert.Equal(result.Request.Limit, result.FiniteLimit);
                    Assert.Equal(1, result.MaterializedCandidateCount);
                    Assert.False(string.IsNullOrWhiteSpace(result.PlanClassification));
                    Assert.False(string.IsNullOrWhiteSpace(result.IndexName));
                    Assert.Collection(
                        result.Commands,
                        command => Assert.Equal(PhysicalDocumentQueryCommandKind.Count, command.Kind),
                        command => Assert.Equal(PhysicalDocumentQueryCommandKind.Page, command.Kind));
                    Assert.All(result.Commands, command =>
                    {
                        Assert.Contains(result.IndexName, command.IndexNames, StringComparer.Ordinal);
                        Assert.Matches("^[0-9a-f]{64}$", command.NativePlanSha256);
                    });
                    Assert.Equal("identity-native-route-plan", result.Evidence.Kind);
                    Assert.DoesNotContain(result.Request.StorageScope, result.Evidence.Content, StringComparison.Ordinal);
                    Assert.DoesNotContain(result.Request.RouteValue, result.Evidence.Content, StringComparison.Ordinal);
                });
            }

            await driver.ResetPhysicalAsync([new IdentityGroundworkStorageManifestSource()], CancellationToken.None);
            await using var client = await driver.OpenPhysicalClientAsync(
                DocumentStoreAccess.Scoped(new StorageScope(AspNetCoreIdentityObservableScenario.TenantId)),
                CancellationToken.None);
            var workload = await new AspNetCoreIdentityPerformanceWorkload().ExecuteAsync(
                new AspNetCoreIdentityPhysicalWorkloadTarget(
                    client,
                    results.Where(result => AspNetCoreIdentityPerformanceWorkload.RequiredNativeRoutes.Contains(
                        result.Request.QueryIdentity,
                        StringComparer.Ordinal)).ToArray()),
                CancellationToken.None);

            Assert.Equal(driver.Descriptor.ProviderIdentity, workload.ProviderIdentity);
            Assert.Equal(AspNetCoreIdentityPerformanceWorkload.ExpectedInputFingerprint, workload.InputFingerprint);
            Assert.Equal(AspNetCoreIdentityPerformanceWorkload.ExpectedResultDigest, workload.ResultDigest);
            Assert.Equal(
                AspNetCoreIdentityPerformanceWorkload.RequiredNativeRoutes.Order(StringComparer.Ordinal),
                workload.NativeRouteEvidence.Select(evidence => evidence.RouteIdentity).Order(StringComparer.Ordinal));
            Assert.Contains("reject-stale-revision-update", workload.ObservableOperations);
            return new AspNetCoreIdentityNativePlanEvidence(results, workload);
        }
    }

    private static IReadOnlyList<GroundworkNativeRoutePlanRequest> NativePlanRequests(
        IReadOnlyCollection<NativeRouteScenario> scenarios)
    {
        var manifest = IdentityStorageManifest.Create();
        return scenarios
            .Select(scenario =>
            {
                var route = scenario.Route;
                var unit = manifest.StorageUnits.Single(candidate => candidate.Identity.Value == route.DocumentKind);
                var physical = unit.PhysicalStorage ?? throw new InvalidOperationException($"Unit '{route.DocumentKind}' is not physicalized.");
                var table = ExplicitTableDefinition(physical.Policy);
                return new GroundworkNativeRoutePlanRequest(
                    route.DocumentKind,
                    route.QueryIdentity,
                    table.FeatureDefaultLogicalName ?? throw new InvalidOperationException($"Unit '{route.DocumentKind}' has no physical name."),
                    route.Field,
                    table.ProjectedColumns.Select(column => column.LogicalName).ToArray(),
                    AspNetCoreIdentityObservableScenario.TenantId,
                    scenario.RouteValue,
                    route.Limit,
                    AcceptanceDatasetSize,
                    scenario.CandidateDocumentId,
                    scenario.CandidateContentJson,
                    IdentityStorageManifest.SchemaVersion);
            })
            .ToArray();
    }

    private static IReadOnlyList<NativeRoute> RequiredRoutes() =>
    [
        new(IdentityStorageManifest.IdentityUserDocumentKind, IdentityStorageManifest.FindUserByNormalizedNameQuery, IdentityStorageManifest.NormalizedUserNameKeyField, 1),
        new(IdentityStorageManifest.IdentityUserDocumentKind, IdentityStorageManifest.FindUserByNormalizedEmailQuery, IdentityStorageManifest.NormalizedEmailKeyField, 2),
        new(IdentityStorageManifest.IdentityRoleDocumentKind, IdentityStorageManifest.FindRoleByNormalizedNameQuery, IdentityStorageManifest.NormalizedRoleNameKeyField, 1),
        new(IdentityStorageManifest.IdentityRoleDocumentKind, IdentityStorageManifest.ListRolesByTenantQuery, IdentityStorageManifest.TenantIdField, 512),
        new(IdentityStorageManifest.UserClaimDocumentKind, IdentityStorageManifest.ListUserClaimsQuery, IdentityStorageManifest.UserLookupKeyField, 512),
        new(IdentityStorageManifest.UserClaimDocumentKind, IdentityStorageManifest.FindUsersByClaimQuery, IdentityStorageManifest.ClaimKeyField, 512),
        new(IdentityStorageManifest.RoleClaimDocumentKind, IdentityStorageManifest.ListRoleClaimsQuery, IdentityStorageManifest.RoleLookupKeyField, 512),
        new(IdentityStorageManifest.UserRoleDocumentKind, IdentityStorageManifest.ListUserRolesQuery, IdentityStorageManifest.UserLookupKeyField, 512),
        new(IdentityStorageManifest.UserRoleDocumentKind, IdentityStorageManifest.ListRoleUsersQuery, IdentityStorageManifest.RoleLookupKeyField, 512),
        new(IdentityStorageManifest.ExternalLoginDocumentKind, IdentityStorageManifest.ListUserLoginsQuery, IdentityStorageManifest.UserLookupKeyField, 512)
    ];

    internal static IReadOnlyList<AspNetCoreIdentityNativeRouteContract> EvidenceRouteCatalog()
    {
        var manifest = IdentityStorageManifest.Create();
        return RequiredRoutes().Select(route =>
        {
            var unit = manifest.StorageUnits.Single(candidate => candidate.Identity.Value == route.DocumentKind);
            var physical = unit.PhysicalStorage
                           ?? throw new InvalidOperationException($"Unit '{route.DocumentKind}' is not physicalized.");
            var table = ExplicitTableDefinition(physical.Policy);
            var boundedQuery = physical.BoundedQueries.Single(candidate => candidate.Identity == route.QueryIdentity);
            return new AspNetCoreIdentityNativeRouteContract(
                route.DocumentKind,
                route.QueryIdentity,
                route.Field,
                route.Limit,
                table.FeatureDefaultLogicalName
                ?? throw new InvalidOperationException($"Unit '{route.DocumentKind}' has no physical name."),
                boundedQuery.IndexIdentity);
        }).ToArray();
    }

    private static IReadOnlyList<NativeRouteScenario> NativeRouteScenarios()
    {
        var tenantId = AspNetCoreIdentityObservableScenario.TenantId;
        var userId = AspNetCoreIdentityObservableScenario.UserId;
        var roleId = AspNetCoreIdentityObservableScenario.RoleId;
        var userLookupKey = IdentityDocumentId.From(tenantId, userId);
        var roleLookupKey = IdentityDocumentId.From(tenantId, roleId);
        var userNameKey = IdentityDocumentId.From(tenantId, AspNetCoreIdentityObservableScenario.NormalizedUserName);
        var emailKey = IdentityDocumentId.From(tenantId, AspNetCoreIdentityObservableScenario.NormalizedEmail);
        var roleNameKey = IdentityDocumentId.From(tenantId, AspNetCoreIdentityObservableScenario.NormalizedRoleName);
        var claim = new Claim("permission", "workflow.read");
        var claimKey = IdentityDocumentId.From(tenantId, claim.Type, claim.Value);
        var userDocument = new IdentityUserDocument(
            tenantId,
            userId,
            AspNetCoreIdentityObservableScenario.NormalizedUserName,
            AspNetCoreIdentityObservableScenario.NormalizedEmail,
            userNameKey,
            emailKey,
            new UserRecord(
                userId,
                tenantId,
                AspNetCoreIdentityObservableScenario.UserName,
                AspNetCoreIdentityObservableScenario.Email,
                "Ada Lovelace",
                UserStatus.Active,
                ResourceOwnership.Foundation,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        var roleDocument = new IdentityRoleDocument(
            tenantId,
            roleId,
            AspNetCoreIdentityObservableScenario.NormalizedRoleName,
            roleNameKey,
            new RoleRecord(
                roleId,
                tenantId,
                AspNetCoreIdentityObservableScenario.RoleName,
                Description: null,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                System: false));
        var documents = new Dictionary<string, (string Id, string Content)>(StringComparer.Ordinal)
        {
            [IdentityStorageManifest.IdentityUserDocumentKind] = (
                IdentityCompositeDocumentId.From(tenantId, userId),
                Serialize(userDocument)),
            [IdentityStorageManifest.IdentityRoleDocumentKind] = (
                IdentityCompositeDocumentId.From(tenantId, roleId),
                Serialize(roleDocument)),
            [IdentityStorageManifest.UserClaimDocumentKind] = (
                IdentityDocumentId.From("native-user-claim", tenantId, userId),
                Serialize(new IdentityUserClaimDocument(tenantId, userId, claim.Type, claim.Value, claimKey, userLookupKey))),
            [IdentityStorageManifest.RoleClaimDocumentKind] = (
                IdentityDocumentId.From("native-role-claim", tenantId, roleId),
                Serialize(new IdentityRoleClaimDocument(tenantId, roleId, claim.Type, claim.Value, roleLookupKey))),
            [IdentityStorageManifest.UserRoleDocumentKind] = (
                IdentityDocumentId.From("native-user-role", tenantId, userId, roleId),
                Serialize(new IdentityUserRoleDocument(tenantId, userId, roleId, userLookupKey, roleLookupKey))),
            [IdentityStorageManifest.ExternalLoginDocumentKind] = (
                IdentityDocumentId.From("native-login", tenantId, userId),
                Serialize(new IdentityExternalLoginDocument(
                    tenantId,
                    userId,
                    "oidc",
                    "native-subject",
                    IdentityDocumentId.From(tenantId, "oidc", "native-subject"),
                    "Native OIDC",
                    new ExternalIdentityRecord(
                        tenantId,
                        "oidc",
                        "native-subject",
                        userId,
                        DateTimeOffset.UnixEpoch,
                        LastSeenAt: null,
                        ExternalIdentityLinkPolicy.Admin),
                    userLookupKey)))
        };
        var routeValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IdentityStorageManifest.FindUserByNormalizedNameQuery] = userNameKey,
            [IdentityStorageManifest.FindUserByNormalizedEmailQuery] = emailKey,
            [IdentityStorageManifest.FindRoleByNormalizedNameQuery] = roleNameKey,
            [IdentityStorageManifest.ListRolesByTenantQuery] = IdentityCompositeDocumentId.Normalize(tenantId),
            [IdentityStorageManifest.ListUserClaimsQuery] = userLookupKey,
            [IdentityStorageManifest.FindUsersByClaimQuery] = claimKey,
            [IdentityStorageManifest.ListRoleClaimsQuery] = roleLookupKey,
            [IdentityStorageManifest.ListUserRolesQuery] = userLookupKey,
            [IdentityStorageManifest.ListRoleUsersQuery] = roleLookupKey,
            [IdentityStorageManifest.ListUserLoginsQuery] = userLookupKey
        };
        var executions = new Dictionary<string, Func<NativeIdentityStores, CancellationToken, Task<int>>>(StringComparer.Ordinal)
        {
            [IdentityStorageManifest.FindUserByNormalizedNameQuery] = async (stores, token) =>
                await stores.Users.FindByNameAsync(AspNetCoreIdentityObservableScenario.NormalizedUserName, token) is null ? 0 : 1,
            [IdentityStorageManifest.FindUserByNormalizedEmailQuery] = async (stores, token) =>
                await stores.Users.FindByEmailAsync(AspNetCoreIdentityObservableScenario.NormalizedEmail, token) is null ? 0 : 1,
            [IdentityStorageManifest.FindRoleByNormalizedNameQuery] = async (stores, token) =>
                await stores.IdentityRoles.FindByNameAsync(AspNetCoreIdentityObservableScenario.NormalizedRoleName, token) is null ? 0 : 1,
            [IdentityStorageManifest.ListRolesByTenantQuery] = async (stores, token) =>
                (await stores.Roles.ListAsync(AspNetCoreIdentityObservableScenario.TenantId, token)).Count,
            [IdentityStorageManifest.ListUserClaimsQuery] = async (stores, token) =>
                (await stores.Users.GetClaimsAsync(stores.User(), token)).Count,
            [IdentityStorageManifest.FindUsersByClaimQuery] = async (stores, token) =>
                (await stores.Users.GetUsersForClaimAsync(claim, token)).Count,
            [IdentityStorageManifest.ListRoleClaimsQuery] = async (stores, token) =>
                (await stores.IdentityRoles.GetClaimsAsync(stores.Role(), token)).Count,
            [IdentityStorageManifest.ListUserRolesQuery] = async (stores, token) =>
                (await stores.Users.GetRolesAsync(stores.User(), token)).Count,
            [IdentityStorageManifest.ListRoleUsersQuery] = async (stores, token) =>
                (await stores.Users.GetUsersInRoleAsync(AspNetCoreIdentityObservableScenario.NormalizedRoleName, token)).Count,
            [IdentityStorageManifest.ListUserLoginsQuery] = async (stores, token) =>
                (await stores.Users.GetLoginsAsync(stores.User(), token)).Count
        };

        return RequiredRoutes().Select(route =>
        {
            var document = documents[route.DocumentKind];
            return new NativeRouteScenario(
                route,
                routeValues[route.QueryIdentity],
                document.Id,
                document.Content,
                executions[route.QueryIdentity]);
        }).ToArray();

        static string Serialize<T>(T value) => JsonSerializer.Serialize(value, IdentityGroundworkJson.Options);
    }

    private static void AssertCapturedQuery(NativeRoute route, string routeValue, DocumentQuery query)
    {
        Assert.Equal(route.DocumentKind, query.DocumentKind);
        Assert.Equal(route.QueryIdentity, query.QueryIdentity);
        Assert.Equal(route.Limit, query.Take);
        Assert.Equal(BoundedQueryResultOperation.Documents, query.ResultOperation);
        var clause = Assert.Single(query.Clauses);
        var comparison = Assert.Single(clause.Comparisons);
        Assert.Equal(route.Field, comparison.Path);
        Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
        Assert.Equal(routeValue, Assert.Single(comparison.Values));
    }

    private sealed record NativeRouteScenario(
        NativeRoute Route,
        string RouteValue,
        string CandidateDocumentId,
        string CandidateContentJson,
        Func<NativeIdentityStores, CancellationToken, Task<int>> ExecuteAsync);

    private sealed class NativeIdentityStores
    {
        public NativeIdentityStores(IDocumentStore documents, IBoundedDocumentStore queries)
        {
            var accessor = new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(
                new PersistenceScope(AspNetCoreIdentityObservableScenario.TenantId)));
            Users = new GroundworkIdentityUserStore(documents, accessor, queries);
            IdentityRoles = new GroundworkIdentityRoleStore(documents, accessor, queries);
            Roles = new Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkRoleStore(
                documents,
                accessor,
                queries);
        }

        public GroundworkIdentityUserStore Users { get; }
        public GroundworkIdentityRoleStore IdentityRoles { get; }
        public Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkRoleStore Roles { get; }

        public AspNetCoreIdentityUser User() => AspNetCoreIdentityObservableScenario.CreateUser(
            AspNetCoreIdentityObservableScenario.UserId,
            AspNetCoreIdentityObservableScenario.UserName,
            AspNetCoreIdentityObservableScenario.NormalizedUserName,
            AspNetCoreIdentityObservableScenario.Email,
            AspNetCoreIdentityObservableScenario.NormalizedEmail);

        public IdentityRole Role() => AspNetCoreIdentityObservableScenario.CreateRole(
            AspNetCoreIdentityObservableScenario.RoleId,
            AspNetCoreIdentityObservableScenario.RoleName,
            AspNetCoreIdentityObservableScenario.NormalizedRoleName);
    }

    private sealed class RecordingBoundedDocumentStore(IBoundedDocumentStore inner) : IBoundedDocumentStore
    {
        private readonly List<DocumentQuery> queries = [];

        public IReadOnlyList<DocumentQuery> Queries => queries;

        public void Clear() => queries.Clear();

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            queries.Add(query);
            return inner.QueryAsync(query, cancellationToken);
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            queries.Add(query);
            return inner.CountAsync(query, cancellationToken);
        }

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            queries.Add(query);
            return inner.FirstOrDefaultAsync(query, cancellationToken);
        }

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            queries.Add(query);
            return inner.AnyAsync(query, cancellationToken);
        }
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private static void RequireProviderMatrixOptIn() =>
        Skip.IfNot(
            string.Equals(Environment.GetEnvironmentVariable(ProviderMatrixOptIn), "1", StringComparison.Ordinal),
            $"Set {ProviderMatrixOptIn}=1 to run the 100,000-record external-provider native-plan matrix.");

    private sealed record NativeRoute(string DocumentKind, string QueryIdentity, string Field, int Limit);

    private static PhysicalTableDefinition ExplicitTableDefinition(PhysicalStoragePolicy policy)
    {
        var property = policy.GetType().GetProperty(
            "Definition",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                       ?? throw new InvalidOperationException($"Physical policy '{policy.GetType().Name}' does not expose a table definition.");
        return Assert.IsType<PhysicalTableDefinition>(property.GetValue(policy));
    }
}

internal sealed record AspNetCoreIdentityNativePlanEvidence(
    IReadOnlyList<GroundworkNativeRoutePlanResult> Routes,
    AspNetCoreIdentityWorkloadResult Workload);

internal sealed record AspNetCoreIdentityNativeRouteContract(
    string DocumentKind,
    string QueryIdentity,
    string Field,
    int Limit,
    string PhysicalName,
    string IndexLogicalName);
