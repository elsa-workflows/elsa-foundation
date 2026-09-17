using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// A guard that never fires looks exactly like a codebase with no unordered row limits, so both directions are pinned.
/// </summary>
public sealed class UnorderedRowLimitGuardTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly RowContext context;

    public UnorderedRowLimitGuardTests()
    {
        connection.Open();
        context = new(new DbContextOptionsBuilder<RowContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        context.Rows.AddRange(new Row { Id = "b" }, new Row { Id = "a" }, new Row { Id = "c" });
        context.SaveChanges();
    }

    [Fact]
    public async Task A_row_limit_without_an_order_fails_the_query()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Rows.Take(2).ToListAsync());

        Assert.Contains(CoreEventId.RowLimitingOperationWithoutOrderByWarning.Name!, exception.Message);
    }

    [Fact]
    public async Task An_offset_without_an_order_fails_the_query()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Rows.Skip(1).ToListAsync());
    }

    [Fact]
    public async Task An_ordered_row_limit_runs()
    {
        var rows = await context.Rows.OrderBy(row => row.Id).Skip(1).Take(2).ToListAsync();

        Assert.Equal(["b", "c"], rows.Select(row => row.Id));
    }

    public async ValueTask DisposeAsync()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }

    private sealed class RowContext(DbContextOptions<RowContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public string Id { get; set; } = null!;
    }
}
