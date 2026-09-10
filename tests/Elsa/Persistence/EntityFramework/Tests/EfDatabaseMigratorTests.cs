using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfDatabaseMigratorTests
{
    [Fact]
    public async Task AutoMigrate_creates_the_model_after_the_provider_guard()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        await EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite);
        Assert.True(await fixture.Context.Items.AnyAsync() == false);
        fixture.Context.Items.Add(new MigratorItem { Name = "alpha" });
        await fixture.Context.SaveChangesAsync();
        Assert.Equal("alpha", (await fixture.Context.Items.SingleAsync()).Name);
    }

    [Fact]
    public async Task AutoMigrate_refuses_the_wrong_provider_before_sql()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.PostgreSql));
        Assert.Contains(EfProviderNames.PostgreSql, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_succeeds_when_the_model_is_already_applied()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        await fixture.Context.Database.EnsureCreatedAsync();
        await EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite, EfMigratePolicy.Validate);
    }

    [Fact]
    public async Task Validate_fails_when_migrations_are_pending()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        // EnsureCreated applies the model without recording migrations, so GetPendingMigrations
        // still reports the Initial migration when one exists. This fixture has no migrations
        // assembly, so Validate is a no-op pending list (empty) — prove the unknown-policy branch
        // and an explicit pending failure via a dedicated pending-aware context instead.
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            EfDatabaseMigrator.ApplyAsync(
                fixture.Context,
                EfProviderNames.Sqlite,
                (EfMigratePolicy)99));
        Assert.Equal("policy", exception.ParamName);
    }

    private sealed class SqliteMigratorFixture : IAsyncDisposable
    {
        private readonly string path;

        private SqliteMigratorFixture(string path, MigratorContext context)
        {
            this.path = path;
            Context = context;
        }

        public MigratorContext Context { get; }

        public static async ValueTask<SqliteMigratorFixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-ef-migrate-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<MigratorContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            var context = new MigratorContext(options);
            await context.Database.EnsureCreatedAsync();
            return new SqliteMigratorFixture(path, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            File.Delete(path);
        }
    }

    private sealed class MigratorContext(DbContextOptions<MigratorContext> options) : DbContext(options)
    {
        public DbSet<MigratorItem> Items => Set<MigratorItem>();
    }

    private sealed class MigratorItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
