using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Services;
using Elsa.Primitives.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.EFCore.Tests;

/// <summary>
/// Covers <see cref="EFCoreSaveCommand{TDbContext,TEntity}"/> (issue #394): saves of the same entity
/// type must not be serialized process-wide, and the insert-vs-update decision must keep keying on
/// <see cref="Entity.Id"/> after the per-call expression compilation was removed from
/// <see cref="EntityExtensions.BuildEqualsExpression{TEntity}"/>.
/// </summary>
public sealed class EFCoreSaveCommandTests : IDisposable
{
    private readonly GatedSqliteContextFactory _factory = GatedSqliteContextFactory.Create();
    private readonly EFCoreSaveCommand<TestDbContext, TestEntity> _command;

    public EFCoreSaveCommandTests()
    {
        _command = new(_factory, new ServiceCollection().BuildServiceProvider());
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task SaveAsync_DoesNotSerializeSavesOfDifferentEntitiesProcessWide()
    {
        // Deterministic interleaving: the factory holds every CreateDbContextAsync call open until two
        // callers have arrived. CreateDbContextAsync sits inside SaveAsync's critical section, so with
        // the former type-wide semaphore the second caller can never arrive while the first is held —
        // the two-arrivals task only completes once saves are allowed to overlap.
        var arrivals = 0;
        var bothArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _factory.OnCreateAsync = async () =>
        {
            if (Interlocked.Increment(ref arrivals) == 2)
                bothArrived.TrySetResult();

            await Task.WhenAny(bothArrived.Task, release.Task);
        };

        var save1 = Task.Run(() => _command.SaveAsync(new TestEntity { Id = "a", Name = "one" }));
        var save2 = Task.Run(() => _command.SaveAsync(new TestEntity { Id = "b", Name = "two" }));

        // The failing (serialized) case is detected by a bounded timeout; the passing case completes
        // as soon as both callers arrive and never waits out the delay.
        var winner = await Task.WhenAny(bothArrived.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        // Unblock everything regardless of outcome so the test never hangs.
        release.TrySetResult();
        await Task.WhenAll(save1, save2);

        Assert.Same(bothArrived.Task, winner);
        Assert.Equal(2, arrivals);
    }

    [Fact]
    public async Task SaveAsync_InsertsNewAndUpdatesExistingById()
    {
        await _command.SaveAsync(new TestEntity { Id = "a", Name = "first" });
        await _command.SaveAsync(new TestEntity { Id = "a", Name = "second" });
        await _command.SaveAsync(new TestEntity { Id = "b", Name = "other" });

        using var db = _factory.CreateDbContext();
        var rows = db.Entities.AsNoTracking().OrderBy(x => x.Id).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b"], rows.Select(x => x.Id));
        Assert.Equal("second", rows[0].Name);
        Assert.Equal("other", rows[1].Name);
    }

    [Fact]
    public void BuildEqualsExpression_MatchesOnlyTheEntityKey()
    {
        var entity = new TestEntity { Id = "x" };

        var predicate = entity.BuildEqualsExpression(e => e.Id).Compile();

        Assert.True(predicate(new TestEntity { Id = "x" }));
        Assert.False(predicate(new TestEntity { Id = "y" }));
    }

    public sealed class TestEntity : Entity
    {
        public string Name { get; set; } = string.Empty;
    }

    public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> Entities => Set<TestEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Ignore(x => x.RowNumber);
            });
        }
    }

    /// <summary>
    /// Shared in-memory SQLite factory (root connection held open) with an optional async gate invoked
    /// on every <see cref="CreateDbContextAsync"/> so tests can observe and control interleavings.
    /// </summary>
    private sealed class GatedSqliteContextFactory : IDbContextFactory<TestDbContext>, IDisposable
    {
        private readonly SqliteConnection _rootConnection;
        private readonly string _connectionString;

        private GatedSqliteContextFactory(SqliteConnection rootConnection, string connectionString)
        {
            _rootConnection = rootConnection;
            _connectionString = connectionString;
        }

        public Func<Task>? OnCreateAsync { get; set; }

        public static GatedSqliteContextFactory Create()
        {
            var connectionString = $"Data Source=save-cmd-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            connection.Open();

            var factory = new GatedSqliteContextFactory(connection, connectionString);
            using var context = factory.CreateDbContext();
            context.Database.EnsureCreated();
            return factory;
        }

        public TestDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(_connectionString)
                .Options;
            return new(options);
        }

        public async Task<TestDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            if (OnCreateAsync is { } gate)
                await gate();

            return CreateDbContext();
        }

        public void Dispose() => _rootConnection.Dispose();
    }
}
