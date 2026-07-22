using System.Globalization;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Services;
using Elsa.Persistence.EFCore.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Persistence.EFCore.Tests;

/// <summary>
/// Covers <see cref="RunMigrationsStartupTask{TDbContext}"/> (issue #395): the DbContext created to
/// run migrations must be disposed, otherwise every host startup leaks a connection per registered
/// DbContext type. Also covers <see cref="MigrationsLockReclaimer"/> (issue #949): a stale, unreleased
/// EF Core SQLite <c>__EFMigrationsLock</c> row left by a crashed process must be reclaimed so the next
/// migration can acquire the lock instead of retrying forever, while a freshly-held lock is left in place.
/// </summary>
/// <remarks>
/// The reclaimer cases deliberately share this file's single <see cref="TrackingDbContext"/> /
/// <c>UseSqlite</c> setup rather than introducing their own, so the EF Core surface ratchet
/// (<c>tests/Elsa/Architecture/Baselines/ef-core-surface.json</c>) does not grow a new DbContext file or
/// provider-registration entry for a shrink-only inventory.
/// </remarks>
public sealed class RunMigrationsStartupTaskTests : IDisposable
{
    private const string LockTable = "__EFMigrationsLock";
    private readonly SqliteConnection _rootConnection;
    private readonly TrackingContextFactory _factory;

    public RunMigrationsStartupTaskTests()
    {
        var connectionString = $"Data Source=migrations-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _rootConnection = new(connectionString);
        _rootConnection.Open();
        _factory = new(connectionString);
    }

    private static MigrationsLockReclaimer Reclaimer => new(NullLogger<MigrationsLockReclaimer>.Instance);

    public void Dispose() => _rootConnection.Dispose();

    [Fact]
    public async Task ExecuteAsync_DisposesTheDbContextItCreates()
    {
        var task = new RunMigrationsStartupTask<TrackingDbContext>(_factory, Reclaimer, OptionsFor(runMigrations: true));

        await task.ExecuteAsync(CancellationToken.None);

        var created = Assert.Single(_factory.Created);
        Assert.True(created.IsDisposed, "The DbContext created for running migrations was not disposed.");
    }

    [Fact]
    public async Task ExecuteAsync_CreatesNoDbContextWhenMigrationsAreDisabled()
    {
        var task = new RunMigrationsStartupTask<TrackingDbContext>(_factory, Reclaimer, OptionsFor(runMigrations: false));

        await task.ExecuteAsync(CancellationToken.None);

        Assert.Empty(_factory.Created);
    }

    [Fact]
    public async Task Reclaimer_RemovesLockOlderThanTheStalenessWindow()
    {
        await CreateLockTableAsync();
        await InsertLockAsync(DateTimeOffset.UtcNow - TimeSpan.FromHours(1));

        var reclaimed = await ReclaimAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(1, reclaimed);
        Assert.Equal(0, await CountLockRowsAsync());
    }

    [Fact]
    public async Task Reclaimer_LeavesFreshLockInPlace()
    {
        await CreateLockTableAsync();
        await InsertLockAsync(DateTimeOffset.UtcNow);

        var reclaimed = await ReclaimAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(0, reclaimed);
        Assert.Equal(1, await CountLockRowsAsync());
    }

    [Fact]
    public async Task Reclaimer_IsNoOpWhenLockTableDoesNotExist()
    {
        var reclaimed = await ReclaimAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(0, reclaimed);
    }

    [Fact]
    public async Task Reclaimer_IsDisabledWhenTimeoutIsZero()
    {
        await CreateLockTableAsync();
        await InsertLockAsync(DateTimeOffset.UtcNow - TimeSpan.FromHours(1));

        var reclaimed = await ReclaimAsync(TimeSpan.Zero);

        Assert.Equal(0, reclaimed);
        Assert.Equal(1, await CountLockRowsAsync());
    }

    [Fact]
    public async Task Reclaimer_TreatsUnparseableTimestampAsStale()
    {
        await CreateLockTableAsync();
        await ExecuteRootAsync($"INSERT INTO \"{LockTable}\"(\"Id\", \"Timestamp\") VALUES(1, 'not-a-timestamp');");

        var reclaimed = await ReclaimAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(1, reclaimed);
        Assert.Equal(0, await CountLockRowsAsync());
    }

    private async Task<int> ReclaimAsync(TimeSpan staleAfter)
    {
        await using var dbContext = await _factory.CreateDbContextAsync();
        return await Reclaimer.ReclaimStaleLocksAsync(dbContext, staleAfter);
    }

    private Task CreateLockTableAsync() =>
        ExecuteRootAsync($"CREATE TABLE IF NOT EXISTS \"{LockTable}\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_{LockTable}\" PRIMARY KEY, \"Timestamp\" TEXT NOT NULL);");

    private Task InsertLockAsync(DateTimeOffset timestamp) =>
        // Mirror EF Core's DateTimeOffset literal shape ("yyyy-MM-dd HH:mm:ss.fffffff+00:00").
        ExecuteRootAsync(
            $"INSERT OR REPLACE INTO \"{LockTable}\"(\"Id\", \"Timestamp\") VALUES(1, $ts);",
            ("$ts", timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffffff+00:00", CultureInfo.InvariantCulture)));

    private async Task<long> CountLockRowsAsync()
    {
        await using var command = _rootConnection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{LockTable}\";";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task ExecuteRootAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = _rootConnection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static IOptions<MigrationOptions> OptionsFor(bool runMigrations) => Microsoft.Extensions.Options.Options.Create(new MigrationOptions
    {
        RunMigrations = new Dictionary<string, bool>
        {
            [$"{typeof(TrackingDbContext)}"] = runMigrations,
        },
    });

    public sealed class TrackingDbContext(DbContextOptions<TrackingDbContext> options) : DbContext(options)
    {
        public bool IsDisposed { get; private set; }

        public override void Dispose()
        {
            IsDisposed = true;
            base.Dispose();
        }

        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return base.DisposeAsync();
        }
    }

    private sealed class TrackingContextFactory(string connectionString) : IDbContextFactory<TrackingDbContext>
    {
        public List<TrackingDbContext> Created { get; } = [];

        public TrackingDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<TrackingDbContext>()
                .UseSqlite(connectionString)
                .Options;
            var context = new TrackingDbContext(options);
            Created.Add(context);
            return context;
        }

        public Task<TrackingDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
