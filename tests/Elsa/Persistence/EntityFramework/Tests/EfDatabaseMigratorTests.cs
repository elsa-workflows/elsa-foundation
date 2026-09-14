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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite, EfMigratePolicy.Validate));
        Assert.Contains(EfTestMigrationIds.AddDescription, exception.Message, StringComparison.Ordinal);

        await EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite);
        await EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite, EfMigratePolicy.Validate);
        Assert.Empty(await fixture.Context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Invalid_policy_is_rejected_after_the_provider_guard()
    {
        await using var fixture = await SqliteMigratorFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            EfDatabaseMigrator.ApplyAsync(fixture.Context, EfProviderNames.Sqlite, (EfMigratePolicy)99));
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
            var path = Path.Join(Path.GetTempPath(), $"elsa-ef-migrate-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<MigratorContext>()
                .UseSqlite($"Data Source={path}", sqlite => sqlite
                    .MigrationsAssembly(typeof(EfDatabaseMigratorTests).Assembly.GetName().Name))
                .Options;
            var context = new MigratorContext(options);
            return new SqliteMigratorFixture(path, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            File.Delete(path);
            File.Delete($"{path}-wal");
            File.Delete($"{path}-shm");
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
