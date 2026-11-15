using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfDatabaseMigratorTests
{
    [Fact]
    public async Task AutoMigrate_creates_the_model_after_the_provider_guard()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        await EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite);
        Assert.False(await fixture.Context.Items.AnyAsync());
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
        await EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite);
        await EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite, EfMigratePolicy.Validate);
    }

    [Fact]
    public async Task Validate_fails_when_a_later_database_migration_is_pending()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        var migrations = fixture.Context.Database.GetMigrations().ToArray();
        Assert.Equal([EfTestMigrationIds.Initial, EfTestMigrationIds.AddDescription], migrations);

        await fixture.Context.Database.MigrateAsync(EfTestMigrationIds.Initial);
        var pending = (await fixture.Context.Database.GetPendingMigrationsAsync()).ToArray();
        Assert.Equal([EfTestMigrationIds.AddDescription], pending);

        var exception = await Assert.ThrowsAsync<EfPendingMigrationsException>(() =>
            EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite, EfMigratePolicy.Validate));
        Assert.Contains(EfTestMigrationIds.AddDescription, exception.Message, StringComparison.Ordinal);

        await EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite);
        await EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite, EfMigratePolicy.Validate);
        Assert.Empty(await fixture.Context.Database.GetPendingMigrationsAsync());
    }

    /// <summary>
    /// A genuine failure that happens to land on <see cref="InvalidOperationException"/> too — here, the
    /// provider guard refusing before <c>GetPendingMigrationsAsync</c> is ever reached — must not be
    /// mistaken for the dedicated pending-migrations signal: a caller classifying by type alone (as
    /// <c>EfToolingHost.Validate</c> used to) would otherwise report an unreachable or misconfigured
    /// database as "pending migrations" and point the operator at the wrong remedy.
    /// </summary>
    [Fact]
    public async Task Validate_does_not_report_a_provider_mismatch_as_a_pending_migration()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.PostgreSql, EfMigratePolicy.Validate));
        Assert.IsNotType<EfPendingMigrationsException>(exception);
    }

    [Fact]
    public async Task Invalid_policy_is_rejected_after_the_provider_guard()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite, (EfMigratePolicy)99));
        Assert.Equal("policy", exception.ParamName);
    }

    /// <summary>
    /// A process killed during a migration leaves EF's SQLite lock row behind, and EF waits for it for ever. With nothing
    /// pending there is nothing to lock for, so a start goes on past it (#2196).
    /// </summary>
    [Fact]
    public async Task AutoMigrate_goes_on_past_a_stale_lock_when_no_migration_is_pending()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        await EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite);
        await SqliteMigrationLockRow.TakeAsync(fixture.Context, DateTimeOffset.UtcNow.AddHours(-1));

        await EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(await SqliteMigrationLockRow.IsHeldAsync(fixture.Context), "A lock Elsa cannot prove dead is not removed.");
    }

    /// <summary>With migrations pending the lock is needed, so a stale one fails the start with the way to clear it instead of hanging it, and applies nothing.</summary>
    [Fact]
    public async Task AutoMigrate_fails_fast_on_a_stale_lock_when_a_migration_is_pending()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync(EfTestMigrationIds.Initial);
        await SqliteMigrationLockRow.TakeAsync(fixture.Context, DateTimeOffset.UtcNow.AddHours(-1));

        var exception = await Assert.ThrowsAsync<EfMigrationLockStaleException>(() =>
            EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite).WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.Contains("DELETE FROM \"__EFMigrationsLock\" WHERE \"Id\" = 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains(fixture.DataSource, exception.Message, StringComparison.Ordinal);
        Assert.Equal([EfTestMigrationIds.AddDescription], await fixture.Context.Database.GetPendingMigrationsAsync());
        Assert.True(await SqliteMigrationLockRow.IsHeldAsync(fixture.Context));
    }

    /// <summary>
    /// A lock younger than the bound may be a live migrator's, so the start waits for it as EF does and migrates once it is
    /// released: two migrators of one file still run one at a time.
    /// </summary>
    [Fact]
    public async Task AutoMigrate_waits_for_a_lock_younger_than_the_bound_and_then_migrates()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync(EfTestMigrationIds.Initial);
        await SqliteMigrationLockRow.TakeAsync(fixture.Context, DateTimeOffset.UtcNow);
        await using var waiter = fixture.NewContext();

        var applying = EfDatabaseMigrator.ApplyAsync(waiter, EfProviderNames.Sqlite);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(applying.IsCompleted);
        Assert.Equal([EfTestMigrationIds.AddDescription], await fixture.Context.Database.GetPendingMigrationsAsync());

        await SqliteMigrationLockRow.ReleaseAsync(fixture.Context);
        await applying.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Empty(await fixture.Context.Database.GetPendingMigrationsAsync());
    }

    /// <summary>The bound is the host's: a lock older than its options say is reported.</summary>
    [Fact]
    public async Task AutoMigrate_reports_a_lock_older_than_the_given_bound()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync(EfTestMigrationIds.Initial);
        await SqliteMigrationLockRow.TakeAsync(fixture.Context, DateTimeOffset.UtcNow.AddMinutes(-2));

        var exception = await Assert.ThrowsAsync<EfMigrationLockStaleException>(() =>
            EfDatabaseMigrator.ApplyAsync(
                fixture.Context, EfProviderNames.Sqlite, new EfMigrateOptions { SqliteMigrationLockStaleAfter = TimeSpan.FromMinutes(1) })
                .WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.Contains("for longer than 00:01:00", exception.Message, StringComparison.Ordinal);
    }

    private sealed class SqliteMigratorFixture : IAsyncDisposable
    {
        private readonly TemporarySqliteDatabase database;

        private SqliteMigratorFixture(TemporarySqliteDatabase database, MigratorContext context)
        {
            this.database = database;
            Context = context;
        }

        public MigratorContext Context { get; }

        public string DataSource => database.Path;

        /// <summary>Another context on the same file, as a second migrator would hold.</summary>
        public MigratorContext NewContext() => new(Options(database));

        public static ValueTask<SqliteMigratorFixture> CreateAsync()
        {
            var database = new TemporarySqliteDatabase("ef-migrate");
            return ValueTask.FromResult(new SqliteMigratorFixture(database, new MigratorContext(Options(database))));
        }

        private static DbContextOptions<MigratorContext> Options(TemporarySqliteDatabase database) =>
            new DbContextOptionsBuilder<MigratorContext>()
                .UseSqlite(database.ConnectionString, sqlite => sqlite
                    .MigrationsAssembly(typeof(EfDatabaseMigratorTests).Assembly.GetName().Name))
                .Options;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await database.DisposeAsync();
        }
    }

    internal sealed class MigratorContext(DbContextOptions<MigratorContext> options) : DbContext(options)
    {
        public DbSet<MigratorItem> Items => Set<MigratorItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<MigratorItem>().ToTable("Items");
    }

    public sealed class MigratorItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
    }
}

internal static class EfTestMigrationIds
{
    public const string Initial = "20260911000000_Initial";
    public const string AddDescription = "20260911000001_AddDescription";
}

[DbContext(typeof(EfDatabaseMigratorTests.MigratorContext))]
[Migration(EfTestMigrationIds.Initial)]
public sealed class EfTestInitialMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Items",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Items", x => x.Id));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("Items");
}

[DbContext(typeof(EfDatabaseMigratorTests.MigratorContext))]
[Migration(EfTestMigrationIds.AddDescription)]
public sealed class EfTestAddDescriptionMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "Description",
            table: "Items",
            type: "TEXT",
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Description", table: "Items");
}
