using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Pins how a module declares a payload column, and proves a declared column survives a real database round trip.
/// SQLite carries the fast-lane proof: losslessness is the property that hurts if it breaks, so it is checked on
/// every run rather than only on the container legs.
/// </summary>
public sealed class EfPayloadColumnsTests : IDisposable
{
    private readonly SqliteConnection connection = new("Filename=:memory:");

    public EfPayloadColumnsTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    [Fact]
    public void A_payload_column_round_trips_through_the_database_unchanged()
    {
        var value = "{\"note\":\"" + new string('x', 4_096) + "\"}";
        using (var writer = Context(EfPayloadCompression.GZip))
        {
            writer.Database.EnsureCreated();
            writer.Add(new Row { Id = "a", ContentJson = value });
            writer.SaveChanges();
        }

        using var reader = Context(EfPayloadCompression.GZip);
        Assert.Equal(value, reader.Set<Row>().Single(row => row.Id == "a").ContentJson);
    }

    /// <summary>
    /// The converter hides the encoding from every test that goes through EF, so without a read that skips it
    /// nothing here would notice if compression silently stopped happening. This is that read.
    /// </summary>
    [Fact]
    public void The_stored_bytes_are_framed_when_a_codec_is_configured_and_plain_when_it_is_not()
    {
        var value = "{\"note\":\"" + new string('x', 4_096) + "\"}";
        using (var compressed = Context(EfPayloadCompression.GZip))
        {
            compressed.Database.EnsureCreated();
            compressed.Add(new Row { Id = "framed", ContentJson = value });
            compressed.SaveChanges();
        }

        using (var plain = Context(EfPayloadCompression.None))
        {
            plain.Add(new Row { Id = "plain", ContentJson = value });
            plain.SaveChanges();
        }

        var framed = ReadRaw("framed");
        var stored = ReadRaw("plain");

        Assert.True(EfPayloadCodec.IsFrame(framed), "the configured codec did not reach the column");
        Assert.False(EfPayloadCodec.IsFrame(stored));
        Assert.Equal(value, stored);
        Assert.Equal(value, EfPayloadCodec.Decode(framed));
    }

    /// <summary>
    /// The row every existing database is full of. A host that turns the codec on must keep reading what it wrote
    /// before, with no migration and no backfill.
    /// </summary>
    [Fact]
    public void A_row_written_before_the_codec_was_turned_on_still_reads()
    {
        var value = "{\"written\":\"earlier\"}";
        using (var before = Context(EfPayloadCompression.None))
        {
            before.Database.EnsureCreated();
            before.Add(new Row { Id = "old", ContentJson = value });
            before.SaveChanges();
        }

        using var after = Context(EfPayloadCompression.GZip);
        Assert.Equal(value, after.Set<Row>().Single(row => row.Id == "old").ContentJson);
    }

    /// <summary>And the reverse, which is what makes the setting safe to turn back off.</summary>
    [Fact]
    public void A_framed_row_still_reads_after_the_codec_is_turned_off()
    {
        var value = "{\"note\":\"" + new string('x', 4_096) + "\"}";
        using (var compressed = Context(EfPayloadCompression.GZip))
        {
            compressed.Database.EnsureCreated();
            compressed.Add(new Row { Id = "framed", ContentJson = value });
            compressed.SaveChanges();
        }

        using var plain = Context(EfPayloadCompression.None);
        Assert.Equal(value, plain.Set<Row>().Single(row => row.Id == "framed").ContentJson);
    }

    /// <summary>
    /// Two hosts in one process, configured differently. The encoding is baked into a value converter on the model,
    /// and EF caches one model per internal service provider, so without the options extension disagreeing on its
    /// hash the second host would silently reuse the first one's model and write the first one's encoding.
    /// </summary>
    [Fact]
    public void Two_contexts_configured_differently_each_read_their_own_rows()
    {
        var value = "{\"note\":\"" + new string('x', 4_096) + "\"}";
        using var compressed = Context(EfPayloadCompression.GZip);
        using var plain = Context(EfPayloadCompression.None);
        compressed.Database.EnsureCreated();

        compressed.Add(new Row { Id = "from-compressed", ContentJson = value });
        compressed.SaveChanges();
        plain.Add(new Row { Id = "from-plain", ContentJson = value });
        plain.SaveChanges();

        Assert.True(EfPayloadCodec.IsFrame(ReadRaw("from-compressed")), "the compressed host reused the plain model");
        Assert.False(EfPayloadCodec.IsFrame(ReadRaw("from-plain")), "the plain host reused the compressed model");
        Assert.Equal(value, compressed.Set<Row>().Single(row => row.Id == "from-plain").ContentJson);
        Assert.Equal(value, plain.Set<Row>().Single(row => row.Id == "from-compressed").ContentJson);
    }

    [Fact]
    public void Null_and_empty_stay_distinct_through_the_database()
    {
        using (var writer = Context(EfPayloadCompression.GZip))
        {
            writer.Database.EnsureCreated();
            writer.Add(new Row { Id = "null", ContentJson = null });
            writer.Add(new Row { Id = "empty", ContentJson = "" });
            writer.SaveChanges();
        }

        using var reader = Context(EfPayloadCompression.GZip);
        Assert.Null(reader.Set<Row>().Single(row => row.Id == "null").ContentJson);
        Assert.Equal("", reader.Set<Row>().Single(row => row.Id == "empty").ContentJson);
    }

    /// <summary>
    /// The one column family that can never be encoded: four provider computed columns read it in the database, so
    /// a client-side encoding makes every row evaluate invalid and vanish from paged reads.
    /// </summary>
    [Theory]
    [InlineData("ContentAuthorityJson")]
    [InlineData("ContentAuthorityCanonicalJson")]
    [InlineData("ContentAuthorityAuthorityKeyJson")]
    [InlineData("ContentAuthoritySourceIdJson")]
    public void An_excluded_authority_column_is_refused_by_name(string property)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Declare<AuthorityRow>(property));

        Assert.Contains(property, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Secret_material_is_refused()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Declare<SecretRecord>("Payload"));

        Assert.Contains("SecretRecord.Payload", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The standing invariant compression makes load bearing. It held before this shipped for a different reason —
    /// the envelope lifts every queryable attribute into its own column — and an invariant that holds by accident
    /// is one nobody preserves on purpose.
    /// </summary>
    [Fact]
    public void A_keyed_or_indexed_column_cannot_be_a_payload_column()
    {
        Assert.Contains("key or an index", Assert.Throws<InvalidOperationException>(() => Declare<Row>("Id")).Message, StringComparison.Ordinal);
        Assert.Contains("key or an index", Assert.Throws<InvalidOperationException>(() => Declare<Row>("ScopeKey")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_length_bounded_column_cannot_be_a_payload_column() =>
        Assert.Contains("maximum length", Assert.Throws<InvalidOperationException>(() => Declare<Row>("Status")).Message, StringComparison.Ordinal);

    /// <summary>One shared list serves models whose entities do not all carry the same columns.</summary>
    [Fact]
    public void A_name_no_entity_declares_is_ignored()
    {
        var model = Declare<Row>("ColumnFromAnotherModule");

        Assert.Null(model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.ContentJson))!.GetValueConverter());
    }

    /// <summary>
    /// A bare <see cref="ModelBuilder"/> runs no conventions, so every property this fixture asserts on has to be
    /// declared. An earlier draft relied on convention discovery, and the refusal tests passed by throwing nothing
    /// at all: the property under test was simply not in the model.
    /// </summary>
    private static Microsoft.EntityFrameworkCore.Metadata.IMutableModel Declare<TEntity>(string property) where TEntity : class
    {
        var builder = new ModelBuilder();
        builder.Entity<Row>(entity =>
        {
            entity.Property(row => row.Id);
            entity.Property(row => row.ScopeKey);
            entity.Property(row => row.Status).HasMaxLength(32);
            entity.Property(row => row.ContentJson);
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.ScopeKey);
        });
        if (typeof(TEntity) != typeof(Row))
        {
            var entity = builder.Entity(typeof(TEntity));
            entity.Property("Id");
            entity.HasKey("Id");
            entity.Property(property);
        }

        EfPayloadColumns.PayloadProperties(builder.Model, property);
        return builder.Model;
    }

    private string? ReadRaw(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ContentJson FROM Rows WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        var value = command.ExecuteScalar();
        return value == DBNull.Value ? null : (string?)value;
    }

    private PayloadContext Context(EfPayloadCompression codec) =>
        new(new DbContextOptionsBuilder<PayloadContext>()
            .UseSqlite(connection)
            .UseElsaPayloadCompression(new EfPayloadCompressionOptions { Codec = codec, MinimumLength = 0 })
            .Options);

    private sealed class PayloadContext(DbContextOptions<PayloadContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Row>(entity =>
            {
                entity.ToTable("Rows");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Status).HasMaxLength(32);
            });
            modelBuilder.UseElsaPayloadColumns(this, nameof(Row.ContentJson));
        }
    }

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string ScopeKey { get; set; } = "";
        public string Status { get; set; } = "";
        public string? ContentJson { get; set; }
    }

    private sealed class AuthorityRow
    {
        public string Id { get; set; } = "";
        public string? ContentAuthorityJson { get; set; }
        public string? ContentAuthorityCanonicalJson { get; set; }
        public string? ContentAuthorityAuthorityKeyJson { get; set; }
        public string? ContentAuthoritySourceIdJson { get; set; }
    }

    private sealed class SecretRecord
    {
        public string Id { get; set; } = "";
        public string Payload { get; set; } = "{}";
    }
}
