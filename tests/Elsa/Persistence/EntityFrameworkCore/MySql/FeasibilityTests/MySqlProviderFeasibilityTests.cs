using System.Data;
using System.Reflection;
using System.Text.Json;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.Data.MySqlClient;
using MySql.EntityFrameworkCore.Extensions;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests;

[Collection(MySqlContainerCollection.Name)]
public sealed class MySqlProviderFeasibilityTests(MySqlContainerFixture fixture)
{
    [Fact]
    public void Oracle_provider_binds_to_ef10_and_exposes_the_selected_versions()
    {
        using var context = MySqlTestContext.Create(
            "Server=localhost;Port=3306;Database=elsa;User ID=root;Password=root;SslMode=Disabled");

        Assert.Equal(SecretsMySqlDbContext.ExpectedProviderName, context.Database.ProviderName);
        var providerAssembly = typeof(MySQLDbContextOptionsExtensions).Assembly;
        Assert.StartsWith("10.0.9", providerAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, StringComparison.Ordinal);
        Assert.StartsWith("26.7.0", providerAssembly.GetName().Version!.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("26.7.0", typeof(MySqlConnection).Assembly.GetName().Version!.ToString(), StringComparison.Ordinal);

        var designModel = context.GetService<IDesignTimeModel>().Model;
        var entity = designModel.FindEntityType(typeof(SecretRecord))!;
        Assert.Equal("elsa_secrets", entity.GetTableName());
        Assert.Equal(SecretsMySqlDbContext.CharacterSet, designModel.FindAnnotation("MySQL:Charset")?.Value);
        Assert.Equal(SecretsMySqlDbContext.Collation, designModel.GetCollation());
        Assert.Equal(
            SecretsMySqlDbContext.Collation,
            entity.FindProperty(nameof(SecretRecord.NormalizedName))!.FindAnnotation("MySQL:Collation")?.Value);
        Assert.Equal("json", entity.FindProperty(nameof(SecretRecord.Payload))!.GetColumnType());
        Assert.Equal("bigint", entity.FindProperty(nameof(SecretRecord.MaxActiveVersionExpiresAt))!.GetColumnType());
        Assert.Equal("varbinary(16)", entity.FindProperty(nameof(SecretRecord.ConcurrencyToken))!.GetColumnType());
    }

    [SkippableFact]
    public async Task Fresh_install_reapply_and_no_pending_model_changes_are_proven()
    {
        SkipIfUnavailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = MySqlTestContext.Create(connectionString);

        await context.Database.MigrateAsync();
        Assert.Contains(SecretsMySqlDbContext.MigrationId, await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(1, await ScalarAsync<long>(context, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'elsa_secrets'"));
        Assert.Equal(1, await ScalarAsync<long>(context, $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '{SecretsMySqlDbContext.HistoryTableName}'"));
        Assert.Equal(
            MySqlContainerFixture.ConflictingDatabaseCollation,
            await ScalarAsync<string>(context, "SELECT DEFAULT_COLLATION_NAME FROM information_schema.schemata WHERE SCHEMA_NAME = DATABASE()"));
        Assert.Equal(
            "NO PAD",
            await ScalarAsync<string>(
                context,
                "SELECT PAD_ATTRIBUTE FROM information_schema.collations WHERE collation_name = @collation",
                ("@collation", SecretsMySqlDbContext.Collation)));
        Assert.Equal(
            7,
            await ScalarAsync<long>(
                context,
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'elsa_secrets' AND collation_name = @collation AND column_name IN ('TenantId', 'NormalizedName', 'NameSearchKey', 'DisplayNameSearchKey', 'TypeNameLookupKey', 'StoreNameLookupKey', 'ScopeLookupKey')",
                ("@collation", SecretsMySqlDbContext.Collation)));
        Assert.Equal(3, await ScalarAsync<long>(context, "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'elsa_secrets' AND index_name = 'IX_elsa_secrets_tenantId_status_normalizedName'"));

        await context.Database.MigrateAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await ScalarAsync<long>(context, $"SELECT COUNT(*) FROM `{SecretsMySqlDbContext.HistoryTableName}`"));
    }

    [SkippableFact]
    public async Task Wrong_migration_assembly_is_rejected_before_domain_work()
    {
        SkipIfUnavailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = MySqlTestContext.Create(
            connectionString,
            migrationsAssembly: typeof(SecretsMySqlDbContext).BaseType!.Assembly.GetName().Name);

        var exception = Assert.Throws<InvalidOperationException>(() => MySqlMigrationArtifactGuard.Ensure(context));
        Assert.Equal(
            "The MySQL feasibility migration artifact must be the test assembly before domain work starts.",
            exception.Message);
        Assert.Equal(0, await ScalarAsync<long>(context, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'elsa_secrets'"));
    }

    [SkippableFact]
    public async Task Pending_model_change_is_detected_without_running_domain_work()
    {
        SkipIfUnavailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using (var migrated = MySqlTestContext.Create(connectionString))
            await migrated.Database.MigrateAsync();

        await using var context = MySqlTestContext.Create(connectionString, includePendingModel: true);
        Assert.True(context.Database.HasPendingModelChanges());
    }

    [SkippableFact]
    public async Task DateTimeOffset_uses_utc_ticks_bigint_while_json_keeps_the_original_offset()
    {
        SkipIfUnavailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = await MigratedContextAsync(connectionString);
        var values = new[]
        {
            (Tenant: "tenant-time-utc", Name: "utc", Value: new DateTimeOffset(2026, 9, 12, 10, 11, 12, 123, TimeSpan.Zero).AddTicks(1)),
            (Tenant: "tenant-time-offset", Name: "offset", Value: new DateTimeOffset(2026, 9, 12, 10, 11, 12, 123, TimeSpan.FromMinutes(330)).AddTicks(4_567))
        };

        foreach (var value in values)
        {
            var record = RecordWithPayload(value.Tenant, value.Name, Payload(value.Value));
            record.MaxActiveVersionExpiresAt = value.Value;
            context.Secrets.Add(record);
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        foreach (var value in values)
        {
            var loaded = await context.Secrets.AsNoTracking().SingleAsync(record => record.TenantId == value.Tenant);
            var loadedExpiration = Assert.IsType<DateTimeOffset>(loaded.MaxActiveVersionExpiresAt);
            Assert.Equal(value.Value.UtcDateTime, loadedExpiration.UtcDateTime);
            Assert.Equal(TimeSpan.Zero, loadedExpiration.Offset);
            Assert.Equal(
                value.Value.UtcTicks,
                await ScalarAsync<long>(
                    context,
                    "SELECT MaxActiveVersionExpiresAt FROM elsa_secrets WHERE TenantId = @tenant AND NormalizedName = @name",
                    ("@tenant", value.Tenant),
                    ("@name", value.Name)));
            Assert.Contains(value.Value.ToString("O"), loaded.Payload, StringComparison.Ordinal);
        }
    }

    [SkippableFact]
    public async Task Binary_collation_and_Elsa_normalized_keys_preserve_ordinal_identity_and_unicode_equivalence()
    {
        SkipIfUnavailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = await MigratedContextAsync(connectionString);
        var asciiUpper = SecretsSearchKeys.LookupKey("Payments.Api");
        var asciiLower = SecretsSearchKeys.LookupKey("payments.api");
        var umlautUpper = SecretsSearchKeys.LookupKey("MÜNCHEN");
        var umlautLower = SecretsSearchKeys.LookupKey("münchen");
        var dotlessI = SecretsSearchKeys.LookupKey("ı");

        Assert.Equal(asciiUpper, asciiLower);
        Assert.Equal(umlautUpper, umlautLower);
        Assert.NotEqual(SecretsSearchKeys.LookupKey("I"), dotlessI);

        context.Secrets.Add(Record("tenant-collation", "CaseSensitive", "raw-upper"));
        context.Secrets.Add(Record("tenant-collation", "casesensitive", "raw-lower"));
        context.Secrets.Add(Record("tenant-collation", "Raw-MÜNCHEN", "unicode-upper"));
        context.Secrets.Add(Record("tenant-collation", "Raw-münchen", "unicode-lower"));
        context.Secrets.Add(Record("tenant-collation", asciiUpper, "ascii"));
        context.Secrets.Add(Record("tenant-collation", umlautUpper, "unicode"));
        context.Secrets.Add(Record("tenant-space", "same-name", "tenant-no-space"));
        context.Secrets.Add(Record("tenant-space ", "same-name", "tenant-space"));
        context.Secrets.Add(Record("tenant-name-space", "name", "name-no-space"));
        context.Secrets.Add(Record("tenant-name-space", "name ", "name-space"));
        await context.SaveChangesAsync();

        Assert.Equal(6, await context.Secrets.CountAsync(record => record.TenantId == "tenant-collation"));
        Assert.Equal(1, await ScalarAsync<long>(
            context,
            "SELECT COUNT(*) FROM elsa_secrets WHERE TenantId = @tenant AND NormalizedName = @name",
            ("@tenant", "tenant-collation"),
            ("@name", "CaseSensitive")));
        Assert.Equal(1, await ScalarAsync<long>(
            context,
            "SELECT COUNT(*) FROM elsa_secrets WHERE TenantId = @tenant AND NormalizedName = @name",
            ("@tenant", "tenant-collation"),
            ("@name", "casesensitive")));
        Assert.Equal(1, await ScalarAsync<long>(
            context,
            "SELECT COUNT(*) FROM elsa_secrets WHERE TenantId = @tenant AND NormalizedName = @name",
            ("@tenant", "tenant-collation"),
            ("@name", "Raw-MÜNCHEN")));
        Assert.Equal(1, await ScalarAsync<long>(
            context,
            "SELECT COUNT(*) FROM elsa_secrets WHERE TenantId = @tenant AND NormalizedName = @name",
            ("@tenant", "tenant-collation"),
            ("@name", "Raw-münchen")));
        Assert.Equal(1, await ScalarAsync<long>(
            context,
            "SELECT COUNT(*) FROM elsa_secrets WHERE TenantId = @tenant AND NormalizedName = @name",
            ("@tenant", "tenant-space"),
            ("@name", "same-name")));
        Assert.Equal(1, await ScalarAsync<long>(
            context,
            "SELECT COUNT(*) FROM elsa_secrets WHERE TenantId = @tenant AND NormalizedName = @name",
            ("@tenant", "tenant-space "),
            ("@name", "same-name")));
        Assert.Equal(1, await ScalarAsync<long>(
            context,
            "SELECT COUNT(*) FROM elsa_secrets WHERE TenantId = @tenant AND NormalizedName = @name",
            ("@tenant", "tenant-name-space"),
            ("@name", "name")));
        Assert.Equal(1, await ScalarAsync<long>(
            context,
            "SELECT COUNT(*) FROM elsa_secrets WHERE TenantId = @tenant AND NormalizedName = @name",
            ("@tenant", "tenant-name-space"),
            ("@name", "name ")));
        var exact = await context.Secrets.SingleAsync(record => record.TenantId == "tenant-collation" && record.NormalizedName == asciiLower);
        Assert.Equal("ascii", JsonDocument.Parse(exact.Payload).RootElement.GetProperty("value").GetString());
    }

    [SkippableFact]
    public async Task Duplicate_normalized_key_exposes_stable_MySql_metadata()
    {
        SkipIfUnavailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = await MigratedContextAsync(connectionString);
        context.Secrets.Add(Record("tenant-unique", SecretsSearchKeys.LookupKey("Payments.Api"), "first"));
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        context.Secrets.Add(Record("tenant-unique", SecretsSearchKeys.LookupKey("payments.api"), "second"));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var mysqlException = Assert.IsType<MySqlException>(exception.InnerException);
        Assert.Equal(1062, mysqlException.Number);
        Assert.Contains("PRIMARY", mysqlException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Json_round_trip_query_and_partial_update_preserve_unmodified_payload()
    {
        SkipIfUnavailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = await MigratedContextAsync(connectionString);
        var payload = "{\"value\":\"alpha\",\"metadata\":{\"region\":\"eu-west\",\"attempt\":3,\"note\":\"München 東京\"},\"secret\":{\"displayName\":\"Payments\",\"versions\":[{\"status\":\"active\",\"payload\":{\"value\":\"alpha\"}}]}}";
        context.Secrets.Add(RecordWithPayload("tenant-json", "json", payload));
        await context.SaveChangesAsync();

        Assert.Equal(1, await ScalarAsync<long>(context, "SELECT COUNT(*) FROM elsa_secrets WHERE JSON_UNQUOTE(JSON_EXTRACT(Payload, '$.secret.displayName')) = 'Payments'"));
        var updated = await ExecuteAsync(
            context,
            "UPDATE elsa_secrets SET Payload = JSON_SET(Payload, '$.secret.displayName', 'Payments API') WHERE TenantId = @tenant AND NormalizedName = @name",
            ("@tenant", "tenant-json"),
            ("@name", "json"));
        Assert.Equal(1, updated);

        context.ChangeTracker.Clear();
        var roundTrip = await context.Secrets.AsNoTracking().SingleAsync();
        using var json = JsonDocument.Parse(roundTrip.Payload);
        Assert.Equal("Payments API", json.RootElement.GetProperty("secret").GetProperty("displayName").GetString());
        Assert.Equal("eu-west", json.RootElement.GetProperty("metadata").GetProperty("region").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("metadata").GetProperty("attempt").GetInt32());
        Assert.Equal("München 東京", json.RootElement.GetProperty("metadata").GetProperty("note").GetString());
        Assert.Equal("alpha", json.RootElement.GetProperty("secret").GetProperty("versions")[0].GetProperty("payload").GetProperty("value").GetString());
    }

    [SkippableFact]
    public async Task Two_context_updates_produce_one_success_and_one_detectable_concurrency_conflict()
    {
        SkipIfUnavailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var first = await MigratedContextAsync(connectionString);
        first.Secrets.Add(Record("tenant-concurrency", "row", "seed"));
        await first.SaveChangesAsync();
        await using var second = MySqlTestContext.Create(connectionString);
        var firstRow = await first.Secrets.SingleAsync();
        var secondRow = await second.Secrets.SingleAsync();
        var originalToken = secondRow.ConcurrencyToken.ToArray();

        firstRow.Payload = Payload("first");
        await first.SaveChangesAsync();
        secondRow.Payload = Payload("second");
        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        Assert.NotNull(exception.Entries.Single().Entity);
        Assert.NotEqual(originalToken, firstRow.ConcurrencyToken);
        var persisted = await first.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal("first", JsonDocument.Parse(persisted.Payload).RootElement.GetProperty("value").GetString());
    }

    [SkippableFact]
    public async Task ExecuteUpdate_and_delete_return_exact_match_counts_for_guarded_operations()
    {
        SkipIfUnavailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = await MigratedContextAsync(connectionString);
        context.Secrets.Add(Record("tenant-dml", "guarded", "active"));
        await context.SaveChangesAsync();

        var updated = await context.Secrets
            .Where(record => record.TenantId == "tenant-dml" && record.NormalizedName == "guarded" && record.Status == "active")
            .ExecuteUpdateAsync(setters => setters.SetProperty(record => record.Status, "leased"));
        var noUpdate = await context.Secrets
            .Where(record => record.TenantId == "tenant-dml" && record.NormalizedName == "missing" && record.Status == "active")
            .ExecuteUpdateAsync(setters => setters.SetProperty(record => record.Status, "leased"));
        var deleted = await context.Secrets
            .Where(record => record.TenantId == "tenant-dml" && record.NormalizedName == "guarded" && record.Status == "leased")
            .ExecuteDeleteAsync();
        var noDelete = await context.Secrets
            .Where(record => record.TenantId == "tenant-dml" && record.NormalizedName == "missing")
            .ExecuteDeleteAsync();

        Assert.Equal(1, updated);
        Assert.Equal(0, noUpdate);
        Assert.Equal(1, deleted);
        Assert.Equal(0, noDelete);
    }

    [SkippableFact]
    public async Task Shared_connection_transaction_commits_or_rolls_back_and_non_enlisted_context_isolated()
    {
        SkipIfUnavailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using (var setup = MySqlTestContext.Create(connectionString))
            await setup.Database.MigrateAsync();
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await using var firstEnlisted = MySqlTestContext.Create(connection);
            await using var secondEnlisted = MySqlTestContext.Create(connection);
            firstEnlisted.Database.UseTransaction(transaction);
            secondEnlisted.Database.UseTransaction(transaction);

            firstEnlisted.Secrets.Add(Record("tenant-transaction", "committed-first", "inside-first"));
            await firstEnlisted.SaveChangesAsync();
            secondEnlisted.Secrets.Add(Record("tenant-transaction", "committed-second", "inside-second"));
            await secondEnlisted.SaveChangesAsync();

            await using var nonEnlisted = MySqlTestContext.Create(connectionString);
            Assert.Equal(0, await nonEnlisted.Secrets.CountAsync(record => record.TenantId == "tenant-transaction"));
            nonEnlisted.Secrets.Add(Record("tenant-independent", "committed-outside", "outside"));
            await nonEnlisted.SaveChangesAsync();
            Assert.Equal(1, await nonEnlisted.Secrets.CountAsync(record => record.TenantId == "tenant-independent"));

            await transaction.CommitAsync();
        }

        await using var reopened = MySqlTestContext.Create(connectionString);
        Assert.Equal(2, await reopened.Secrets.CountAsync(record => record.TenantId == "tenant-transaction"));
        Assert.Equal(1, await reopened.Secrets.CountAsync(record => record.TenantId == "tenant-independent"));

        await using var rollback = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using (var enlisted = MySqlTestContext.Create(connection))
        {
            enlisted.Database.UseTransaction(rollback);
            enlisted.Secrets.Add(Record("tenant-transaction", "rolled-back", "inside"));
            await enlisted.SaveChangesAsync();
        }

        await rollback.RollbackAsync();
        Assert.Equal(0, await reopened.Secrets.CountAsync(record => record.TenantId == "tenant-transaction" && record.NormalizedName == "rolled-back"));
    }

    [SkippableFact]
    public async Task Restart_reconnects_to_migrated_data_and_preserves_concurrency_state()
    {
        SkipIfUnavailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        byte[] token;
        await using (var first = await MigratedContextAsync(connectionString))
        {
            var row = Record("tenant-restart", "durable", "persisted");
            first.Secrets.Add(row);
            await first.SaveChangesAsync();
            token = row.ConcurrencyToken.ToArray();
        }

        await using var restarted = MySqlTestContext.Create(connectionString);
        var durable = await restarted.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal("persisted", JsonDocument.Parse(durable.Payload).RootElement.GetProperty("value").GetString());
        Assert.Equal(token, durable.ConcurrencyToken);
        var pending = await restarted.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    private void SkipIfUnavailable() => Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

    private static async Task<SecretsMySqlDbContext> MigratedContextAsync(string connectionString)
    {
        var context = MySqlTestContext.Create(connectionString);
        await context.Database.MigrateAsync();
        return context;
    }

    private static SecretRecord Record(string tenant, string name, string value) =>
        RecordWithPayload(tenant, name, Payload(value));

    private static SecretRecord RecordWithPayload(string tenant, string name, string payload) => new()
    {
        TenantId = tenant,
        NormalizedName = name,
        NameSearchKey = name,
        DisplayNameSearchKey = name,
        TypeNameLookupKey = "TEXT",
        StoreNameLookupKey = "ENCRYPTED",
        ScopeLookupKey = null,
        Status = "active",
        HasNonExpiringActiveVersion = true,
        Payload = payload,
        ConcurrencyToken = []
    };

    private static string Payload(string value) => $"{{\"value\":\"{value}\"}}";

    private static string Payload(DateTimeOffset expiresAt) =>
        $"{{\"value\":\"time\",\"expiresAt\":\"{expiresAt:O}\"}}";

    private static async Task<T> ScalarAsync<T>(
        DbContext context,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.Add(new MySqlParameter(name, value));
        if (command.Connection!.State != ConnectionState.Open)
            await command.Connection.OpenAsync();
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"The query returned no scalar value: {sql}"));
    }

    private static async Task<int> ExecuteAsync(
        DbContext context,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.Add(new MySqlParameter(name, value));
        if (command.Connection!.State != ConnectionState.Open)
            await command.Connection.OpenAsync();
        return await command.ExecuteNonQueryAsync();
    }
}
