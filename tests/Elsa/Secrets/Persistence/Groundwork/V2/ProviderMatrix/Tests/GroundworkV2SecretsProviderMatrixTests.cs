using Elsa.Persistence.Groundwork.Composition;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Secrets.Persistence.Groundwork.V2.ProviderMatrix.Tests;

/// <summary>
/// Runs the public Groundwork v2 Secrets repository contract through every native provider.
/// SQLite is always exercised; external providers use a local connection string when supplied
/// and otherwise are provisioned by the Docker-backed integration gate.
/// </summary>
public sealed class GroundworkV2SecretsProviderMatrixTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Secrets_preserve_public_crud_revision_query_tenant_and_restart_contract(string providerName)
    {
        var configuredConnection = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            providerName != "sqlite" && string.IsNullOrWhiteSpace(configuredConnection) && !IsContinuousIntegration(),
            $"Set {EnvironmentVariable(providerName)} locally, or run the provider matrix in CI.");

        await using var runtime = await ProviderRuntime.CreateAsync(providerName, configuredConnection);
        var suffix = Guid.NewGuid().ToString("N");
        var tenantA = $"tenant-a-{suffix}";
        var tenantB = $"tenant-b-{suffix}";
        var listTenant = $"list-{suffix}";
        var unit = SecretsGroundworkStorageSchema.CreateUnit();

        using (var connection = runtime.OpenConnection())
        {
            connection.Schema.Apply(unit);
            var repository = CreateRepository(connection, unit);
            await ExerciseCrudRevisionAndTenantIsolationAsync(repository, tenantA, tenantB);
            await ExerciseBoundedSearchCountAndPagingAsync(repository, listTenant);
        }

        // A new provider connection is required here: all subsequent assertions prove durable
        // storage rather than an in-memory/session cache.
        using var reopenedConnection = runtime.OpenConnection();
        var reopened = CreateRepository(reopenedConnection, unit);

        var persisted = await reopened.FindAsync(tenantA, "payments.api");
        Assert.NotNull(persisted);
        Assert.Equal("updated", persisted!.DisplayName);
        Assert.Equal(SecretStatus.Deleted, persisted.Status);
        Assert.Equal(
            "beta",
            (await reopened.FindAsync(tenantB, "payments.api"))!.LatestActiveVersion!.Payload.Value);

        var persistedPage = await reopened.ListPageAsync(
            listTenant,
            new SecretRepositoryListRequest(skip: 1, take: 2));
        Assert.Equal(5, persistedPage.TotalCount);
        Assert.Equal(["payments.alpha", "payments.configuration"], persistedPage.Items.Select(secret => secret.Name));
    }

    private static async Task ExerciseCrudRevisionAndTenantIsolationAsync(
        ISecretRepository repository,
        string tenantA,
        string tenantB)
    {
        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        var alpha = Secret(tenantA, "payments.api", "alpha");
        var beta = Secret(tenantB, "payments.api", "beta");

        Assert.True(await repository.TryAddAsync(alpha));
        Assert.False(await repository.TryAddAsync(Secret(tenantA, "payments.api", "duplicate")));
        Assert.True(await repository.TryAddAsync(beta));
        Assert.Equal("alpha", (await repository.FindAsync(tenantA, alpha.Name))!.LatestActiveVersion!.Payload.Value);
        Assert.Equal("beta", (await repository.FindAsync(tenantB, beta.Name))!.LatestActiveVersion!.Payload.Value);
        Assert.Null(await repository.FindAsync(tenantA, "payments.api-missing"));

        var current = await revisions.FindWithRevisionAsync(tenantA, alpha.Name);
        Assert.NotNull(current);
        current!.Secret.DisplayName = "updated";
        var updated = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Saved, updated.Status);
        Assert.NotEqual(current.Revision, updated.Revision);

        var stale = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Conflict, stale.Status);
        var missing = await revisions.SaveWithRevisionAsync(
            Secret(tenantA, "payments.api-missing", "value"),
            "gw:00000000000000000001");
        Assert.Equal(SecretRevisionSaveStatus.NotFound, missing.Status);

        // Secrets has a soft-delete contract: the row remains addressable but cannot be created
        // again under the same tenant/name identity.
        current.Secret.Status = SecretStatus.Deleted;
        await repository.SaveAsync(current.Secret);
        Assert.Equal(SecretStatus.Deleted, (await repository.FindAsync(tenantA, alpha.Name))!.Status);
        Assert.False(await repository.TryAddAsync(Secret(tenantA, alpha.Name, "reserved")));
    }

    private static async Task ExerciseBoundedSearchCountAndPagingAsync(
        ISecretRepository repository,
        string tenantId)
    {
        await repository.SaveAsync(Secret(tenantId, "payments.alpha", "a", "Payments Alpha", scope: "Finance"));
        await repository.SaveAsync(Secret(
            tenantId,
            "payments.configuration",
            "b",
            "Payments Config",
            SecretStoreNames.Configuration,
            "Finance"));
        await repository.SaveAsync(Secret(tenantId, "payments.other", "c", "Payments Other", scope: "Operations"));
        await repository.SaveAsync(Secret(tenantId, "orders.alpha", "d", "Orders Alpha", scope: "Finance"));
        var deleted = Secret(tenantId, "payments.deleted", "e", "Payments Deleted", scope: "Finance");
        deleted.Status = SecretStatus.Deleted;
        await repository.SaveAsync(deleted);

        var filtered = await repository.ListPageAsync(
            tenantId,
            new SecretRepositoryListRequest(
                search: "PAYMENTS",
                typeName: SecretTypeNames.Text,
                typeNames: [SecretTypeNames.Text, SecretTypeNames.RsaKey],
                storeName: SecretStoreNames.Encrypted,
                storeNames: [SecretStoreNames.Encrypted],
                scope: "FINANCE",
                status: SecretStatus.Active,
                excludedStatus: SecretStatus.Deleted,
                take: 2));
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal("payments.alpha", Assert.Single(filtered.Items).Name);

        var page = await repository.ListPageAsync(
            tenantId,
            new SecretRepositoryListRequest(skip: 1, take: 2));
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(["payments.alpha", "payments.configuration"], page.Items.Select(secret => secret.Name));
    }

    private static GroundworkSecretRepository CreateRepository(
        IStorageProviderConnection connection,
        StorageUnit unit) => new(new DirectSessionSource(connection, unit));

    private static Secret Secret(
        string tenantId,
        string name,
        string value,
        string? displayName = null,
        string storeName = SecretStoreNames.Encrypted,
        string? scope = null) => new()
        {
            TenantId = tenantId,
            Name = name,
            DisplayName = displayName ?? name,
            TypeName = SecretTypeNames.Text,
            StoreName = storeName,
            Scope = scope,
            Versions = [new SecretVersion
            {
                Version = 1,
                Status = SecretStatus.Active,
                ExpiresAt = null,
                Payload = SecretPayload.FromValue(value)
            }]
        };

    private static IStorageProviderConnection CreateConnection(string providerName, string connectionString) =>
        providerName switch
        {
            "sqlite" => new SqliteProviderFactory().Create(connectionString),
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private static bool IsContinuousIntegration() =>
        Environment.GetEnvironmentVariable("CI") is "1" or "true";

    private sealed class DirectSessionSource(
        IStorageProviderConnection connection,
        StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return connection.OpenSession(unit, access);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => connection.BeginUnitOfWork(access, options, unit);

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return unit;
        }
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

            if (providerName == "sqlite")
                return CreateSqliteRuntime();
            if (!IsContinuousIntegration())
                throw new InvalidOperationException("Native provider containers are enabled only in CI.");

            return providerName switch
            {
                "postgresql" => await CreatePostgreSqlRuntimeAsync(),
                "sqlserver" => await CreateSqlServerRuntimeAsync(),
                "mongodb" => await CreateMongoRuntimeAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
            };
        }

        public IStorageProviderConnection OpenConnection() => CreateConnection(providerName, connectionString);

        public async ValueTask DisposeAsync()
        {
            if (container is not null)
                await container.DisposeAsync();
            if (sqlitePath is not null)
            {
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                    if (File.Exists(path))
                        File.Delete(path);
            }
        }

        private static ProviderRuntime CreateSqliteRuntime()
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-secrets-v2-matrix-{Guid.NewGuid():N}.db");
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
            // Mongo transactions require a replica set; the Testcontainers fixture supplies one
            // so conditional writes and schema setup exercise the same transactional capability
            // as the supported deployment shape.
            var container = new MongoDbBuilder("mongo:7.0.37")
                .WithReplicaSet("rs0")
                .Build();
            await container.StartAsync();
            var connectionString = container.GetConnectionString();
            var queryStart = connectionString.IndexOf('?', StringComparison.Ordinal);
            var server = (queryStart < 0 ? connectionString : connectionString[..queryStart]).TrimEnd('/');
            return new("mongodb", $"{server}/elsa?replicaSet=rs0&authSource=admin&directConnection=true", container, null);
        }
    }
}
