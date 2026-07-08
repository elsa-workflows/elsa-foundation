using Elsa.Persistence.EFCore.Services;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EFCore.Tests;

/// <summary>
/// Golden-output guard for the Postgres dialect of <see cref="UpsertCommandGenerator"/> (issue #514,
/// follow-up to #415 item 2). Freezes the exact <c>INSERT … ON CONFLICT</c> SQL and parameter sequence so a
/// change to the shared <c>ResolveEntityShape</c>/<c>ExtractRowValues</c> helpers that breaks this dialect
/// flips red. The model is built fully offline against a bogus connection string — SQL generation reads only
/// model metadata and type mappings, never a live database.
/// <para>
/// This dialect exercises the Postgres-specific placeholder branch: properties whose column type is
/// <c>json</c> / <c>jsonb</c> have their parameter token wrapped in <c>CAST({n} AS json)</c> /
/// <c>CAST({n} AS jsonb)</c>. Both fragments are frozen below.
/// </para>
/// </summary>
public sealed class UpsertCommandGeneratorPostgresTests : IDisposable
{
    // EF model column order for the entity below: Id (key), then the base + declared columns as EF orders
    // them — Count, CreatedAt, JsonDoc, JsonbDoc, LastModifiedAt, Name. Frozen so the shared helpers cannot
    // silently reorder columns or renumber parameters.
    private const string TwoRowSql =
        "INSERT INTO \"public\".\"TestEntities\" (\"Id\", \"Count\", \"CreatedAt\", \"JsonDoc\", \"JsonbDoc\", \"LastModifiedAt\", \"Name\") VALUES ({0}, {1}, {2}, CAST({3} AS json), CAST({4} AS jsonb), {5}, {6}), ({7}, {8}, {9}, CAST({10} AS json), CAST({11} AS jsonb), {12}, {13})\n" +
        "ON CONFLICT (\"Id\") DO UPDATE SET\n" +
        "\"Count\" = EXCLUDED.\"Count\", \"CreatedAt\" = EXCLUDED.\"CreatedAt\", \"JsonDoc\" = EXCLUDED.\"JsonDoc\", \"JsonbDoc\" = EXCLUDED.\"JsonbDoc\", \"LastModifiedAt\" = EXCLUDED.\"LastModifiedAt\", \"Name\" = EXCLUDED.\"Name\";\n";

    private const string SingleRowSql =
        "INSERT INTO \"public\".\"TestEntities\" (\"Id\", \"Count\", \"CreatedAt\", \"JsonDoc\", \"JsonbDoc\", \"LastModifiedAt\", \"Name\") VALUES ({0}, {1}, {2}, CAST({3} AS json), CAST({4} AS jsonb), {5}, {6})\n" +
        "ON CONFLICT (\"Id\") DO UPDATE SET\n" +
        "\"Count\" = EXCLUDED.\"Count\", \"CreatedAt\" = EXCLUDED.\"CreatedAt\", \"JsonDoc\" = EXCLUDED.\"JsonDoc\", \"JsonbDoc\" = EXCLUDED.\"JsonbDoc\", \"LastModifiedAt\" = EXCLUDED.\"LastModifiedAt\", \"Name\" = EXCLUDED.\"Name\";\n";

    private readonly TestDbContext _dbContext = TestDbContext.Create();
    private readonly UpsertCommandGenerator _generator = new();

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public void Generate_Postgres_TwoEntities_EmitsFrozenSqlAndParameters()
    {
        var created = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var modified = new DateTimeOffset(2024, 6, 7, 8, 9, 10, TimeSpan.Zero);
        var entities = new List<TestEntity>
        {
            new() { Id = "a", Name = "one", Count = 1, CreatedAt = created, LastModifiedAt = modified, JsonDoc = "{\"a\":1}", JsonbDoc = "{\"b\":2}" },
            new() { Id = "b", Name = "two", Count = 2, CreatedAt = created, LastModifiedAt = modified, JsonDoc = "{\"c\":3}", JsonbDoc = "{\"d\":4}" }
        };

        var command = _generator.Generate(_dbContext, entities, e => e.Id);

        Assert.Equal(NormalizeNewlines(TwoRowSql), NormalizeNewlines(command.Command));
        Assert.Equal(
            new object[]
            {
                "a", 1, created, "{\"a\":1}", "{\"b\":2}", modified, "one",
                "b", 2, created, "{\"c\":3}", "{\"d\":4}", modified, "two"
            },
            command.Parameters);
    }

    [Fact]
    public void Generate_Postgres_SingleEntity_EmitsFrozenSqlAndParameters()
    {
        var created = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var modified = new DateTimeOffset(2024, 6, 7, 8, 9, 10, TimeSpan.Zero);

        var command = _generator.Generate(
            _dbContext,
            new List<TestEntity> { new() { Id = "x", Name = "only", Count = 7, CreatedAt = created, LastModifiedAt = modified, JsonDoc = "{\"a\":1}", JsonbDoc = "{\"b\":2}" } },
            e => e.Id);

        Assert.Equal(NormalizeNewlines(SingleRowSql), NormalizeNewlines(command.Command));
        Assert.Equal(
            new object[] { "x", 7, created, "{\"a\":1}", "{\"b\":2}", modified, "only" },
            command.Parameters);
    }

    // StringBuilder.AppendLine emits Environment.NewLine, so normalize to keep the golden strings
    // platform-independent (the dialect syntax, column order and parameter numbering are what we freeze).
    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n");

    public sealed class TestEntity : Entity
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public string JsonDoc { get; set; } = string.Empty;
        public string JsonbDoc { get; set; } = string.Empty;
    }

    public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> Entities => Set<TestEntity>();

        // Bogus connection string: the model builds offline and SQL generation never connects.
        public static TestDbContext Create()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql("Host=localhost;Database=elsa;Username=elsa;Password=elsa")
                .Options;
            return new(options);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Pin the schema so the emitted "schema"."table" is deterministic across environments.
            modelBuilder.HasDefaultSchema("public");
            modelBuilder.Entity<TestEntity>(entity =>
            {
                entity.ToTable("TestEntities");
                entity.HasKey(x => x.Id);
                entity.Ignore(x => x.RowNumber);
                entity.Property(x => x.JsonDoc).HasColumnType("json");
                entity.Property(x => x.JsonbDoc).HasColumnType("jsonb");
            });
        }
    }
}
