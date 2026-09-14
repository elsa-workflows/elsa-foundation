using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(StudioPreferencesPostgreSqlContainerFixture.CollectionName)]
public sealed class StudioPreferencesPostgreSqlSmokeTests(StudioPreferencesPostgreSqlContainerFixture fixture)
{
    [SkippableFact]
    public async Task Create_read_and_stale_cas_are_durable_on_postgresql()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "PostgreSQL is unavailable.");
        await StudioPreferencesProviderSmoke.RunAsync(
            fixture.ConnectionString,
            connectionString => new StudioPreferencesPostgreSqlDbContext(
                new DbContextOptionsBuilder<StudioPreferencesPostgreSqlDbContext>()
                    .UseNpgsql(connectionString)
                    .Options));
    }
}

[Collection(StudioPreferencesSqlServerContainerFixture.CollectionName)]
public sealed class StudioPreferencesSqlServerSmokeTests(StudioPreferencesSqlServerContainerFixture fixture)
{
    [SkippableFact]
    public async Task Create_read_and_stale_cas_are_durable_on_sql_server()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "SQL Server is unavailable.");
        await StudioPreferencesProviderSmoke.RunAsync(
            fixture.ConnectionString,
            connectionString => new StudioPreferencesSqlServerDbContext(
                new DbContextOptionsBuilder<StudioPreferencesSqlServerDbContext>()
                    .UseSqlServer(connectionString)
                    .Options));
    }
}

[Collection(StudioPreferencesMySqlContainerFixture.CollectionName)]
public sealed class StudioPreferencesMySqlSmokeTests(StudioPreferencesMySqlContainerFixture fixture)
{
    [SkippableFact]
    public async Task Create_read_and_stale_cas_are_durable_on_mysql()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "MySQL is unavailable.");
        await StudioPreferencesProviderSmoke.RunAsync(
            fixture.ConnectionString,
            connectionString => new StudioPreferencesMySqlDbContext(
                new DbContextOptionsBuilder<StudioPreferencesMySqlDbContext>()
                    .UseMySQL(connectionString)
                    .Options));
    }

    [SkippableFact]
    public async Task Public_feature_binds_mysql_with_the_module_history_options_and_a_working_store()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "MySQL is unavailable.");
        var services = new ServiceCollection();
        new StudioPreferencesEntityFrameworkCoreFeature
        {
            Provider = "MySql",
            ConnectionString = fixture.ConnectionString
        }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<StudioPreferencesDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IStudioPreferenceStore>();

        Assert.IsType<StudioPreferencesMySqlDbContext>(context);
        Assert.IsType<EfStudioPreferenceStore>(store);
        Assert.Equal("MySql.EntityFrameworkCore", context.Database.ProviderName);
        var relational = context.GetService<IDbContextOptions>().Extensions
            .OfType<RelationalOptionsExtension>()
            .Single();
        Assert.Equal(StudioPreferencesEfModule.HistoryTableName, relational.MigrationsHistoryTableName);
        Assert.Equal(typeof(StudioPreferencesDbContext).Assembly.GetName().Name, relational.MigrationsAssembly);

        await context.Database.EnsureCreatedAsync();
        var key = new StudioPreferenceKey(
            $"public-feature-user-{Guid.NewGuid():N}",
            "public-feature-tenant",
            "public-feature-host",
            "dashboard");
        var created = await store.WriteAsync(
            key,
            new(1, JsonSerializer.SerializeToElement(new { layout = "public-feature" })),
            StudioPreferenceWriteCondition.MustNotExist,
            DateTimeOffset.Parse("2026-09-12T14:20:00Z"));

        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, created.Status);
        Assert.Equal("public-feature", (await store.FindAsync(key))!.Value.GetProperty("layout").GetString());
    }
}

public sealed class StudioPreferencesProviderModelTests
{
    [Theory]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer", "nvarchar(max)")]
    [InlineData("PostgreSql", "Npgsql.EntityFrameworkCore.PostgreSQL", "text")]
    [InlineData("MySql", "MySql.EntityFrameworkCore", "longtext")]
    public void Provider_specific_models_build_without_connecting_or_starting_migrations(
        string provider,
        string expectedProviderName,
        string expectedValueColumnType)
    {
        using var context = CreateContext(provider);

        Assert.Equal(expectedProviderName, context.Database.ProviderName);
        var entity = context.Model.FindEntityType(typeof(StudioPreferenceRecord));
        Assert.NotNull(entity);
        Assert.NotNull(entity!.FindPrimaryKey());
        Assert.True(entity.FindProperty(nameof(StudioPreferenceRecord.Revision))!.IsConcurrencyToken);
        Assert.Equal(
            expectedValueColumnType,
            entity.FindProperty(nameof(StudioPreferenceRecord.ValueJson))!.GetColumnType(),
            ignoreCase: true);
    }

    private static StudioPreferencesDbContext CreateContext(string provider) => provider switch
    {
        "SqlServer" => new StudioPreferencesSqlServerDbContext(
            new DbContextOptionsBuilder<StudioPreferencesSqlServerDbContext>()
                .UseSqlServer("Server=localhost;Database=unused;User ID=unused;Password=Unused123!;TrustServerCertificate=True")
                .Options),
        "PostgreSql" => new StudioPreferencesPostgreSqlDbContext(
            new DbContextOptionsBuilder<StudioPreferencesPostgreSqlDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options),
        "MySql" => new StudioPreferencesMySqlDbContext(
            new DbContextOptionsBuilder<StudioPreferencesMySqlDbContext>()
                .UseMySQL("Server=localhost;Database=unused;User ID=unused;Password=unused")
                .Options),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };
}

internal static class StudioPreferencesProviderSmoke
{
    public static async Task RunAsync(
        string connectionString,
        Func<string, StudioPreferencesDbContext> createContext)
    {
        await using var firstContext = createContext(connectionString);
        await firstContext.Database.EnsureCreatedAsync();
        var firstStore = new EfStudioPreferenceStore(firstContext);

        var key = new StudioPreferenceKey(
            $"provider-smoke-user-{Guid.NewGuid():N}",
            "provider-smoke-tenant",
            "provider-smoke-host",
            "dashboard");
        var timestamp = new DateTimeOffset(2026, 9, 12, 14, 10, 11, TimeSpan.FromHours(5)).AddTicks(1);
        var created = await firstStore.WriteAsync(
            key,
            new(1, Json("{\"layout\":\"initial\"}")),
            StudioPreferenceWriteCondition.MustNotExist,
            timestamp);

        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, created.Status);
        Assert.NotNull(created.Document);
        Assert.Equal("rev-1", created.Document!.Revision);

        await using var secondContext = createContext(connectionString);
        var secondStore = new EfStudioPreferenceStore(secondContext);
        var independentlyLoaded = await secondStore.FindAsync(key);
        Assert.NotNull(independentlyLoaded);
        Assert.Equal(created.Document.Value.GetRawText(), independentlyLoaded!.Value.GetRawText());
        Assert.Equal(timestamp, independentlyLoaded.UpdatedAt);
        var recordId = await firstContext.Preferences
            .Where(x => x.SubjectId == key.SubjectId)
            .Select(x => x.Id)
            .SingleAsync();
        _ = await secondContext.Preferences.SingleAsync(x => x.Id == recordId);

        var updated = await firstStore.WriteAsync(
            key,
            new(1, Json("{\"layout\":\"updated\"}")),
            StudioPreferenceWriteCondition.Matches(created.Document.Revision),
            timestamp.AddTicks(1));

        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, updated.Status);
        Assert.Equal("rev-2", updated.Document!.Revision);

        var stale = await secondStore.WriteAsync(
            key,
            new(1, Json("{\"layout\":\"stale\"}")),
            StudioPreferenceWriteCondition.Matches(independentlyLoaded.Revision),
            timestamp.AddTicks(2));

        Assert.Equal(StudioPreferenceStoreWriteStatus.Conflict, stale.Status);
        var winner = await firstStore.FindAsync(key);
        Assert.NotNull(winner);
        Assert.Equal("rev-2", winner!.Revision);
        Assert.Equal("updated", winner.Value.GetProperty("layout").GetString());

        var rollbackKey = key with { SubjectId = $"{key.SubjectId}-rollback" };
        await using (var transactionContext = createContext(connectionString))
        {
            await using var transaction = await transactionContext.Database.BeginTransactionAsync();
            var transactionStore = new EfStudioPreferenceStore(transactionContext);
            var staged = await transactionStore.WriteAsync(
                rollbackKey,
                new(1, Json("{\"layout\":\"rolled-back\"}")),
                StudioPreferenceWriteCondition.MustNotExist,
                timestamp.AddTicks(3));
            Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, staged.Status);
            await transaction.RollbackAsync();
        }

        await using var verificationContext = createContext(connectionString);
        var verificationStore = new EfStudioPreferenceStore(verificationContext);
        Assert.Null(await verificationStore.FindAsync(rollbackKey));
    }

    private static System.Text.Json.JsonElement Json(string json) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
}
