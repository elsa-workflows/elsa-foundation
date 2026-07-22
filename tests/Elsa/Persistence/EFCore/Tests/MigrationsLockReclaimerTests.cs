using System.Globalization;
using Elsa.Persistence.EFCore.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Persistence.EFCore.Tests;

/// <summary>
/// Covers <see cref="MigrationsLockReclaimer"/> (issue #949): a stale, unreleased EF Core SQLite
/// <c>__EFMigrationsLock</c> row left by a crashed process must be reclaimed so the next migration can
/// acquire the lock instead of retrying forever, while a freshly-held lock is left untouched.
/// </summary>
public sealed class MigrationsLockReclaimerTests : IAsyncLifetime
{
    private const string LockTable = "__EFMigrationsLock";
    private readonly string _connectionString = $"Data Source=reclaim-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection _rootConnection = null!;

    public async Task InitializeAsync()
    {
        // Keep the in-memory shared-cache database alive for the duration of the test.
        _rootConnection = new(_connectionString);
        await _rootConnection.OpenAsync();
    }

    public async Task DisposeAsync() => await _rootConnection.DisposeAsync();

    [Fact]
    public async Task ReclaimsLockOlderThanTheStalenessWindow()
    {
        await CreateLockTableAsync();
        await InsertLockAsync(DateTimeOffset.UtcNow - TimeSpan.FromHours(1));

        var reclaimed = await ReclaimAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(1, reclaimed);
        Assert.Equal(0, await CountLockRowsAsync());
    }

    [Fact]
    public async Task LeavesFreshLockInPlace()
    {
        await CreateLockTableAsync();
        await InsertLockAsync(DateTimeOffset.UtcNow);

        var reclaimed = await ReclaimAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(0, reclaimed);
        Assert.Equal(1, await CountLockRowsAsync());
    }

    [Fact]
    public async Task IsNoOpWhenLockTableDoesNotExist()
    {
        var reclaimed = await ReclaimAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(0, reclaimed);
    }

    [Fact]
    public async Task IsDisabledWhenTimeoutIsZero()
    {
        await CreateLockTableAsync();
        await InsertLockAsync(DateTimeOffset.UtcNow - TimeSpan.FromHours(1));

        var reclaimed = await ReclaimAsync(TimeSpan.Zero);

        Assert.Equal(0, reclaimed);
        Assert.Equal(1, await CountLockRowsAsync());
    }

    [Fact]
    public async Task TreatsUnparseableTimestampAsStale()
    {
        await CreateLockTableAsync();
        await using (var command = _rootConnection.CreateCommand())
        {
            command.CommandText = $"INSERT INTO \"{LockTable}\"(\"Id\", \"Timestamp\") VALUES(1, 'not-a-timestamp');";
            await command.ExecuteNonQueryAsync();
        }

        var reclaimed = await ReclaimAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(1, reclaimed);
        Assert.Equal(0, await CountLockRowsAsync());
    }

    private async Task<int> ReclaimAsync(TimeSpan staleAfter)
    {
        var reclaimer = new MigrationsLockReclaimer(NullLogger<MigrationsLockReclaimer>.Instance);
        await using var dbContext = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connectionString).Options);
        return await reclaimer.ReclaimStaleLocksAsync(dbContext, staleAfter);
    }

    private async Task CreateLockTableAsync()
    {
        await using var command = _rootConnection.CreateCommand();
        command.CommandText = $"CREATE TABLE IF NOT EXISTS \"{LockTable}\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_{LockTable}\" PRIMARY KEY, \"Timestamp\" TEXT NOT NULL);";
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertLockAsync(DateTimeOffset timestamp)
    {
        await using var command = _rootConnection.CreateCommand();
        // Mirror EF Core's DateTimeOffset literal shape ("yyyy-MM-dd HH:mm:ss.fffffff+00:00").
        command.CommandText = $"INSERT OR REPLACE INTO \"{LockTable}\"(\"Id\", \"Timestamp\") VALUES(1, $ts);";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$ts";
        parameter.Value = timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffffff+00:00", CultureInfo.InvariantCulture);
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountLockRowsAsync()
    {
        await using var command = _rootConnection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{LockTable}\";";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
}
