using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

public sealed class SecretsProjectionContractTests
{
    [Fact]
    public async Task Legacy_row_fails_closed_then_reindex_preserves_revision_and_restores_lookup()
    {
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-projection-{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        try
        {
            var options = new DbContextOptionsBuilder<SecretsSqliteDbContext>()
                .UseSqlite(connection, sqlite => sqlite
                    .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                    .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
                .Options;
            await using var context = new SecretsSqliteDbContext(options);
            await EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName);

            const string legacyRuntimeKey = "\u019B";
            var current = SecretDocument.FromSecret(CreateSecret(legacyRuntimeKey));
            Assert.Equal("\uA7DC", current.TypeNameLookupKey);
            var legacy = current with { TypeNameLookupKey = legacyRuntimeKey };
            var record = legacy.ToRecord();
            context.Secrets.Add(record);
            await context.SaveChangesAsync();
            var originalToken = record.ConcurrencyToken.ToArray();
            context.ChangeTracker.Clear();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SecretsProjectionContract.EnsureCurrentAsync(context));
            Assert.Contains("dual-migrate.sh apply", exception.Message, StringComparison.Ordinal);
            Assert.Contains(SecretsSearchKeys.UnicodeOrdinalIgnoreCaseAlgorithmId, exception.Message, StringComparison.Ordinal);

            Assert.Equal(1, await SecretsProjectionContract.ReindexAsync(context));
            context.ChangeTracker.Clear();
            await SecretsProjectionContract.EnsureCurrentAsync(context);

            var repaired = await context.Secrets.AsNoTracking().SingleAsync();
            Assert.Equal("\uA7DC", repaired.TypeNameLookupKey);
            Assert.Equal("\uA7DC", SecretDocument.Parse(repaired.Payload).TypeNameLookupKey);
            Assert.Equal(originalToken, repaired.ConcurrencyToken);
            Assert.Equal(0, await SecretsProjectionContract.ReindexAsync(context));

            var page = await new EfSecretRepository(context).ListPageAsync(
                "tenant-a",
                new SecretRepositoryListRequest(typeName: legacyRuntimeKey));
            Assert.Equal("legacy.projection", Assert.Single(page.Items).Name);
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
        }
    }

    [SkippableFact]
    public async Task Operator_apply_reindexes_legacy_rows_before_validate_startup()
    {
        Skip.IfNot(DualMigrateProcessRunner.HasDotnetEf(), "dotnet-ef is not available.");
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-projection-tool-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Pooling=False";
        byte[] originalToken;
        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var context = new SecretsSqliteDbContext(CreateOptions(connection));
                await EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName);
                var current = SecretDocument.FromSecret(CreateSecret("\u019B"));
                var record = (current with { TypeNameLookupKey = "\u019B" }).ToRecord();
                context.Secrets.Add(record);
                await context.SaveChangesAsync();
                originalToken = record.ConcurrencyToken.ToArray();
            }

            await using var provider = new ServiceCollection()
                .AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = connectionString,
                    MigratePolicy = EfMigratePolicy.Validate
                })
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var lifecycle = provider.GetRequiredService<SecretsEfMigrationHostedService>();
            var startupFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => lifecycle.InitializeAsync());
            Assert.Contains("dual-migrate.sh apply", startupFailure.Message, StringComparison.Ordinal);

            var result = DualMigrateProcessRunner.RunFromExistingBuild(
                ["apply", "--sqlite"],
                new Dictionary<string, string?> { ["ELSA_SECRETS_EF_SQLITE"] = connectionString });
            Assert.True(result.ExitCode == 0, result.Describe());
            Assert.Contains("reindexed 1 row(s)", result.Output, StringComparison.Ordinal);
            await lifecycle.InitializeAsync();

            await using var verifyConnection = new SqliteConnection(connectionString);
            await verifyConnection.OpenAsync();
            await using var verifyContext = new SecretsSqliteDbContext(CreateOptions(verifyConnection));
            await SecretsProjectionContract.EnsureCurrentAsync(verifyContext);
            var repaired = await verifyContext.Secrets.AsNoTracking().SingleAsync();
            Assert.Equal("\uA7DC", repaired.TypeNameLookupKey);
            Assert.Equal(originalToken, repaired.ConcurrencyToken);
        }
        finally
        {
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
        }
    }

    [Fact]
    public async Task Reindex_processes_more_than_one_bounded_batch()
    {
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-projection-batches-{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        try
        {
            await using var context = new SecretsSqliteDbContext(CreateOptions(connection));
            await EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName);
            for (var index = 0; index < 101; index++)
            {
                var current = SecretDocument.FromSecret(CreateSecret("\u019B", $"legacy.projection.{index:D3}"));
                context.Secrets.Add((current with { TypeNameLookupKey = "\u019B" }).ToRecord());
            }

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            Assert.Equal(101, await SecretsProjectionContract.ReindexAsync(context));
            await SecretsProjectionContract.EnsureCurrentAsync(context);
            Assert.Equal(101, await context.Secrets.CountAsync(record => record.TypeNameLookupKey == "\uA7DC"));
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
        }
    }

    [Fact]
    public async Task Startup_audit_pages_by_keyset_without_offset()
    {
        var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-projection-keyset-{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        var sql = new List<string>();
        try
        {
            var options = new DbContextOptionsBuilder<SecretsSqliteDbContext>()
                .UseSqlite(connection, sqlite => sqlite
                    .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                    .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
                .LogTo(sql.Add, [DbLoggerCategory.Database.Command.Name], LogLevel.Information)
                .Options;
            await using var context = new SecretsSqliteDbContext(options);
            await EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName);
            for (var index = 0; index < 101; index++)
                context.Secrets.Add(SecretDocument.FromSecret(CreateSecret("text", $"current.{index:D3}")).ToRecord());
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            sql.Clear();

            await SecretsProjectionContract.EnsureCurrentAsync(context);

            var selects = sql
                .Where(statement => statement.Contains("FROM \"elsa_secrets\"", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(2, selects.Length);
            Assert.All(selects, statement => Assert.DoesNotContain("OFFSET", statement, StringComparison.OrdinalIgnoreCase));
            Assert.Contains("\"TenantId\"", selects[1], StringComparison.Ordinal);
            Assert.Contains("\"NormalizedName\"", selects[1], StringComparison.Ordinal);
            Assert.Contains('>', selects[1]);
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
        }
    }

    [Fact]
    public async Task Damaged_payload_wraps_json_exception_with_row_identity_and_rolls_back()
    {
        const string leakedPayload = "PAYLOAD-LEAK-MARKER";
        await using var fixture = await SqliteProjectionFixture.CreateAsync();
        var damaged = SecretDocument.FromSecret(CreateSecret("text", "z.damaged")).ToRecord();
        damaged.Payload = $"{{\"not-json\" {leakedPayload}";
        var good = SecretDocument.FromSecret(CreateSecret("\u019B", "a.legacy"));
        fixture.Context.Secrets.Add((good with { TypeNameLookupKey = "\u019B" }).ToRecord());
        fixture.Context.Secrets.Add(damaged);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var startup = await Assert.ThrowsAsync<SecretsProjectionException>(
            () => SecretsProjectionContract.EnsureCurrentAsync(fixture.Context));
        AssertRowDiagnostic(startup, "tenant-a", "z.damaged", leakedPayload);
        Assert.IsAssignableFrom<JsonException>(startup.InnerException);

        var reindex = await Assert.ThrowsAsync<SecretsProjectionException>(
            () => SecretsProjectionContract.ReindexAsync(fixture.Context));
        AssertRowDiagnostic(reindex, "tenant-a", "z.damaged", leakedPayload);
        Assert.IsAssignableFrom<JsonException>(reindex.InnerException);

        fixture.Context.ChangeTracker.Clear();
        var leftover = await fixture.Context.Secrets.AsNoTracking()
            .SingleAsync(record => record.NormalizedName == "a.legacy");
        Assert.Equal("\u019B", leftover.TypeNameLookupKey);
    }

    [Theory]
    [InlineData("missing-secret")]
    [InlineData("null-secret")]
    [InlineData("missing-versions")]
    [InlineData("null-versions")]
    [InlineData("null-version")]
    public async Task Structurally_invalid_document_is_redacted_and_scoped_to_its_row(string corruption)
    {
        const string leakedPayload = "STRUCTURAL-PAYLOAD-LEAK-MARKER";
        await using var fixture = await SqliteProjectionFixture.CreateAsync();
        var malformed = SecretDocument.FromSecret(CreateSecret("text", $"invalid.{corruption}")).ToRecord();
        var payload = JsonNode.Parse(malformed.Payload)!.AsObject();
        var serializedSecret = payload["secret"]!.AsObject();
        payload["redactionProbe"] = leakedPayload;
        switch (corruption)
        {
            case "missing-secret":
                payload.Remove("secret");
                break;
            case "null-secret":
                payload["secret"] = null;
                break;
            case "missing-versions":
                serializedSecret.Remove("versions");
                break;
            case "null-versions":
                serializedSecret["versions"] = null;
                break;
            case "null-version":
                serializedSecret["versions"]!.AsArray()[0] = null;
                break;
            default:
                throw new InvalidOperationException($"Unknown test corruption '{corruption}'.");
        }

        malformed.Payload = payload.ToJsonString();
        Assert.Contains(leakedPayload, malformed.Payload, StringComparison.Ordinal);
        fixture.Context.Secrets.Add(malformed);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<SecretsProjectionException>(
            () => SecretsProjectionContract.EnsureCurrentAsync(fixture.Context));
        AssertRowDiagnostic(exception, "tenant-a", $"invalid.{corruption}", leakedPayload);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Ill_formed_utf16_json_is_redacted_and_scoped_to_its_row()
    {
        const string leakedPayload = "UTF16-PAYLOAD-LEAK-MARKER";
        await using var fixture = await SqliteProjectionFixture.CreateAsync();
        var malformed = SecretDocument.FromSecret(CreateSecret("text", "invalid.utf16")).ToRecord();
        var payload = JsonNode.Parse(malformed.Payload)!.AsObject();
        payload["redactionProbe"] = leakedPayload;
        malformed.Payload = payload.ToJsonString().Replace(
            "\"typeName\":\"text\"",
            "\"typeName\":\"\\uD800\"",
            StringComparison.Ordinal);
        Assert.Contains("\\uD800", malformed.Payload, StringComparison.Ordinal);
        fixture.Context.Secrets.Add(malformed);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<SecretsProjectionException>(
            () => SecretsProjectionContract.EnsureCurrentAsync(fixture.Context));
        AssertRowDiagnostic(exception, "tenant-a", "invalid.utf16", leakedPayload);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task Invalid_projected_value_is_redacted_and_scoped_to_its_row()
    {
        const string leakedPayload = "PROJECTED-VALUE-PAYLOAD-LEAK-MARKER";
        await using var fixture = await SqliteProjectionFixture.CreateAsync();
        var malformed = SecretDocument.FromSecret(CreateSecret("text", "invalid.projected-value")).ToRecord();
        var payload = JsonNode.Parse(malformed.Payload)!.AsObject();
        payload["redactionProbe"] = leakedPayload;
        payload["secret"]!["typeName"] = null;
        malformed.Payload = payload.ToJsonString();
        fixture.Context.Secrets.Add(malformed);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<SecretsProjectionException>(
            () => SecretsProjectionContract.EnsureCurrentAsync(fixture.Context));
        AssertRowDiagnostic(exception, "tenant-a", "invalid.projected-value", leakedPayload);
        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    [Fact]
    public async Task Required_payload_properties_follow_case_insensitive_deserializer_semantics()
    {
        await using var fixture = await SqliteProjectionFixture.CreateAsync();
        var record = SecretDocument.FromSecret(CreateSecret("text", "case-insensitive-shape")).ToRecord();
        var payload = JsonNode.Parse(record.Payload)!.AsObject();
        var serializedSecret = payload["secret"]!.AsObject();
        serializedSecret["Versions"] = serializedSecret["versions"]!.DeepClone();
        serializedSecret.Remove("versions");
        payload["Secret"] = serializedSecret.DeepClone();
        payload.Remove("secret");
        record.Payload = payload.ToJsonString();
        fixture.Context.Secrets.Add(record);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await SecretsProjectionContract.EnsureCurrentAsync(fixture.Context);
    }

    [Fact]
    public async Task Null_version_entry_rolls_back_the_current_reindex_batch()
    {
        const string leakedPayload = "NULL-VERSION-PAYLOAD-LEAK-MARKER";
        await using var fixture = await SqliteProjectionFixture.CreateAsync();
        var repairable = SecretDocument.FromSecret(CreateSecret("\u019B", "a.repairable"));
        fixture.Context.Secrets.Add((repairable with { TypeNameLookupKey = "\u019B" }).ToRecord());

        var malformed = SecretDocument.FromSecret(CreateSecret("text", "z.null-version")).ToRecord();
        var payload = JsonNode.Parse(malformed.Payload)!.AsObject();
        var serializedSecret = payload["secret"]!.AsObject();
        serializedSecret["description"] = leakedPayload;
        serializedSecret["versions"]!.AsArray()[0] = null;
        malformed.Payload = payload.ToJsonString();
        fixture.Context.Secrets.Add(malformed);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<SecretsProjectionException>(
            () => SecretsProjectionContract.ReindexAsync(fixture.Context));
        AssertRowDiagnostic(exception, "tenant-a", "z.null-version", leakedPayload);
        Assert.IsType<InvalidOperationException>(exception.InnerException);

        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        await fixture.Context.SaveChangesAsync();
        var rolledBack = await fixture.Context.Secrets.AsNoTracking()
            .SingleAsync(record => record.NormalizedName == "a.repairable");
        Assert.Equal("\u019B", rolledBack.TypeNameLookupKey);
    }

    [Fact]
    public async Task Identity_mismatch_rolls_back_batch_and_names_the_row()
    {
        await using var fixture = await SqliteProjectionFixture.CreateAsync();
        var mismatched = SecretDocument.FromSecret(CreateSecret("text", "embedded-name")).ToRecord();
        mismatched.NormalizedName = "z.row-key";
        fixture.Context.Secrets.Add(mismatched);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var startup = await Assert.ThrowsAsync<SecretsProjectionException>(
            () => SecretsProjectionContract.EnsureCurrentAsync(fixture.Context));
        AssertRowDiagnostic(startup, "tenant-a", "z.row-key", payloadFragment: "embedded-name");
        Assert.Null(startup.InnerException);

        var good = SecretDocument.FromSecret(CreateSecret("\u019B", "a.legacy"));
        fixture.Context.Secrets.Add((good with { TypeNameLookupKey = "\u019B" }).ToRecord());
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var reindex = await Assert.ThrowsAsync<SecretsProjectionException>(
            () => SecretsProjectionContract.ReindexAsync(fixture.Context));
        AssertRowDiagnostic(reindex, "tenant-a", "z.row-key", payloadFragment: "embedded-name");
        Assert.Null(reindex.InnerException);

        fixture.Context.ChangeTracker.Clear();
        var leftover = await fixture.Context.Secrets.AsNoTracking()
            .SingleAsync(record => record.NormalizedName == "a.legacy");
        Assert.Equal("\u019B", leftover.TypeNameLookupKey);
        var corrupt = await fixture.Context.Secrets.AsNoTracking()
            .SingleAsync(record => record.NormalizedName == "z.row-key");
        Assert.Equal("embedded-name", SecretDocument.Parse(corrupt.Payload).NormalizedName);
    }

    [Theory]
    [InlineData("tenantId", "foreign-tenant")]
    [InlineData("normalizedName", "foreign-name")]
    public async Task Stored_document_identity_mismatch_fails_closed_even_when_nested_secret_matches_the_row(
        string propertyName,
        string foreignValue)
    {
        const string leakedPayload = "IDENTITY-PAYLOAD-LEAK-MARKER";
        await using var fixture = await SqliteProjectionFixture.CreateAsync();
        var record = SecretDocument.FromSecret(CreateSecret("text", "stored-identity")).ToRecord();
        var payload = JsonNode.Parse(record.Payload)!.AsObject();
        payload[propertyName] = foreignValue;
        payload["secret"]!["description"] = leakedPayload;
        record.Payload = payload.ToJsonString();
        fixture.Context.Secrets.Add(record);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<SecretsProjectionException>(
            () => SecretsProjectionContract.EnsureCurrentAsync(fixture.Context));
        AssertRowDiagnostic(exception, "tenant-a", "stored-identity", leakedPayload);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("tenantId", "foreign-tenant")]
    [InlineData("name", "foreign-name")]
    public async Task Nested_secret_identity_mismatch_fails_closed_when_stored_document_matches_the_row(
        string propertyName,
        string foreignValue)
    {
        const string leakedPayload = "NESTED-IDENTITY-PAYLOAD-LEAK-MARKER";
        await using var fixture = await SqliteProjectionFixture.CreateAsync();
        var record = SecretDocument.FromSecret(CreateSecret("text", "nested-identity")).ToRecord();
        var payload = JsonNode.Parse(record.Payload)!.AsObject();
        payload["redactionProbe"] = leakedPayload;
        payload["secret"]![propertyName] = foreignValue;
        record.Payload = payload.ToJsonString();
        fixture.Context.Secrets.Add(record);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<SecretsProjectionException>(
            () => SecretsProjectionContract.EnsureCurrentAsync(fixture.Context));
        AssertRowDiagnostic(exception, "tenant-a", "nested-identity", leakedPayload);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Current_columns_with_legacy_payload_fail_closed_then_reindex_repairs_payload_copy()
    {
        await using var fixture = await SqliteProjectionFixture.CreateAsync();
        var current = SecretDocument.FromSecret(CreateSecret("\u019B"));
        Assert.Equal("\uA7DC", current.TypeNameLookupKey);
        var record = current.ToRecord();
        record.Payload = (current with { TypeNameLookupKey = "\u019B" }).ToPayload();
        fixture.Context.Secrets.Add(record);
        await fixture.Context.SaveChangesAsync();
        var originalToken = record.ConcurrencyToken.ToArray();
        fixture.Context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SecretsProjectionContract.EnsureCurrentAsync(fixture.Context));
        Assert.IsNotType<SecretsProjectionException>(exception);
        Assert.Contains("dual-migrate.sh apply", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\u019B", exception.Message, StringComparison.Ordinal);

        Assert.Equal(1, await SecretsProjectionContract.ReindexAsync(fixture.Context));
        fixture.Context.ChangeTracker.Clear();
        await SecretsProjectionContract.EnsureCurrentAsync(fixture.Context);

        var repaired = await fixture.Context.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal("\uA7DC", repaired.TypeNameLookupKey);
        Assert.Equal("\uA7DC", SecretDocument.Parse(repaired.Payload).TypeNameLookupKey);
        Assert.Equal(originalToken, repaired.ConcurrencyToken);
        Assert.Equal(0, await SecretsProjectionContract.ReindexAsync(fixture.Context));
    }

    private static void AssertRowDiagnostic(
        SecretsProjectionException exception,
        string tenantId,
        string normalizedName,
        string payloadFragment)
    {
        Assert.Equal(tenantId, exception.TenantId);
        Assert.Equal(normalizedName, exception.NormalizedName);
        Assert.Contains(tenantId, exception.Message, StringComparison.Ordinal);
        Assert.Contains(normalizedName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("dual-migrate.sh apply", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not committed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(payloadFragment, exception.Message, StringComparison.Ordinal);
    }

    private sealed class SqliteProjectionFixture : IAsyncDisposable
    {
        private readonly string _path;
        private readonly SqliteConnection _connection;

        private SqliteProjectionFixture(string path, SqliteConnection connection, SecretsSqliteDbContext context)
        {
            _path = path;
            _connection = connection;
            Context = context;
        }

        public SecretsSqliteDbContext Context { get; }

        public static async Task<SqliteProjectionFixture> CreateAsync()
        {
            var path = Path.Join(Path.GetTempPath(), $"elsa-secrets-projection-{Guid.NewGuid():N}.db");
            var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            var context = new SecretsSqliteDbContext(CreateOptions(connection));
            await EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName);
            return new SqliteProjectionFixture(path, connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            File.Delete(_path);
            File.Delete($"{_path}-wal");
            File.Delete($"{_path}-shm");
        }
    }

    private static DbContextOptions<SecretsSqliteDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<SecretsSqliteDbContext>()
            .UseSqlite(connection, sqlite => sqlite
                .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
            .Options;

    private static Secret CreateSecret(string typeName, string name = "legacy.projection") => new()
    {
        TenantId = "tenant-a",
        Name = name,
        DisplayName = "Legacy projection",
        TypeName = typeName,
        StoreName = SecretStoreNames.Encrypted,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                Status = SecretStatus.Active,
                Payload = SecretPayload.FromValue("value")
            }
        ]
    };
}
