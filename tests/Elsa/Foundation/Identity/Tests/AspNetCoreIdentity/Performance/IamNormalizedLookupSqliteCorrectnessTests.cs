using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Xunit;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity.Performance;

/// <summary>
/// Executes the shared, timing-free IAM scenario against the Groundwork SQLite-backed store implementation.
/// Native-plan evidence remains a separate prerequisite for performance measurement and verdicts.
/// </summary>
public sealed class IamNormalizedLookupSqliteCorrectnessTests
{
    private const int BoundedCursorPageSize = 100;
    private const int NativePlanAcceptanceCardinality = 100_000;
    private const int SeedBatchSize = 500;

    [Fact]
    [Trait("Category", "Sqlite")]
    public async Task Groundwork_store_contracts_produce_the_ratified_digest()
    {
        using var persistence = new IdentityV2TestPersistence();
        var access = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope(IamNormalizedLookupWorkload.TenantId)));
        var adapter = new GroundworkIdentityWorkloadAdapter(
            new GroundworkIdentityUserStore(persistence.Rows(access), access),
            new GroundworkIdentityRoleStore(persistence.Rows(access), access));

        AssertRatified(await new IamNormalizedLookupWorkload().ExecuteAsync(adapter));
    }

    [Fact]
    [Trait("Category", "Sqlite")]
    [Trait("Category", "NativePlan")]
    public async Task Public_iam_routes_use_declared_indexes_at_acceptance_scale()
    {
        var previousFlag = Environment.GetEnvironmentVariable("GW_EXPLAIN_ASSERT");
        var previousDirectory = Environment.GetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR");
        var artifactDirectory = Path.Combine(
            string.IsNullOrWhiteSpace(previousDirectory) ? Path.GetTempPath() : previousDirectory,
            $"elsa-iam-native-plan-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", null);
        Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", artifactDirectory);
        try
        {
            using var persistence = new IdentityV2TestPersistence();
            var recording = new RecordingSessionSource(persistence);
            var access = new FixedAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope(IamNormalizedLookupWorkload.TenantId)));
            var rows = new GroundworkIdentityRowStore(recording, access);
            var users = new GroundworkIdentityUserStore(rows, access);
            var roles = new GroundworkIdentityRoleStore(rows, access);
            var domainRoles = new GroundworkRoleStore(rows, access);
            var candidate = IamNormalizedLookupWorkload.CreateUser(
                IamNormalizedLookupWorkload.UserId,
                IamNormalizedLookupWorkload.UserName,
                IamNormalizedLookupWorkload.NormalizedUserName,
                IamNormalizedLookupWorkload.Email,
                IamNormalizedLookupWorkload.NormalizedEmail);
            IamNormalizedLookupWorkload.AssertSucceeded(
                await users.CreateAsync(candidate, CancellationToken.None),
                "create-native-plan-candidate");
            var role = IamNormalizedLookupWorkload.CreateRole(
                IamNormalizedLookupWorkload.RoleId,
                IamNormalizedLookupWorkload.RoleName,
                IamNormalizedLookupWorkload.NormalizedRoleName);
            IamNormalizedLookupWorkload.AssertSucceeded(
                await roles.CreateAsync(role, CancellationToken.None),
                "create-native-plan-role");
            var userClaim = new Claim("permission", "identity.users.read");
            var roleClaim = new Claim("permission", "identity.users.manage");
            var login = new UserLoginInfo("oidc", "native-plan-subject", "OIDC");
            await users.AddClaimsAsync(candidate, [userClaim], CancellationToken.None);
            await roles.AddClaimAsync(role, roleClaim, CancellationToken.None);
            await users.AddLoginAsync(candidate, login, CancellationToken.None);
            await users.AddToRoleAsync(candidate, role.NormalizedName!, CancellationToken.None);

            var claimMappings = new GroundworkClaimMappingStore(rows, access);
            await claimMappings.SaveAsync(new ClaimMappingRule(
                "native-plan-claim-mapping",
                IamNormalizedLookupWorkload.TenantId,
                "oidc",
                "groups",
                "operators",
                new HashSet<string>(["operators"], StringComparer.Ordinal),
                new HashSet<string>(["identity.users.read"], StringComparer.Ordinal),
                1,
                true));
            var expiry = DateTimeOffset.UtcNow.AddMinutes(-1);
            Assert.True(rows.Save(new GroundworkIdentityRowWrite(
                IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                "native-plan-expired-receipt",
                "{}",
                new Dictionary<string, object?>
                {
                    [IdentityStorageManifest.MutationReceiptExpiresAtField] = expiry
                },
                GroundworkIdentityRowWriteCondition.CreateOnly)).Succeeded);

            SeedNoise(persistence, IdentityStorageManifest.IdentityUserDocumentKind, SeedUserValues);
            SeedNoise(persistence, IdentityStorageManifest.IdentityRoleDocumentKind, SeedRoleValues);
            SeedNoise(persistence, IdentityStorageManifest.UserClaimDocumentKind, SeedUserClaimValues);
            SeedNoise(persistence, IdentityStorageManifest.RoleClaimDocumentKind, SeedRoleClaimValues);
            SeedNoise(persistence, IdentityStorageManifest.UserRoleDocumentKind, SeedUserRoleValues);
            SeedNoise(persistence, IdentityStorageManifest.ExternalLoginDocumentKind, SeedExternalLoginValues);

            recording.Queries.Clear();
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", "1");
            var byName = await users.FindByNameAsync(
                IamNormalizedLookupWorkload.NormalizedUserName,
                CancellationToken.None);
            var byEmail = await users.FindByEmailAsync(
                IamNormalizedLookupWorkload.NormalizedEmail,
                CancellationToken.None);
            var roleByName = await roles.FindByNameAsync(
                IamNormalizedLookupWorkload.NormalizedRoleName,
                CancellationToken.None);
            var tenantRoles = await domainRoles.ListAsync(
                IamNormalizedLookupWorkload.TenantId,
                CancellationToken.None);
            var userClaims = await users.GetClaimsAsync(candidate, CancellationToken.None);
            var claimUsers = await users.GetUsersForClaimAsync(userClaim, CancellationToken.None);
            var roleClaims = await roles.GetClaimsAsync(role, CancellationToken.None);
            var userRoles = await users.GetRolesAsync(candidate, CancellationToken.None);
            var roleUsers = await users.GetUsersInRoleAsync(role.NormalizedName!, CancellationToken.None);
            var logins = await users.GetLoginsAsync(candidate, CancellationToken.None);
            var claimMappingRows = await claimMappings.ListForProviderAsync(
                IamNormalizedLookupWorkload.TenantId,
                "oidc",
                CancellationToken.None);
            var expiredReceiptRows = rows.Query(
                IdentityStorageManifest.IdentityMutationReceiptDocumentKind,
                new GroundworkIdentityRowQuery(
                    IdentityStorageManifest.MutationReceiptExpiresAtField,
                    GroundworkIdentityRowComparison.LessThanOrEqual,
                    expiry.AddMinutes(1),
                    IdentityStorageManifest.MutationReceiptExpiresAtField,
                    Take: 64,
                    ExpectedIndex: IdentityV2StorageManifest.MutationReceiptByExpiryIndex));

            Assert.Equal(candidate.Id, byName?.Id);
            Assert.Equal(candidate.Id, byEmail?.Id);
            Assert.Equal(role.Id, roleByName?.Id);
            Assert.Equal([role.Id], tenantRoles.Select(value => value.Id));
            var observedUserClaim = Assert.Single(userClaims);
            Assert.Equal((userClaim.Type, userClaim.Value), (observedUserClaim.Type, observedUserClaim.Value));
            Assert.Equal([candidate.Id], claimUsers.Select(user => user.Id));
            var observedRoleClaim = Assert.Single(roleClaims);
            Assert.Equal((roleClaim.Type, roleClaim.Value), (observedRoleClaim.Type, observedRoleClaim.Value));
            Assert.Equal([role.Name!], userRoles);
            Assert.Equal([candidate.Id], roleUsers.Select(user => user.Id));
            Assert.Equal([login.ProviderKey], logins.Select(value => value.ProviderKey));
            Assert.Single(claimMappingRows);
            Assert.Single(expiredReceiptRows);
            AssertRouteEvidence(recording.Queries);
            AssertPlanArtifacts(artifactDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", previousFlag);
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", previousDirectory);
            if (string.IsNullOrWhiteSpace(previousDirectory) && Directory.Exists(artifactDirectory))
                Directory.Delete(artifactDirectory, recursive: true);
        }
    }

    private static void AssertRatified(IamNormalizedLookupResult result)
    {
        Assert.Equal(IamNormalizedLookupWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(IamNormalizedLookupWorkload.ExpectedResultDigest, result.ResultDigest);
    }

    private static void SeedNoise(
        IdentityV2TestPersistence persistence,
        string unitId,
        Func<int, IReadOnlyDictionary<string, object?>> values)
    {
        var unit = persistence.Unit(unitId);
        var session = persistence.Open(
            unitId,
            StorageAccess.Scoped(new StorageScope(IamNormalizedLookupWorkload.TenantId)));
        var batched = Assert.IsAssignableFrom<IBatchedStorageSession>(session);
        var noiseCount = NativePlanAcceptanceCardinality - 1;
        for (var offset = 0; offset < noiseCount; offset += SeedBatchSize)
        {
            var upper = Math.Min(offset + SeedBatchSize, noiseCount);
            var writes = new List<RowWrite>(upper - offset);
            for (var index = offset; index < upper; index++)
                writes.Add(RowWrite.Insert(unit, new StorageValues(values(index))));

            var outcomes = batched.ApplyBatch(writes);
            Assert.Equal(writes.Count, outcomes.Count);
            Assert.All(outcomes, outcome => Assert.True(outcome.Outcome.Succeeded));
        }
    }

    private static IReadOnlyDictionary<string, object?> SeedUserValues(int index)
    {
        var suffix = index.ToString("D6");
        return BaseValues($"native-plan-user-{suffix}")
            .Append(KeyValuePair.Create<string, object?>(
                IdentityStorageManifest.NormalizedUserNameKeyField,
                IdentityDocumentId.From(IamNormalizedLookupWorkload.TenantId, $"NATIVE-PLAN-USER-{suffix}")))
            .Append(KeyValuePair.Create<string, object?>(
                IdentityStorageManifest.NormalizedEmailKeyField,
                IdentityDocumentId.From(IamNormalizedLookupWorkload.TenantId, $"NATIVE-PLAN-USER-{suffix}@EXAMPLE.TEST")))
            .ToDictionary(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, object?> SeedRoleValues(int index)
    {
        var suffix = index.ToString("D6");
        return BaseValues($"native-plan-role-{suffix}")
            .Append(KeyValuePair.Create<string, object?>(
                IdentityStorageManifest.NormalizedRoleNameKeyField,
                IdentityDocumentId.From(IamNormalizedLookupWorkload.TenantId, $"NATIVE-PLAN-ROLE-{suffix}")))
            .Append(KeyValuePair.Create<string, object?>(
                IdentityStorageManifest.TenantIdField,
                IdentityCompositeDocumentId.Normalize($"native-plan-tenant-{suffix}")))
            .ToDictionary(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, object?> SeedUserClaimValues(int index)
    {
        var suffix = index.ToString("D6");
        return BaseValues($"native-plan-user-claim-{suffix}")
            .Append(KeyValuePair.Create<string, object?>(
                IdentityStorageManifest.UserLookupKeyField,
                IdentityDocumentId.From(IamNormalizedLookupWorkload.TenantId, $"native-plan-user-{suffix}")))
            .Append(KeyValuePair.Create<string, object?>(
                IdentityStorageManifest.ClaimKeyField,
                IdentityDocumentId.From(IamNormalizedLookupWorkload.TenantId, "permission", $"noise-{suffix}")))
            .ToDictionary(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, object?> SeedRoleClaimValues(int index)
    {
        var suffix = index.ToString("D6");
        return BaseValues($"native-plan-role-claim-{suffix}")
            .Append(KeyValuePair.Create<string, object?>(
                IdentityStorageManifest.RoleLookupKeyField,
                IdentityDocumentId.From(IamNormalizedLookupWorkload.TenantId, $"native-plan-role-{suffix}")))
            .ToDictionary(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, object?> SeedUserRoleValues(int index)
    {
        var suffix = index.ToString("D6");
        return BaseValues($"native-plan-membership-{suffix}")
            .Append(KeyValuePair.Create<string, object?>(
                IdentityStorageManifest.UserLookupKeyField,
                IdentityDocumentId.From(IamNormalizedLookupWorkload.TenantId, $"native-plan-user-{suffix}")))
            .Append(KeyValuePair.Create<string, object?>(
                IdentityStorageManifest.RoleLookupKeyField,
                IdentityDocumentId.From(IamNormalizedLookupWorkload.TenantId, $"native-plan-role-{suffix}")))
            .ToDictionary(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, object?> SeedExternalLoginValues(int index)
    {
        var suffix = index.ToString("D6");
        return BaseValues($"native-plan-login-{suffix}")
            .Append(KeyValuePair.Create<string, object?>(
                IdentityStorageManifest.UserLookupKeyField,
                IdentityDocumentId.From(IamNormalizedLookupWorkload.TenantId, $"native-plan-user-{suffix}")))
            .ToDictionary(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, object?> BaseValues(string id) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [IdentityV2StorageManifest.IdField] = id,
            [IdentityV2StorageManifest.SchemaVersionField] = IdentityStorageManifest.SchemaVersion,
            [IdentityV2StorageManifest.ContentField] = "{}"
        };

    private static void AssertRouteEvidence(IReadOnlyList<QueryObservation> observations)
    {
        var expected = new Dictionary<string, (int Count, int Limit, bool Range)>(StringComparer.Ordinal)
        {
            [IdentityV2StorageManifest.UserByNormalizedNameIndex] = (1, 1, false),
            [IdentityV2StorageManifest.UserByNormalizedEmailIndex] = (1, 2, false),
            [IdentityV2StorageManifest.RoleByNormalizedNameIndex] = (2, 1, false),
            [IdentityV2StorageManifest.RoleByTenantIndex] = (1, BoundedCursorPageSize, false),
            [IdentityV2StorageManifest.UserClaimByUserIndex] = (1, BoundedCursorPageSize, false),
            [IdentityV2StorageManifest.UserClaimByClaimIndex] = (1, BoundedCursorPageSize, false),
            [IdentityV2StorageManifest.RoleClaimByRoleIndex] = (1, BoundedCursorPageSize, false),
            [IdentityV2StorageManifest.UserRoleByUserIndex] = (1, BoundedCursorPageSize, false),
            [IdentityV2StorageManifest.UserRoleByRoleIndex] = (1, BoundedCursorPageSize, false),
            [IdentityV2StorageManifest.LoginByUserIndex] = (1, BoundedCursorPageSize, false),
            [IdentityV2StorageManifest.ClaimMappingByProviderIndex] = (1, BoundedCursorPageSize, false),
            [IdentityV2StorageManifest.MutationReceiptByExpiryIndex] = (1, 64, true)
        };
        Assert.Equal(expected.Values.Sum(item => item.Count), observations.Count);
        foreach (var (indexName, requirement) in expected)
        {
            var routes = observations.Where(observation => observation.Options.SelectedIndex == indexName).ToArray();
            Assert.Equal(requirement.Count, routes.Length);
            Assert.All(routes, route =>
            {
                Assert.Equal(route.Unit.Name, route.Request.Table.Value);
                Assert.Equal(ScopePolicy.Scoped, route.Unit.Scope);
                Assert.Equal(StorageAccessKind.Scoped, route.Access.Kind);
                Assert.Equal(IamNormalizedLookupWorkload.TenantId, route.Access.Scope?.Value);
                Assert.Equal(requirement.Limit, route.Request.Paging.Limit);
                Assert.Equal(IdentityV2StorageManifest.IdField, route.Request.Order[^1].Column.Name);
                Assert.All(route.Request.Order, order =>
                {
                    Assert.Equal(OrderDirection.Ascending, order.Direction);
                    Assert.Equal(NullOrder.Last, order.NullOrder);
                });
                var predicateColumn = requirement.Range
                    ? Assert.IsType<Predicate.Range>(route.Request.Where).Column.Name
                    : Assert.IsType<Predicate.Equal>(route.Request.Where).Column.Name;
                var declaration = Assert.Single(route.Options.Indexes, declaration => declaration.Name == indexName);
                Assert.Equal(predicateColumn, declaration.Columns[0]);
                Assert.Equal(QueryIndexPinning.ProviderDefault, declaration.Pinning);
                Assert.Equal(indexName, route.Result.SelectedIndex);
                Assert.False(route.Result.IndexHintApplied);
                Assert.Single(route.Result.Rows);
            });
        }
    }

    private static void AssertPlanArtifacts(string artifactDirectory)
    {
        var artifacts = Directory.GetFiles(artifactDirectory, "*.txt");
        Assert.Equal(13, artifacts.Length);
        Assert.All(artifacts, artifact =>
        {
            Assert.Contains("optimizer-selected", Path.GetFileName(artifact), StringComparison.Ordinal);
            Assert.Contains("USING", File.ReadAllText(artifact), StringComparison.OrdinalIgnoreCase);
        });
    }

    private sealed class RecordingSessionSource(IdentityV2TestPersistence inner)
        : IGroundworkStorageSessionSource
    {
        public List<QueryObservation> Queries { get; } = [];

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            new RecordingSession(inner.Open(unitId, access, targetName), Queries);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            inner.BeginUnitOfWork(access, options, unitIds, targetName);

        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);
    }

    private sealed class RecordingSession(
        IStorageSession inner,
        ICollection<QueryObservation> observations) : SynchronousStorageSessionTestDouble, IStorageSession, IConcurrencyStorageSession
    {
        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;
        public StoredEntry? Read(StorageKey key) => inner.Read(key);

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            var supplied = options ?? QueryRenderOptions.Default;
            var result = inner.Query(request, supplied);
            observations.Add(new QueryObservation(Unit, Access, request, supplied, result));
            return result;
        }

        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null) =>
            Assert.IsAssignableFrom<IConcurrencyStorageSession>(inner).ConditionalUpsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) =>
            inner.Append(operationId, values);
    }

    private sealed record QueryObservation(
        StorageUnit Unit,
        StorageAccess Access,
        QueryRequest Request,
        QueryRenderOptions Options,
        QueryMaterializedResult Result);

    private sealed class GroundworkIdentityWorkloadAdapter(
        GroundworkIdentityUserStore users,
        GroundworkIdentityRoleStore roles) : IIamIdentityWorkloadAdapter
    {
        public Task<IdentityResult> CreateUserAsync(AspNetCoreIdentityUser user, CancellationToken token) =>
            users.CreateAsync(user, token);

        public Task<IdentityResult> CreateRoleAsync(IdentityRole role, CancellationToken token) =>
            roles.CreateAsync(role, token);

        public Task AddToRoleAsync(AspNetCoreIdentityUser user, string role, CancellationToken token) =>
            users.AddToRoleAsync(user, role, token);

        public Task<AspNetCoreIdentityUser?> FindUserByNormalizedNameAsync(string value, CancellationToken token) =>
            users.FindByNameAsync(value, token);

        public Task<AspNetCoreIdentityUser?> FindUserByNormalizedEmailAsync(string value, CancellationToken token) =>
            users.FindByEmailAsync(value, token);

        public Task<IdentityRole?> FindRoleByNormalizedNameAsync(string value, CancellationToken token) =>
            roles.FindByNameAsync(value, token);

        public Task<IList<string>> GetRolesAsync(AspNetCoreIdentityUser user, CancellationToken token) =>
            users.GetRolesAsync(user, token);

        public Task<IList<AspNetCoreIdentityUser>> GetUsersInRoleAsync(string value, CancellationToken token) =>
            users.GetUsersInRoleAsync(value, token);

        public Task<AspNetCoreIdentityUser?> FindUserByIdAsync(string value, CancellationToken token) =>
            users.FindByIdAsync(value, token);

        public Task<IdentityResult> UpdateUserAsync(AspNetCoreIdentityUser user, CancellationToken token) =>
            users.UpdateAsync(user, token);
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
