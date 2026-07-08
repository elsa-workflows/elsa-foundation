using Elsa.Persistence.EFCore.Services;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EFCore.Tests;

/// <summary>
/// Golden-output guard for the SqlServer dialect of <see cref="UpsertCommandGenerator"/> (issue #514,
/// follow-up to #415 item 2). Freezes the exact <c>MERGE</c> SQL and parameter sequence so a change to the
/// shared <c>ResolveEntityShape</c>/<c>ExtractRowValues</c> helpers that breaks this dialect flips red.
/// The model is built fully offline against a bogus connection string — SQL generation reads only model
/// metadata and type mappings, never a live database.
/// <para>
/// This dialect exercises the SqlServer-specific placeholder branch: a <c>byte[]</c> property maps to
/// <c>varbinary(max)</c>, and when its value is <c>null</c> the generator emits the literal
/// <c>CAST(NULL AS varbinary(max))</c> in place of a parameter token — while STILL appending the null value
/// to the parameter sequence. The single-row case below locks that byte-for-byte quirk.
/// </para>
/// </summary>
public sealed class UpsertCommandGeneratorSqlServerTests : IDisposable
{
    // EF model column order for the entity below: Id (key), then the base + declared columns as EF orders
    // them — Blob, Count, CreatedAt, LastModifiedAt, Name. Frozen so the shared helpers cannot silently
    // reorder columns or renumber parameters.
    private const string TwoRowSql =
        "MERGE [dbo].[TestEntities] AS Target\n" +
        "USING (VALUES\n" +
        "({0}, {1}, {2}, {3}, {4}, {5}),\n" +
        "({6}, {7}, {8}, {9}, {10}, {11})\n" +
        ") AS Source ([Id], [Blob], [Count], [CreatedAt], [LastModifiedAt], [Name])\n" +
        "ON Target.[Id] = Source.[Id]\n" +
        "WHEN MATCHED THEN\n" +
        "    UPDATE SET Target.[Blob] = Source.[Blob], Target.[Count] = Source.[Count], Target.[CreatedAt] = Source.[CreatedAt], Target.[LastModifiedAt] = Source.[LastModifiedAt], Target.[Name] = Source.[Name]\n" +
        "WHEN NOT MATCHED THEN\n" +
        "    INSERT ([Id], [Blob], [Count], [CreatedAt], [LastModifiedAt], [Name])\n" +
        "    VALUES (Source.[Id], Source.[Blob], Source.[Count], Source.[CreatedAt], Source.[LastModifiedAt], Source.[Name]);\n";

    // Single row with a NULL Blob: the {n} token for the varbinary column is replaced by the CAST literal,
    // but the null value is still appended to the parameter sequence (positions therefore skip a token).
    private const string SingleRowNullBlobSql =
        "MERGE [dbo].[TestEntities] AS Target\n" +
        "USING (VALUES\n" +
        "({0}, CAST(NULL AS varbinary(max)), {2}, {3}, {4}, {5})\n" +
        ") AS Source ([Id], [Blob], [Count], [CreatedAt], [LastModifiedAt], [Name])\n" +
        "ON Target.[Id] = Source.[Id]\n" +
        "WHEN MATCHED THEN\n" +
        "    UPDATE SET Target.[Blob] = Source.[Blob], Target.[Count] = Source.[Count], Target.[CreatedAt] = Source.[CreatedAt], Target.[LastModifiedAt] = Source.[LastModifiedAt], Target.[Name] = Source.[Name]\n" +
        "WHEN NOT MATCHED THEN\n" +
        "    INSERT ([Id], [Blob], [Count], [CreatedAt], [LastModifiedAt], [Name])\n" +
        "    VALUES (Source.[Id], Source.[Blob], Source.[Count], Source.[CreatedAt], Source.[LastModifiedAt], Source.[Name]);\n";

    private readonly TestDbContext _dbContext = TestDbContext.Create();
    private readonly UpsertCommandGenerator _generator = new();

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public void Generate_SqlServer_TwoEntities_EmitsFrozenSqlAndParameters()
    {
        var created = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var modified = new DateTimeOffset(2024, 6, 7, 8, 9, 10, TimeSpan.Zero);
        var blobA = new byte[] { 1, 2, 3 };
        var blobB = new byte[] { 4, 5 };
        var entities = new List<TestEntity>
        {
            new() { Id = "a", Name = "one", Count = 1, CreatedAt = created, LastModifiedAt = modified, Blob = blobA },
            new() { Id = "b", Name = "two", Count = 2, CreatedAt = created, LastModifiedAt = modified, Blob = blobB }
        };

        var command = _generator.Generate(_dbContext, entities, e => e.Id);

        Assert.Equal(NormalizeNewlines(TwoRowSql), NormalizeNewlines(command.Command));
        Assert.Equal(
            new object[] { "a", blobA, 1, created, modified, "one", "b", blobB, 2, created, modified, "two" },
            command.Parameters);
    }

    [Fact]
    public void Generate_SqlServer_SingleEntity_NullVarbinary_EmitsCastLiteralButKeepsParameter()
    {
        var created = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var modified = new DateTimeOffset(2024, 6, 7, 8, 9, 10, TimeSpan.Zero);

        var command = _generator.Generate(
            _dbContext,
            new List<TestEntity> { new() { Id = "x", Name = "only", Count = 7, CreatedAt = created, LastModifiedAt = modified, Blob = null } },
            e => e.Id);

        Assert.Equal(NormalizeNewlines(SingleRowNullBlobSql), NormalizeNewlines(command.Command));
        // The null Blob value is still appended, so the sequence has 6 entries even though its token was
        // replaced by the CAST literal.
        Assert.Equal(new object?[] { "x", null, 7, created, modified, "only" }, command.Parameters);
    }

    // StringBuilder.AppendLine emits Environment.NewLine, so normalize to keep the golden strings
    // platform-independent (the dialect syntax, column order and parameter numbering are what we freeze).
    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n");

    public sealed class TestEntity : Entity
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public byte[]? Blob { get; set; }
    }

    public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> Entities => Set<TestEntity>();

        // Bogus connection string: the model builds offline and SQL generation never connects.
        public static TestDbContext Create()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer("Server=.;Database=Elsa;Trusted_Connection=True;")
                .Options;
            return new(options);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Pin the schema so the emitted [schema].[table] is deterministic across environments.
            modelBuilder.HasDefaultSchema("dbo");
            modelBuilder.Entity<TestEntity>(entity =>
            {
                entity.ToTable("TestEntities");
                entity.HasKey(x => x.Id);
                entity.Ignore(x => x.RowNumber);
            });
        }
    }
}
