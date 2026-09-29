using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Elsa.Persistence.Schema;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Pins what the materialization interceptor exists for: a module that maps domain types directly deserializes JSON
/// in a value converter while EF materializes the row, so the stamp has to be checked before EF reads that column. A
/// row whose converter would throw is the witness: a check that ran any later reports the converter's exception.
/// </summary>
public sealed class EfSchemaVersionMaterializationInterceptorTests : IDisposable
{
    private readonly SqliteConnection connection = new("Filename=:memory:");

    public EfSchemaVersionMaterializationInterceptorTests()
    {
        connection.Open();
        using var context = Context();
        context.Database.EnsureCreated();
    }

    public void Dispose() => connection.Dispose();

    [Fact]
    public void A_row_at_the_current_version_materializes_tracked_and_untracked()
    {
        Insert("current", StampedContext.Version, """{"note":"ok"}""");

        using var context = Context();
        Assert.Equal("ok", context.Rows.Single().Content.Note);
        Assert.Equal("ok", context.Rows.AsNoTracking().Single().Content.Note);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_newer_row_whose_content_this_build_cannot_read_reports_skew_before_the_converter_runs(bool tracked)
    {
        Insert("newer", "2.0.0", "not-json");

        using var context = Context();
        var query = tracked ? context.Rows : context.Rows.AsNoTracking();
        var skew = Assert.Throws<EfSchemaVersionSkewException>(() => query.Single());

        Assert.Equal(StampedContext.Family, skew.Family);
        Assert.Equal("2.0.0", skew.Found);
        Assert.Equal(StampedContext.Version, skew.Expected);
    }

    /// <summary>
    /// The witness the test above relies on: at the current version the same content fails in the converter, so a check
    /// that ran after the converter would have reported that failure rather than skew.
    /// </summary>
    [Fact]
    public void A_current_row_whose_content_this_build_cannot_read_fails_in_the_converter_rather_than_as_skew()
    {
        Insert("current", StampedContext.Version, "not-json");

        using var context = Context();
        var failure = Assert.ThrowsAny<Exception>(() => context.Rows.AsNoTracking().Single());

        Assert.IsNotType<EfSchemaVersionSkewException>(failure);
        Assert.True(failure is JsonException || failure.InnerException is JsonException, failure.ToString());
    }

    /// <summary>A stamp column left empty is a row with no version, and a missing version is skew, never the baseline.</summary>
    [Fact]
    public void A_row_carrying_an_empty_stamp_is_skew_rather_than_the_current_version()
    {
        Insert("unstamped", "", """{"note":"ok"}""");

        using var context = Context();
        var skew = Assert.Throws<EfSchemaVersionSkewException>(() => context.Rows.AsNoTracking().Single());
        Assert.Equal("", skew.Found);
    }

    /// <summary>
    /// A value converter deserializes the content while EF materializes the row, before any upcaster could run, so the
    /// interceptor accepts the current version alone even when the family's chain reaches further back (spec 180,
    /// FR-006): a predecessor's row is refused as skew rather than read as if this build had written it. Its writes still
    /// stamp the current version.
    /// </summary>
    [Fact]
    public void A_context_whose_family_has_a_chain_refuses_a_predecessor_row_rather_than_reading_it_unupcast()
    {
        Insert("older", "1", """{"note":"ok"}""");
        using var context = new ChainedContext(new DbContextOptionsBuilder<ChainedContext>().UseSqlite(connection).Options);

        var skew = Assert.Throws<EfSchemaVersionSkewException>(() => context.Rows.AsNoTracking().Single());

        Assert.Equal("1", skew.Found);
        Assert.Equal(["2"], skew.ReadableVersions);
        context.Rows.Add(new Row { Id = "saved", Content = new("ok") });
        context.SaveChanges();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SchemaVersion FROM rows WHERE Id = 'saved'";
        Assert.Equal("2", command.ExecuteScalar());
    }

    [Fact]
    public void Every_row_the_context_saves_carries_its_schema_version()
    {
        using (var context = Context())
        {
            context.Rows.Add(new Row { Id = "saved", Content = new("ok") });
            context.SaveChanges();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SchemaVersion FROM rows WHERE Id = 'saved'";
        Assert.Equal(StampedContext.Version, command.ExecuteScalar());
    }

    /// <summary>
    /// A context that declares no family is one whose stores check stamps in their own code, so the shared instance
    /// leaves its reads alone; stamping its writes has no version to stamp, and fails.
    /// </summary>
    [Fact]
    public void A_context_that_declares_no_schema_family_is_read_as_is_and_cannot_stamp_its_writes()
    {
        Insert("newer", "2.0.0", """{"note":"ok"}""");

        using var context = new UnversionedContext(new DbContextOptionsBuilder<UnversionedContext>()
            .UseSqlite(connection)
            .AddInterceptors(EfSchemaVersionMaterializationInterceptor.Instance)
            .Options);

        Assert.Equal("ok", context.Rows.AsNoTracking().Single().Content.Note);
        var failure = Assert.Throws<InvalidOperationException>(() => EfSchemaVersionMaterializationInterceptor.StampWrites(context));
        Assert.Contains(nameof(IEfSchemaVersionedContext), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAdded_adds_the_one_shared_interceptor_once()
    {
        var builder = new DbContextOptionsBuilder<StampedContext>().UseSqlite(connection);
        EfSchemaVersionMaterializationInterceptor.EnsureAdded(builder);
        EfSchemaVersionMaterializationInterceptor.EnsureAdded(builder);

        var interceptors = builder.Options.FindExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>()!.Interceptors!;
        Assert.Single(interceptors, interceptor => ReferenceEquals(interceptor, EfSchemaVersionMaterializationInterceptor.Instance));
    }

    /// <summary>
    /// A pooled context's options are frozen before <c>OnConfiguring</c> runs, and EF refuses to change them there. A
    /// registration that adds the interceptor up front leaves <see cref="EfSchemaVersionMaterializationInterceptor.EnsureAdded"/>
    /// nothing to change, so the pooled context still checks its rows.
    /// </summary>
    [Fact]
    public void A_pooled_context_registered_with_the_interceptor_checks_its_rows()
    {
        Insert("newer", "2.0.0", "not-json");
        using var services = new ServiceCollection()
            .AddDbContextPool<StampedContext>(options => options.UseSqlite(connection).AddInterceptors(EfSchemaVersionMaterializationInterceptor.Instance))
            .BuildServiceProvider();
        using var scope = services.CreateScope();

        Assert.Throws<EfSchemaVersionSkewException>(() => scope.ServiceProvider.GetRequiredService<StampedContext>().Rows.Single());
    }

    private StampedContext Context() => new(new DbContextOptionsBuilder<StampedContext>().UseSqlite(connection).Options);

    private void Insert(string id, string schemaVersion, string content)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO rows (Id, SchemaVersion, Content) VALUES ($id, $version, $content)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$version", schemaVersion);
        command.Parameters.AddWithValue("$content", content);
        command.ExecuteNonQuery();
    }

    private sealed class StampedContext(DbContextOptions<StampedContext> options) : DbContext(options), IEfSchemaVersionedContext
    {
        public const string Family = "Probe";
        public const string Version = "1.0.0";

        public DbSet<Row> Rows => Set<Row>();

        private static readonly EfSchemaChain SchemaChain =
            EfSchemaChain.For(new EfSchemaFamilyDescriptor(Family, null, Version, typeof(StampedContext).Assembly));

        EfSchemaChain IEfSchemaVersionedContext.SchemaChain => SchemaChain;

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            EfSchemaVersionMaterializationInterceptor.StampWrites(this);
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => EfSchemaVersionMaterializationInterceptor.EnsureAdded(optionsBuilder);

        protected override void OnModelCreating(ModelBuilder modelBuilder) => Configure(modelBuilder);

        public static void Configure(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<Row>();
            row.ToTable("rows");
            row.HasKey(x => x.Id);
            row.Property<string>(EfSchemaVersionMaterializationInterceptor.PropertyName).HasMaxLength(32).IsRequired();
            row.Property(x => x.Content).HasConversion(
                value => JsonSerializer.Serialize(value, JsonSerializerOptions.Web),
                value => JsonSerializer.Deserialize<Payload>(value, JsonSerializerOptions.Web)!);
        }
    }

    /// <summary>The same table, for a family whose chain upcasts version 1 to its current version 2.</summary>
    private sealed class ChainedContext(DbContextOptions<ChainedContext> options) : DbContext(options), IEfSchemaVersionedContext
    {
        private static readonly EfSchemaChain SchemaChain = SchemaChains.Declare(StampedContext.Family, "2", SchemaChains.Step<ProbeOneToTwo>());

        public DbSet<Row> Rows => Set<Row>();

        EfSchemaChain IEfSchemaVersionedContext.SchemaChain => SchemaChain;

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            EfSchemaVersionMaterializationInterceptor.StampWrites(this);
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => EfSchemaVersionMaterializationInterceptor.EnsureAdded(optionsBuilder);

        protected override void OnModelCreating(ModelBuilder modelBuilder) => StampedContext.Configure(modelBuilder);
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class ProbeOneToTwo : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
    }

    private sealed class UnversionedContext(DbContextOptions<UnversionedContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => StampedContext.Configure(modelBuilder);
    }

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public Payload Content { get; set; } = new("");
    }

    private sealed record Payload(string Note);
}
