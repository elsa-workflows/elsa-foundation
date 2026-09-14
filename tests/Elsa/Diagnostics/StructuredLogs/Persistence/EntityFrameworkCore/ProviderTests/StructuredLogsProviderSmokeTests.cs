using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class StructuredLogsProviderModelTests
{
    [Theory]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer", "nvarchar(max)")]
    [InlineData("PostgreSql", "Npgsql.EntityFrameworkCore.PostgreSQL", "text")]
    [InlineData("MySql", "MySql.EntityFrameworkCore", "longtext")]
    public void Provider_models_build_without_connecting(string provider, string expectedProvider, string expectedPayloadType)
    {
        using var context = provider switch
        {
            "SqlServer" => (StructuredLogsDbContext)new StructuredLogsSqlServerDbContext(new DbContextOptionsBuilder<StructuredLogsSqlServerDbContext>()
                .UseSqlServer("Server=localhost;Database=unused;User ID=unused;Password=Unused123!;TrustServerCertificate=True").Options),
            "PostgreSql" => new StructuredLogsPostgreSqlDbContext(new DbContextOptionsBuilder<StructuredLogsPostgreSqlDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options),
            "MySql" => new StructuredLogsMySqlDbContext(new DbContextOptionsBuilder<StructuredLogsMySqlDbContext>()
                .UseMySQL("Server=localhost;Database=unused;User ID=unused;Password=unused").Options),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

        Assert.Equal(expectedProvider, context.Database.ProviderName);
        var entity = context.Model.FindEntityType(typeof(StructuredLogRecord));
        Assert.NotNull(entity?.FindPrimaryKey());
        Assert.True(context.Model.FindEntityType(typeof(StructuredLogStreamState))!
            .FindProperty(nameof(StructuredLogStreamState.Version))!.IsConcurrencyToken);
        Assert.Equal(expectedPayloadType,
            entity!.FindProperty(nameof(StructuredLogRecord.PayloadJson))!.GetColumnType(), ignoreCase: true);
    }
}

[Collection(StructuredLogsPostgreSqlFixture.CollectionName)]
public sealed class StructuredLogsPostgreSqlSmokeTests(StructuredLogsPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task Append_query_transaction_cas_and_trim_on_postgresql() =>
        StructuredLogsProviderSmoke.RunAsync(fixture, "PostgreSql");
}

[Collection(StructuredLogsSqlServerFixture.CollectionName)]
public sealed class StructuredLogsSqlServerSmokeTests(StructuredLogsSqlServerFixture fixture)
{
    [SkippableFact]
    public Task Append_query_transaction_cas_and_trim_on_sql_server() =>
        StructuredLogsProviderSmoke.RunAsync(fixture, "SqlServer");
}

[Collection(StructuredLogsMySqlFixture.CollectionName)]
public sealed class StructuredLogsMySqlSmokeTests(StructuredLogsMySqlFixture fixture)
{
    [SkippableFact]
    public Task Append_query_transaction_cas_and_trim_on_mysql() =>
        StructuredLogsProviderSmoke.RunAsync(fixture, "MySql");
}

internal static class StructuredLogsProviderSmoke
{
    public static async Task RunAsync(StructuredLogsProviderFixture fixture, string providerName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"{providerName} is unavailable.");
        var services = new ServiceCollection();
        services.AddOptions<StructuredLogsOptions>();
        new StructuredLogsEntityFrameworkCoreFeature
        {
            Provider = providerName,
            ConnectionString = fixture.ConnectionString
        }.ConfigureServices(services);

        await using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using (var scope = serviceProvider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>().Database.EnsureCreatedAsync();

        var store = serviceProvider.GetRequiredService<EfStructuredLogStore>();
        store.Start();
        var committed = await store.AppendAsync(new StructuredLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level = LogLevel.Warning,
            Category = "provider-smoke",
            SourceId = providerName,
            Message = "provider smoke"
        });
        Assert.NotNull(committed.ReplayCursor);
        Assert.Contains(
            await store.GetRecentAsync(new StructuredLogFilter { SourceId = providerName }),
            entry => entry.Message == committed.Message && entry.ReplayCursor == committed.ReplayCursor);

        await using (var first = serviceProvider.CreateAsyncScope())
        await using (var second = serviceProvider.CreateAsyncScope())
        {
            var state1 = await first.ServiceProvider.GetRequiredService<StructuredLogsDbContext>().StreamStates.SingleAsync();
            var state2 = await second.ServiceProvider.GetRequiredService<StructuredLogsDbContext>().StreamStates.SingleAsync();
            state1.HighWater++;
            state1.Version = Guid.NewGuid().ToString("N");
            await first.ServiceProvider.GetRequiredService<StructuredLogsDbContext>().SaveChangesAsync();
            state2.HighWater++;
            state2.Version = Guid.NewGuid().ToString("N");
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                second.ServiceProvider.GetRequiredService<StructuredLogsDbContext>().SaveChangesAsync());
        }

        await using (var rollbackScope = serviceProvider.CreateAsyncScope())
        {
            var rollbackDb = rollbackScope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
            var state = await rollbackDb.StreamStates.SingleAsync();
            await using var transaction = await rollbackDb.Database.BeginTransactionAsync();
            rollbackDb.Records.Add(new StructuredLogRecord
            {
                ScopeKey = state.ScopeKey,
                Position = 999_999,
                TenantId = state.TenantId,
                ScopeId = state.ScopeId,
                StreamId = state.StreamId,
                TimestampTicks = DateTimeOffset.UtcNow.Ticks,
                TimestampOffsetMinutes = 0,
                Level = (int)LogLevel.Information,
                CategoryKey = "rollback",
                SourceKey = "rollback",
                ReplayToken = "rollback-token",
                PayloadJson = "{}"
            });
            await rollbackDb.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var verifyRollbackScope = serviceProvider.CreateAsyncScope())
        {
            var verifyDb = verifyRollbackScope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
            Assert.False(await verifyDb.Records.AnyAsync(record => record.ReplayToken == "rollback-token"));
        }

        await using (var conflictScope = serviceProvider.CreateAsyncScope())
        {
            var conflictDb = conflictScope.ServiceProvider.GetRequiredService<StructuredLogsDbContext>();
            var operation = await conflictDb.AppendOperations.AsNoTracking().SingleAsync();
            conflictDb.AppendOperations.Add(new StructuredLogAppendOperation
            {
                ScopeKey = operation.ScopeKey,
                BatchId = operation.BatchId,
                TenantId = operation.TenantId,
                ScopeId = operation.ScopeId,
                StreamId = operation.StreamId,
                IssuedAtTicks = operation.IssuedAtTicks,
                Fingerprint = operation.Fingerprint,
                OutcomeJson = operation.OutcomeJson
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => conflictDb.SaveChangesAsync());
        }

        await store.TrimAsync(0);
        Assert.Equal(committed.Sequence + 1, await store.GetHighWaterMarkAsync());
        await store.StopAsync();
    }
}
