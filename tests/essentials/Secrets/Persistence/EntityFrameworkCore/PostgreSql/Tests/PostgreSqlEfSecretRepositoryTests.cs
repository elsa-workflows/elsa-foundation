using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests;

[Collection(PostgresContainerCollection.Name)]
public sealed class PostgreSqlEfSecretRepositoryTests(PostgresContainerFixture fixture)
{
    /// <summary>The provider selector the CLI takes, which is the module's own <c>[EfModule]</c> provider name.</summary>
    private const string Provider = "PostgreSql";

    [Fact]
    public void UseNpgsql_sets_the_provider_history_table_when_the_engine_is_loaded()
    {
        var builder = new DbContextOptionsBuilder();
        EfRelationalProviderBinding.UseNpgsql(
            builder,
            "Host=localhost;Database=x;Username=postgres;Password=postgres",
            SecretsEfModule.HistoryTableName,
            typeof(SecretsPostgreSqlDbContext).Assembly.GetName().Name);

        var relational = builder.Options.Extensions.OfType<RelationalOptionsExtension>().SingleOrDefault()
                         ?? throw new InvalidOperationException("Npgsql binding did not install a relational extension.");
        Assert.Equal(SecretsEfModule.HistoryTableName, relational.MigrationsHistoryTableName);
        Assert.Contains("Npgsql", relational.GetType().Name, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Migrate_crud_revision_and_search_work_on_postgres()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = CreateContext(connectionString);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260910210216_Initial");

        var repository = new EfSecretRepository(context);
        await repository.SaveAsync(Secret("tenant-a", "before.upgrade", "preserved", scope: "Finance"));
        await EfDatabaseMigrator.ApplyAsync(context, SecretsPostgreSqlDbContext.ExpectedProviderName);
        Assert.Equal(
            "before.upgrade",
            Assert.Single((await repository.ListPageAsync(
                "tenant-a",
                new SecretRepositoryListRequest(scope: "FINANCE"))).Items).Name);

        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        Assert.True(await repository.TryAddAsync(Secret("tenant-a", "payments.api", "alpha", "Payments API")));
        Assert.False(await repository.TryAddAsync(Secret("tenant-a", "payments.api", "duplicate")));

        var current = await revisions.FindWithRevisionAsync("tenant-a", "payments.api");
        current!.Secret.DisplayName = "updated";
        var saved = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Saved, saved.Status);
        var stale = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Conflict, stale.Status);

        var page = await repository.ListPageAsync(
            "tenant-a",
            new SecretRepositoryListRequest(search: "PAYMENTS", take: 10));
        Assert.Equal("payments.api", Assert.Single(page.Items).Name);
        Assert.Equal("updated", page.Items[0].DisplayName);

        var plusTwo = new DateTimeOffset(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(2));
        Assert.True(await repository.TryAddAsync(Secret("tenant-a", "offset.future", "v", expiresAt: plusTwo)));
        var now = new DateTimeOffset(2026, 8, 16, 10, 30, 0, TimeSpan.FromHours(-1));
        var active = await repository.ListPageAsync(
            "tenant-a",
            new SecretRepositoryListRequest(activeOnly: true, now: now, take: 10));
        Assert.Contains(active.Items, secret => secret.Name == "offset.future");

        var longScope = new string('s', 100) + "München";
        await repository.SaveAsync(Secret("tenant-a", "long.lookup", "long", scope: longScope));
        Assert.Contains(
            (await repository.ListPageAsync(
                "tenant-a",
                new SecretRepositoryListRequest(scope: longScope.ToUpperInvariant()))).Items,
            secret => secret.Name == "long.lookup");
    }

    [SkippableFact]
    public async Task Provider_guard_refuses_a_postgres_context_when_sqlite_is_expected()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = CreateContext(connectionString);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EfDatabaseMigrator.ApplyAsync(context, SecretsSqliteDbContext.ExpectedProviderName));
        Assert.Contains(SecretsPostgreSqlDbContext.ExpectedProviderName, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole out-of-process operator path against a live PostgreSQL server (#1878): the real
    /// <c>dotnet elsa persistence</c> tool — a separate process, against a built host's output — applies this
    /// module's compiled migrations, refuses to exit 0 while the projection reindex it declares is still
    /// outstanding, runs that repair under <c>post-migrate</c>, and leaves a database a fresh runtime
    /// <c>Validate</c> accepts. Before #1878 this drove <c>tools/ef/dual-migrate.sh</c>.
    /// </summary>
    /// <remarks>
    /// The connection hygiene is the reason this stays a real subprocess against a real server rather than an
    /// in-process call: a driver, not this repository's code, is what would echo a credential back, and only a
    /// live connection produces one. Both transports the tool accepts are exercised — the value in the child's
    /// environment, then the value on its stdin — and both streams are checked on the refusal as well as on the
    /// success, because a refusal message is exactly where an echoed connection string would surface.
    /// </remarks>
    [SkippableFact]
    public async Task Real_operator_apply_then_fresh_runtime_validate_uses_the_postgres_artifact()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        byte[] originalToken;

        await using (var olderContext = CreateContext(connectionString))
        {
            await olderContext.GetService<IMigrator>().MigrateAsync("20260910210216_Initial");
            var pending = (await olderContext.Database.GetPendingMigrationsAsync()).ToArray();
            Assert.Contains("20260911011058_WidenLookupKeys", pending);

            var exception = await Assert.ThrowsAsync<EfPendingMigrationsException>(() =>
                EfDatabaseMigrator.ApplyAsync(
                    olderContext,
                    SecretsPostgreSqlDbContext.ExpectedProviderName,
                EfMigratePolicy.Validate));
            Assert.Contains("20260911011058_WidenLookupKeys", exception.Message, StringComparison.Ordinal);

            var legacySecret = Secret("tenant-a", "legacy.postgres.projection", "value");
            legacySecret.TypeName = "\u019B";
            var current = SecretDocument.FromSecret(legacySecret);
            var record = (current with { TypeNameLookupKey = "\u019B" }).ToRecord();
            olderContext.Secrets.Add(record);
            await olderContext.SaveChangesAsync();
            originalToken = record.ConcurrencyToken.ToArray();
        }

        var applied = PersistenceCliProcessRunner.Run(
            "apply",
            Provider,
            connectionString,
            ConnectionTransport.Environment);
        AssertNothingSensitiveWasWritten(applied, connectionString, "apply");

        // A refusal, not a failure to apply: the schema moved forward and the declared data repair did not
        // run by itself. Exiting 0 here would tell an operator the database was ready when it is not.
        Assert.Equal(EfToolingExitCode.NegativeResult, applied.ExitCode);
        Assert.Contains("post-migration-required", applied.Error, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretsProjectionReindex), applied.Error, StringComparison.Ordinal);
        Assert.Contains(
            EfPostMigrationActions.CommandFor(PersistenceCliProcessRunner.Module, Provider),
            applied.Error,
            StringComparison.Ordinal);

        await using (var afterApply = CreateContext(connectionString))
        {
            var history = (await afterApply.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.Contains("20260910210216_Initial", history);
            Assert.Contains("20260911011058_WidenLookupKeys", history);
            Assert.Empty(await afterApply.Database.GetPendingMigrationsAsync());
            Assert.True(await TableExistsAsync(afterApply, SecretsEfModule.TableName));
            Assert.True(await TableExistsAsync(afterApply, SecretsEfModule.HistoryTableName));
            // …and the repair genuinely did not run: a row quietly reindexed by the audit would look exactly
            // like a healthy apply from here on.
            Assert.Equal("\u019B", (await afterApply.Secrets.AsNoTracking().SingleAsync()).TypeNameLookupKey);
        }

        var repaired = PersistenceCliProcessRunner.Run(
            "post-migrate",
            Provider,
            connectionString,
            ConnectionTransport.Stdin);
        AssertNothingSensitiveWasWritten(repaired, connectionString, "post-migrate");

        Assert.True(repaired.ExitCode == EfToolingExitCode.Success, repaired.Describe());
        Assert.Contains(PersistenceCliProcessRunner.Module, repaired.Output, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretsProjectionReindex), repaired.Output, StringComparison.Ordinal);
        // The count, not just the name: the action appears under DECLARED whether or not it ran, and
        // "nothing required" on a database that still needs repairing is the report this must never give.
        Assert.Contains("1 post-migration action(s) ran", repaired.Output, StringComparison.Ordinal);

        await using var context = CreateContext(connectionString);
        Assert.Equal(EfProviderNames.PostgreSql, context.Database.ProviderName);
        Assert.Same(typeof(SecretsPostgreSqlDbContext).Assembly, context.GetService<IMigrationsAssembly>().Assembly);
        var row = await context.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal("\uA7DC", row.TypeNameLookupKey);
        Assert.Equal("\uA7DC", SecretDocument.Parse(row.Payload).TypeNameLookupKey);
        Assert.Equal(originalToken, row.ConcurrencyToken);
        await EfDatabaseMigrator.ApplyAsync(
            context,
            SecretsPostgreSqlDbContext.ExpectedProviderName,
            EfMigratePolicy.Validate);
    }

    [SkippableFact]
    public async Task Projection_reindex_rejects_a_concurrent_postgres_writer_without_overwriting_it()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using (var setup = CreateContext(connectionString))
        {
            await EfDatabaseMigrator.ApplyAsync(setup, SecretsPostgreSqlDbContext.ExpectedProviderName);
            var legacySecret = Secret("tenant-a", "legacy.concurrent.projection", "value");
            legacySecret.TypeName = "\u019B";
            var current = SecretDocument.FromSecret(legacySecret);
            setup.Secrets.Add((current with { TypeNameLookupKey = "\u019B" }).ToRecord());
            await setup.SaveChangesAsync();
        }

        var concurrentWriter = new ConcurrentSecretWriterInterceptor(connectionString);
        await using (var repair = CreateContext(connectionString, concurrentWriter))
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                SecretsProjectionContract.ReindexAsync(repair));
        }

        await using var verify = CreateContext(connectionString);
        var stored = await verify.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal("Concurrent writer", SecretDocument.Parse(stored.Payload).Secret.DisplayName);
        Assert.Equal(concurrentWriter.ConcurrencyToken, stored.ConcurrencyToken);
    }

    [SkippableFact]
    public async Task Projection_reindex_uses_translatable_keyset_pages_on_postgres()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = CreateContext(connectionString);
        await EfDatabaseMigrator.ApplyAsync(context, SecretsPostgreSqlDbContext.ExpectedProviderName);

        const string legacyRuntimeKey = "\u019B";
        for (var index = 0; index < 101; index++)
        {
            var current = SecretDocument.FromSecret(Secret("tenant-a", $"keyset.{index:D3}", "value"));
            context.Secrets.Add((current with { TypeNameLookupKey = legacyRuntimeKey }).ToRecord());
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.Equal(101, await SecretsProjectionContract.ReindexAsync(context));
        await SecretsProjectionContract.EnsureCurrentAsync(context);
        var expectedTypeKey = SecretsSearchKeys.LookupKey(SecretTypeNames.Text);
        Assert.Equal(101, await context.Secrets.CountAsync(record => record.TypeNameLookupKey == expectedTypeKey));
    }

    private static SecretsPostgreSqlDbContext CreateContext(
        string connectionString,
        params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<SecretsPostgreSqlDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(SecretsPostgreSqlDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(SecretsEfModule.HistoryTableName));
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);

        return new SecretsPostgreSqlDbContext(builder.Options);
    }

    private static async Task<bool> TableExistsAsync(SecretsPostgreSqlDbContext context, string table)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @table)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// Neither stream carried the connection string or its password. Asserted before any assertion that
    /// reports a captured stream, so a failure below can never be the thing that prints the credential.
    /// </summary>
    private static void AssertNothingSensitiveWasWritten(CliResult run, string connectionString, string command)
    {
        var password = new NpgsqlConnectionStringBuilder(connectionString).Password;
        Assert.False(
            ContainsSensitiveConnectionData(run.Output, connectionString, password),
            $"The operator wrote sensitive PostgreSQL connection data to stdout on '{command}'.");
        Assert.False(
            ContainsSensitiveConnectionData(run.Error, connectionString, password),
            $"The operator wrote sensitive PostgreSQL connection data to stderr on '{command}'.");
    }

    private static bool ContainsSensitiveConnectionData(string text, string connectionString, string? password) =>
        text.Contains(connectionString, StringComparison.Ordinal) ||
        (!string.IsNullOrEmpty(password) && text.Contains(password, StringComparison.Ordinal));

    private sealed class ConcurrentSecretWriterInterceptor(string connectionString) : SaveChangesInterceptor
    {
        private bool hasRun;

        public byte[]? ConcurrencyToken { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (hasRun)
                return result;

            hasRun = true;
            await using var writer = CreateContext(connectionString);
            var record = await writer.Secrets.SingleAsync(cancellationToken);
            var secret = SecretDocument.Parse(record.Payload).Secret;
            secret.DisplayName = "Concurrent writer";
            SecretDocument.FromSecret(secret).CopyProjectionsTo(record);
            await writer.SaveChangesAsync(cancellationToken);
            ConcurrencyToken = record.ConcurrencyToken.ToArray();
            return result;
        }
    }

    private static Secret Secret(
        string tenantId,
        string name,
        string value,
        string? displayName = null,
        DateTimeOffset? expiresAt = null,
        string? scope = null) => new()
    {
        TenantId = tenantId,
        Name = name,
        DisplayName = displayName ?? name,
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Encrypted,
        Scope = scope,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                Status = SecretStatus.Active,
                ExpiresAt = expiresAt,
                Payload = SecretPayload.FromValue(value)
            }
        ]
    };
}
