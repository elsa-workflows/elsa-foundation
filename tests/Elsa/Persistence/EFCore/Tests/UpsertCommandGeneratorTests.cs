using Elsa.Persistence.EFCore.Services;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EFCore.Tests;

/// <summary>
/// Golden-output guard for <see cref="UpsertCommandGenerator"/> (issue #415 item 2). The five
/// provider-specific <c>Generate*Upsert</c> methods share identical column/parameter-extraction logic and
/// differ only in SQL dialect; this test freezes the exact emitted SQL and parameter sequence for the
/// Sqlite dialect (the only provider available in this test project) so the follow-up DRY refactor —
/// extracting the shared shape/row-extraction helpers into the sealed class — stays byte-for-byte
/// behavior-preserving. Mutating a shared helper (column order, parameter numbering, dialect syntax)
/// flips this red.
/// </summary>
public sealed class UpsertCommandGeneratorTests : IDisposable
{
    // EF model property order for the entity below: Id (key), then the Entity base columns and the
    // declared columns as EF orders them — Count, CreatedAt, LastModifiedAt, Name. Frozen here so the
    // refactor cannot silently reorder columns or renumber parameters.
    private const string TwoRowSql =
        "INSERT INTO \"TestEntities\" (\"Id\", \"Count\", \"CreatedAt\", \"LastModifiedAt\", \"Name\") VALUES ({0}, {1}, {2}, {3}, {4}), ({5}, {6}, {7}, {8}, {9})\n" +
        "ON CONFLICT(\"Id\") DO UPDATE SET\n" +
        "\"Count\"=excluded.\"Count\", \"CreatedAt\"=excluded.\"CreatedAt\", \"LastModifiedAt\"=excluded.\"LastModifiedAt\", \"Name\"=excluded.\"Name\";\n";

    private const string SingleRowSql =
        "INSERT INTO \"TestEntities\" (\"Id\", \"Count\", \"CreatedAt\", \"LastModifiedAt\", \"Name\") VALUES ({0}, {1}, {2}, {3}, {4})\n" +
        "ON CONFLICT(\"Id\") DO UPDATE SET\n" +
        "\"Count\"=excluded.\"Count\", \"CreatedAt\"=excluded.\"CreatedAt\", \"LastModifiedAt\"=excluded.\"LastModifiedAt\", \"Name\"=excluded.\"Name\";\n";

    private readonly TestDbContext _dbContext = TestDbContext.Create();
    private readonly UpsertCommandGenerator _generator = new();

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public void Generate_Sqlite_TwoEntities_EmitsFrozenSqlAndParameters()
    {
        var created = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var modified = new DateTimeOffset(2024, 6, 7, 8, 9, 10, TimeSpan.Zero);
        var entities = new List<TestEntity>
        {
            new() { Id = "a", Name = "one", Count = 1, CreatedAt = created, LastModifiedAt = modified },
            new() { Id = "b", Name = "two", Count = 2, CreatedAt = created, LastModifiedAt = modified }
        };

        var command = _generator.Generate(_dbContext, entities, e => e.Id);

        Assert.Equal(NormalizeNewlines(TwoRowSql), NormalizeNewlines(command.Command));
        Assert.Equal(
            new object[] { "a", 1, created, modified, "one", "b", 2, created, modified, "two" },
            command.Parameters);
    }

    [Fact]
    public void Generate_Sqlite_SingleEntity_EmitsFrozenSqlAndParameters()
    {
        var created = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var modified = new DateTimeOffset(2024, 6, 7, 8, 9, 10, TimeSpan.Zero);

        var command = _generator.Generate(
            _dbContext,
            new List<TestEntity> { new() { Id = "x", Name = "only", Count = 7, CreatedAt = created, LastModifiedAt = modified } },
            e => e.Id);

        Assert.Equal(NormalizeNewlines(SingleRowSql), NormalizeNewlines(command.Command));
        Assert.Equal(new object[] { "x", 7, created, modified, "only" }, command.Parameters);
    }

    // StringBuilder.AppendLine emits Environment.NewLine, so normalize to keep the golden strings
    // platform-independent (the dialect syntax, column order and parameter numbering are what we freeze).
    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n");

    public sealed class TestEntity : Entity
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> Entities => Set<TestEntity>();

        public static TestDbContext Create()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options;
            return new(options);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>(entity =>
            {
                entity.ToTable("TestEntities");
                entity.HasKey(x => x.Id);
                entity.Ignore(x => x.RowNumber);
            });
        }
    }
}
