using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.PostCommitOutboxClaimedListingContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="PostCommitOutboxClaimedListingContract"/> on the in-memory checkpoint store and on the EF Core outbox store
/// over SQLite. The native-provider smoke test runs it on PostgreSQL.
/// </summary>
public sealed class PostCommitOutboxClaimedListingContractTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private RuntimeSqliteDbContext? _context;

    [Fact]
    public Task The_in_memory_store_lists_one_executions_claimed_items_of_one_kind()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        return ListsOneExecutionsClaimedItemsOfOneKindAsync(new Backend(store, item => store.AddPendingForTestingAsync(item)));
    }

    [Fact]
    public async Task The_entity_framework_store_lists_one_executions_claimed_items_of_one_kind()
    {
        await _connection.OpenAsync();
        _context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(_connection).Options);
        await _context.Database.EnsureCreatedAsync();
        await ListsOneExecutionsClaimedItemsOfOneKindAsync(PostCommitOutboxClaimedListingContract.EntityFramework(_context, "tenant-a"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
