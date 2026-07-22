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
/// DbContext type.
/// </summary>
public sealed class RunMigrationsStartupTaskTests : IDisposable
{
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
