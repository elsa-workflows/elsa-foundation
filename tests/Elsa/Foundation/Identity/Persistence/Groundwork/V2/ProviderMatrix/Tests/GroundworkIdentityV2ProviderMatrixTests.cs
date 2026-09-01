using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using System.Collections.Concurrent;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit.Sdk;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.V2.ProviderMatrix.Tests;

/// <summary>
/// Provider acceptance for the public Groundwork v2 IAM stores. The test intentionally constructs
/// the store adapters directly over one provider connection, applies the fresh v2 manifest, and
/// then reopens a new connection before the durability assertions.
/// </summary>
public sealed class GroundworkIdentityV2ProviderMatrixTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Public_v2_iam_stores_preserve_crud_revision_lookup_isolation_and_restart_contract(string providerName)
    {
        var configuredConnection = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            providerName != "sqlite" && string.IsNullOrWhiteSpace(configuredConnection) && !IsContinuousIntegration(),
            $"Set {EnvironmentVariable(providerName)} locally, or run the matrix in CI.");

        await using var runtime = await ProviderRuntime.CreateAsync(providerName, configuredConnection);
        var suffix = Guid.NewGuid().ToString("N");
        var tenantA = $"iam-a-{suffix}";
        var tenantB = $"iam-b-{suffix}";
        var provider = "oidc";

        using (var persistence = new IdentityPersistence(runtime.OpenConnection()))
        {
            persistence.ApplySchema();
            await ExerciseAllStoresAsync(persistence, tenantA, tenantB, provider);
        }

        // A new connection proves that the observed rows are durable provider state, not adapter
        // caches or a session-local identity map.
        using var reopened = new IdentityPersistence(runtime.OpenConnection());
        reopened.ApplySchema();
        await AssertAllStoresSurviveReopenAsync(reopened, tenantA, tenantB, provider);
    }

    [Fact]
    public void Matrix_uses_only_the_public_v2_groundwork_surface()
    {
        var source = typeof(GroundworkUserStore).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(source, reference => reference.Name is "Groundwork.Core" or "Groundwork.Documents");
        Assert.Contains(source, reference => reference.Name == "Groundwork.Kernel");
        Assert.Contains(source, reference => reference.Name == "Groundwork.Query.Model");
        Assert.Contains(source, reference => reference.Name == "Groundwork.Store");
    }

    private static async Task ExerciseAllStoresAsync(
        IdentityPersistence persistence,
        string tenantA,
        string tenantB,
        string provider)
    {
        var userA = User(tenantA, "user-shared", "alice", "alice@example.test");
        var userB = User(tenantB, "user-shared", "bob", "bob@example.test");
        var roleA = Role(tenantA, "role-shared", "Operators");
        var roleB = Role(tenantB, "role-shared", "Auditors");
        var roleASecond = Role(tenantA, "role-second", "Operators Secondary");
        var appA = Application(tenantA, "app-shared", "client-shared");
        var appB = Application(tenantB, "app-shared", "client-shared");
        var credentialA = Credential(tenantA, "credential-shared", appA.Id);
        var credentialB = Credential(tenantB, "credential-shared", appB.Id);
        var claimA = ClaimMapping(tenantA, provider, "claim-shared", 20);
        var claimB = ClaimMapping(tenantB, provider, "claim-shared", 10);
        // Keep row-id order opposite to logical rule order so page boundaries prove native ordering.
        var claimASecond = ClaimMapping(tenantA, provider, "z-early", 10);
        var providerA = ProviderConfiguration(tenantA, provider, "tenant-a");
        var providerB = ProviderConfiguration(tenantB, provider, "tenant-b");
        var externalA = ExternalIdentity(tenantA, userA.Id, "subject-shared");
        var externalB = ExternalIdentity(tenantB, userB.Id, "subject-shared");
        var externalASecond = ExternalIdentity(tenantA, userA.Id, "subject-second");
        var membershipA = Membership(tenantA, userA.Id, roleA.Id);
        var membershipB = Membership(tenantB, userB.Id, roleB.Id);

        // Seed user and role authority first because relationship stores validate their owner rows.
        await persistence.Users(tenantA).SaveAsync(userA);
        await persistence.Users(tenantB).SaveAsync(userB);
        await persistence.Roles(tenantA).SaveAsync(roleA);
        await persistence.Roles(tenantB).SaveAsync(roleB);
        await persistence.Applications(tenantA).SaveAsync(appA);
        await persistence.Credentials(tenantA).SaveAsync(credentialA);
        await persistence.ClaimMappings(tenantA).SaveAsync(claimA);
        await persistence.ProviderConfigurations(tenantA).SaveAsync(providerA);
        await persistence.ExternalIdentities(tenantA).SaveAsync(externalA);
        await persistence.Memberships(tenantA).SaveAsync(membershipA);

        await AssertRevisionContractAsync(
            persistence.Users(tenantA),
            userA,
            find: () => persistence.Users(tenantA).FindWithRevisionAsync(tenantA, userA.Id),
            update: current => current with { DisplayName = "Alice Updated" },
            save: (record, revision) => persistence.Users(tenantA).SaveWithRevisionAsync(record, revision),
            missing: User(tenantA, "user-missing", "missing", null));
        await AssertRevisionContractAsync(
            persistence.Roles(tenantA),
            roleA,
            find: () => persistence.Roles(tenantA).FindWithRevisionAsync(tenantA, roleA.Id),
            update: current => current with { Description = "Operators Updated" },
            save: (record, revision) => persistence.Roles(tenantA).SaveWithRevisionAsync(record, revision),
            missing: Role(tenantA, "role-missing", "Missing"));

        await AssertRevisionContractAsync(
            persistence.Applications(tenantA),
            appA,
            find: () => persistence.Applications(tenantA).FindWithRevisionAsync(tenantA, appA.Id),
            update: current => current with { DisplayName = "Client Updated" },
            save: (record, revision) => persistence.Applications(tenantA).SaveWithRevisionAsync(record, revision),
            missing: Application(tenantA, "app-missing", "client-missing"));
        await AssertRevisionContractAsync(
            persistence.Credentials(tenantA),
            credentialA,
            find: () => persistence.Credentials(tenantA).FindWithRevisionAsync(tenantA, credentialA.Id),
            update: current => current with { Status = CredentialStatus.Revoked },
            save: (record, revision) => persistence.Credentials(tenantA).SaveWithRevisionAsync(record, revision),
            missing: Credential(tenantA, "credential-missing", appA.Id));
        await AssertRevisionContractAsync(
            persistence.ClaimMappings(tenantA),
            claimA,
            find: () => persistence.ClaimMappings(tenantA).FindWithRevisionAsync(tenantA, provider, claimA.Id),
            update: current => current with { Order = 30 },
            save: (record, revision) => persistence.ClaimMappings(tenantA).SaveWithRevisionAsync(record, revision),
            missing: ClaimMapping(tenantA, provider, "claim-missing", 40));
        await AssertRevisionContractAsync(
            persistence.ProviderConfigurations(tenantA),
            providerA,
            find: () => persistence.ProviderConfigurations(tenantA).FindForTenantWithRevisionAsync(tenantA, provider),
            update: current => current with { Kind = "tenant-a-updated" },
            save: (record, revision) => persistence.ProviderConfigurations(tenantA).SaveWithRevisionAsync(record, revision),
            missing: ProviderConfiguration(tenantA, "missing-provider", "missing"));
        await AssertRevisionContractAsync(
            persistence.ExternalIdentities(tenantA),
            externalA,
            find: () => persistence.ExternalIdentities(tenantA).FindBySubjectWithRevisionAsync(tenantA, provider, externalA.ProviderSubject),
            update: current => current with { LastSeenAt = DateTimeOffset.UnixEpoch.AddDays(2) },
            save: (record, revision) => persistence.ExternalIdentities(tenantA).SaveWithRevisionAsync(record, revision),
            missing: ExternalIdentity(tenantA, userA.Id, "subject-missing"));
        await AssertRevisionContractAsync(
            persistence.Memberships(tenantA),
            membershipA,
            find: () => persistence.Memberships(tenantA).FindWithRevisionAsync(tenantA, userA.Id),
            update: current => current with { Status = TenantMembershipStatus.Suspended },
            save: (record, revision) => persistence.Memberships(tenantA).SaveWithRevisionAsync(record, revision),
            missing: Membership(tenantA, "user-missing", roleA.Id));

        await persistence.Applications(tenantB).SaveAsync(appB);
        await persistence.Credentials(tenantB).SaveAsync(credentialB);
        await persistence.ClaimMappings(tenantB).SaveAsync(claimB);
        await persistence.ProviderConfigurations(tenantB).SaveAsync(providerB);
        await persistence.ExternalIdentities(tenantB).SaveAsync(externalB);
        await persistence.Memberships(tenantB).SaveAsync(membershipB);

        Assert.Equal("alice@example.test", (await persistence.Users(tenantA).FindByEmailAsync(tenantA, "ALICE@EXAMPLE.TEST"))!.Email);
        Assert.Null(await persistence.Users(tenantB).FindByEmailAsync(tenantB, "ALICE@EXAMPLE.TEST"));
        Assert.Equal("client-shared", (await persistence.Applications(tenantA).FindAsync(tenantA, appA.Id))!.ClientId);
        Assert.Equal("client-shared", (await persistence.Applications(tenantB).FindAsync(tenantB, appB.Id))!.ClientId);
        Assert.Equal("tenant-a-updated", (await persistence.ProviderConfigurations(tenantA).FindForTenantAsync(tenantA, provider))!.Kind);
        Assert.Equal("tenant-b", (await persistence.ProviderConfigurations(tenantB).FindForTenantAsync(tenantB, provider))!.Kind);
        Assert.Equal("subject-shared", (await persistence.ExternalIdentities(tenantA).FindBySubjectAsync(tenantA, provider, "subject-shared"))!.ProviderSubject);
        Assert.Equal("subject-shared", (await persistence.ExternalIdentities(tenantB).FindBySubjectAsync(tenantB, provider, "subject-shared"))!.ProviderSubject);

        var claimRulesA = await persistence.ClaimMappings(tenantA).ListForProviderAsync(tenantA, provider);
        var claimRulesB = await persistence.ClaimMappings(tenantB).ListForProviderAsync(tenantB, provider);
        Assert.Equal(["claim-shared"], claimRulesA.Select(rule => rule.Id));
        Assert.Equal(["claim-shared"], claimRulesB.Select(rule => rule.Id));
        Assert.Equal(30, Assert.Single(claimRulesA).Order);
        Assert.Equal(10, Assert.Single(claimRulesB).Order);

        var roleListA = await persistence.Roles(tenantA).ListAsync(tenantA);
        var roleListB = await persistence.Roles(tenantB).ListAsync(tenantB);
        Assert.Equal([roleA.Id], roleListA.Select(role => role.Id));
        Assert.Equal([roleB.Id], roleListB.Select(role => role.Id));

        var externalListA = await persistence.ExternalIdentities(tenantA).ListForUserAsync(tenantA, userA.Id);
        var externalListB = await persistence.ExternalIdentities(tenantB).ListForUserAsync(tenantB, userB.Id);
        Assert.Equal(["subject-shared"], externalListA.Select(identity => identity.ProviderSubject));
        Assert.Equal(["subject-shared"], externalListB.Select(identity => identity.ProviderSubject));

        await persistence.Roles(tenantA).SaveAsync(roleASecond);
        await persistence.ClaimMappings(tenantA).SaveAsync(claimASecond);
        await persistence.ExternalIdentities(tenantA).SaveAsync(externalASecond);

        var rolePage = await ((IPagedRoleStore)persistence.Roles(tenantA)).ListPageAsync(
            tenantA,
            new IamPageRequest(skip: 1, take: 1));
        Assert.Equal(2, rolePage.TotalCount);
        Assert.Equal([roleA.Id], rolePage.Items.Select(role => role.Id));

        var claimPage = await ((IPagedClaimMappingStore)persistence.ClaimMappings(tenantA)).ListForProviderPageAsync(
            tenantA,
            provider,
            new IamPageRequest(skip: 1, take: 1));
        Assert.Equal(2, claimPage.TotalCount);
        Assert.Equal([claimA.Id], claimPage.Items.Select(rule => rule.Id));

        var externalPage = await ((IPagedExternalIdentityStore)persistence.ExternalIdentities(tenantA)).ListForUserPageAsync(
            tenantA,
            userA.Id,
            new IamPageRequest(skip: 1, take: 1));
        Assert.Equal(2, externalPage.TotalCount);
        Assert.Equal([externalA.ProviderSubject], externalPage.Items.Select(identity => identity.ProviderSubject));

        var global = persistence.GlobalProviderConfigurations();
        await global.SaveAsync(GlobalProviderConfiguration(provider));
        Assert.Equal("global", (await global.FindGlobalAsync(provider))!.Kind);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await persistence.ProviderConfigurations(tenantA).FindGlobalAsync(provider));
    }

    private static async Task AssertAllStoresSurviveReopenAsync(
        IdentityPersistence persistence,
        string tenantA,
        string tenantB,
        string provider)
    {
        Assert.Equal("Alice Updated", (await persistence.Users(tenantA).FindAsync(tenantA, "user-shared"))!.DisplayName);
        Assert.Equal("bob@example.test", (await persistence.Users(tenantB).FindAsync(tenantB, "user-shared"))!.Email);
        Assert.Equal("Operators Updated", (await persistence.Roles(tenantA).FindAsync(tenantA, "role-shared"))!.Description);
        Assert.Equal("Auditors", (await persistence.Roles(tenantB).FindAsync(tenantB, "role-shared"))!.Name);
        Assert.Equal("Client Updated", (await persistence.Applications(tenantA).FindAsync(tenantA, "app-shared"))!.DisplayName);
        Assert.Equal(CredentialStatus.Revoked, (await persistence.Credentials(tenantA).FindAsync(tenantA, "credential-shared"))!.Status);
        Assert.Equal("tenant-b", (await persistence.ProviderConfigurations(tenantB).FindForTenantAsync(tenantB, provider))!.Kind);
        Assert.Equal("global", (await persistence.GlobalProviderConfigurations().FindGlobalAsync(provider))!.Kind);
        var reopenedClaims = await persistence.ClaimMappings(tenantA).ListForProviderAsync(tenantA, provider);
        Assert.Equal(2, reopenedClaims.Count);
        Assert.Contains(reopenedClaims, rule => rule.Order == 30);
        var reopenedExternal = await persistence.ExternalIdentities(tenantA).ListForUserAsync(tenantA, "user-shared");
        Assert.Equal(2, reopenedExternal.Count);
        Assert.Contains(reopenedExternal, identity => identity.ProviderSubject == "subject-shared");
        Assert.Equal(TenantMembershipStatus.Suspended, (await persistence.Memberships(tenantA).FindAsync(tenantA, "user-shared"))!.Status);
        Assert.Equal(TenantMembershipStatus.Active, (await persistence.Memberships(tenantB).FindAsync(tenantB, "user-shared"))!.Status);
    }

    private static async Task AssertRevisionContractAsync<TRecord, TStore>(
        TStore store,
        TRecord original,
        Func<ValueTask<IamRevisionedRecord<TRecord>?>> find,
        Func<TRecord, TRecord> update,
        Func<TRecord, string?, ValueTask<IamRevisionSaveResult>> save,
        TRecord missing)
        where TStore : notnull
    {
        var created = await save(original, null);
        Assert.Equal(IamRevisionSaveStatus.Conflict, created.Status);

        var current = await find();
        var secondRead = await find();
        Assert.NotNull(current);
        Assert.NotNull(secondRead);
        Assert.Equal(current!.Revision, secondRead!.Revision);

        var updated = await save(update(current.Record), current.Revision);
        // Keep the stale payload distinct from the successful write. Groundwork may legitimately
        // replay an identical mutation fingerprint as an idempotent success; this assertion is
        // specifically about a stale revision carrying a different observed state.
        var stale = await save(secondRead.Record, secondRead.Revision);
        Assert.Equal(IamRevisionSaveStatus.Saved, updated.Status);
        Assert.True(
            stale.Status == IamRevisionSaveStatus.Conflict,
            $"stale={stale.Status}/{stale.Revision}, updated={updated.Status}/{updated.Revision}, current={current.Revision}, second={secondRead.Revision}");
        Assert.NotEqual(current.Revision, updated.Revision);

        var notFound = await save(missing, "gw:00000000000000000001");
        Assert.Equal(IamRevisionSaveStatus.NotFound, notFound.Status);
    }

    private static UserRecord User(string tenantId, string id, string name, string? email) => new(
        id,
        tenantId,
        name,
        email,
        name,
        UserStatus.Active,
        ResourceOwnership.Foundation,
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    private static RoleRecord Role(string tenantId, string id, string name) => new(
        id,
        tenantId,
        name,
        $"{name} role",
        new HashSet<string>(["identity.users.read"], StringComparer.Ordinal),
        false);

    private static ApplicationRecord Application(string tenantId, string id, string clientId) => new(
        id,
        tenantId,
        clientId,
        $"{clientId} application",
        ApplicationType.Confidential,
        ResourceOwnership.Foundation,
        new HashSet<string>(["client_credentials"], StringComparer.Ordinal),
        new HashSet<string>(["identity.users.read"], StringComparer.Ordinal));

    private static CredentialRecord Credential(string tenantId, string id, string subjectId) => new(
        id,
        tenantId,
        CredentialSubjectType.Application,
        subjectId,
        CredentialKind.ClientSecret,
        "sha256:test-hash",
        "SHA-256",
        CredentialStatus.Active,
        DateTimeOffset.UnixEpoch.AddDays(90));

    private static ClaimMappingRule ClaimMapping(string tenantId, string provider, string id, int order) => new(
        id,
        tenantId,
        provider,
        "groups",
        "operators",
        new HashSet<string>(["operators"], StringComparer.Ordinal),
        new HashSet<string>(["identity.users.read"], StringComparer.Ordinal),
        order,
        true);

    private static ProviderConfigurationRecord ProviderConfiguration(string tenantId, string provider, string kind) => new(
        provider,
        tenantId,
        kind,
        true,
        true,
        ProviderCapabilities.ExternalOidcDefault,
        new Dictionary<string, string>(StringComparer.Ordinal) { ["authority"] = $"https://{kind}.example.test" });

    private static ProviderConfigurationRecord GlobalProviderConfiguration(string provider) => new(
        provider,
        null,
        "global",
        true,
        false,
        ProviderCapabilities.ExternalOidcDefault,
        new Dictionary<string, string>(StringComparer.Ordinal) { ["authority"] = "https://global.example.test" });

    private static ExternalIdentityRecord ExternalIdentity(string tenantId, string userId, string subject) => new(
        tenantId,
        "oidc",
        subject,
        userId,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        ExternalIdentityLinkPolicy.Auto);

    private static TenantMembershipRecord Membership(string tenantId, string userId, string roleId) => new(
        tenantId,
        userId,
        TenantMembershipStatus.Active,
        new HashSet<string>([roleId], StringComparer.Ordinal),
        new HashSet<string>(["identity.users.read"], StringComparer.Ordinal));

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private static bool IsContinuousIntegration() =>
        Environment.GetEnvironmentVariable("CI") is "1" or "true";

    private sealed class IdentityPersistence(IStorageProviderConnection connection) : IGroundworkStorageSessionSource, IDisposable
    {
        private readonly IReadOnlyDictionary<string, StorageUnit> units = IdentityV2StorageManifest.CreateUnits()
            .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, IStorageSession> sessions = new(StringComparer.Ordinal);

        public void ApplySchema()
        {
            foreach (var unit in units.Values)
                connection.Schema.Apply(unit);
        }

        public GroundworkIdentityRowStore Rows(IPersistenceAccessContextAccessor access) =>
            new(this, access);

        public GroundworkUserStore Users(string tenantId)
        {
            var access = Accessor(tenantId);
            return new(Rows(access), access);
        }

        public GroundworkRoleStore Roles(string tenantId)
        {
            var access = Accessor(tenantId);
            return new(Rows(access), access);
        }

        public GroundworkApplicationStore Applications(string tenantId)
        {
            var access = Accessor(tenantId);
            return new(Rows(access), access);
        }

        public GroundworkCredentialStore Credentials(string tenantId)
        {
            var access = Accessor(tenantId);
            return new(Rows(access), access);
        }

        public GroundworkClaimMappingStore ClaimMappings(string tenantId)
        {
            var access = Accessor(tenantId);
            return new(Rows(access), access);
        }

        public GroundworkProviderConfigurationStore ProviderConfigurations(string tenantId)
        {
            var access = Accessor(tenantId);
            return new(Rows(access), access);
        }

        public GroundworkProviderConfigurationStore GlobalProviderConfigurations()
        {
            var access = new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedGlobal(
                new PersistenceAccessPurpose("identity-v2-provider-matrix")));
            return new(Rows(access), access);
        }

        public GroundworkExternalIdentityStore ExternalIdentities(string tenantId)
        {
            var access = Accessor(tenantId);
            return new(Rows(access), access);
        }

        public GroundworkTenantMembershipStore Memberships(string tenantId)
        {
            var access = Accessor(tenantId);
            return new(Rows(access), access);
        }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            var scopeKey = access.Scope?.Value ?? "global";
            return sessions.GetOrAdd(
                $"{unitId}|{scopeKey}",
                _ => connection.OpenSession(Unit(unitId, targetName), access));
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(id => Unit(id, targetName)).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

        public void Dispose() => connection.Dispose();

        private static IPersistenceAccessContextAccessor Accessor(string tenantId) =>
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class ProviderRuntime(
        string providerName,
        string connectionString,
        IAsyncDisposable? container,
        string? sqlitePath) : IAsyncDisposable
    {
        public static async Task<ProviderRuntime> CreateAsync(string providerName, string? configuredConnection)
        {
            if (!string.IsNullOrWhiteSpace(configuredConnection))
                return new(providerName, configuredConnection, null, null);
            return providerName switch
            {
                "sqlite" => CreateSqliteRuntime(),
                "postgresql" => await CreatePostgreSqlRuntimeAsync(),
                "sqlserver" => await CreateSqlServerRuntimeAsync(),
                "mongodb" => await CreateMongoRuntimeAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
            };
        }

        public IStorageProviderConnection OpenConnection() => providerName switch
        {
            "sqlite" => new SqliteProviderFactory().Create(connectionString),
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };

        public async ValueTask DisposeAsync()
        {
            if (container is not null)
                await container.DisposeAsync();
            if (sqlitePath is null)
                return;
            foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                if (File.Exists(path))
                    File.Delete(path);
        }

        private static ProviderRuntime CreateSqliteRuntime()
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-identity-v2-matrix-{Guid.NewGuid():N}.db");
            return new("sqlite", $"Data Source={path}", null, path);
        }

        private static async Task<ProviderRuntime> CreatePostgreSqlRuntimeAsync()
        {
            var container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("elsa")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await container.StartAsync();
            return new("postgresql", container.GetConnectionString(), container, null);
        }

        private static async Task<ProviderRuntime> CreateSqlServerRuntimeAsync()
        {
            var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
            await container.StartAsync();
            return new("sqlserver", container.GetConnectionString(), container, null);
        }

        private static async Task<ProviderRuntime> CreateMongoRuntimeAsync()
        {
            var container = new MongoDbBuilder("mongo:7.0.37").WithReplicaSet("rs0").Build();
            await container.StartAsync();
            var connection = container.GetConnectionString();
            var queryStart = connection.IndexOf('?', StringComparison.Ordinal);
            var server = (queryStart < 0 ? connection : connection[..queryStart]).TrimEnd('/');
            return new("mongodb", $"{server}/elsa_identity_v2?replicaSet=rs0&authSource=admin&directConnection=true", container, null);
        }
    }
}
