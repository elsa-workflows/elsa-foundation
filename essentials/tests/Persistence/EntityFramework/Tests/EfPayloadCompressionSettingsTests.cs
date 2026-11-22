using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Pins how an operator turns payload compression on: the module's own key, then the host-wide one, then nothing.
/// The default matters as much as the override — a deployment that configures nothing must come out exactly as it
/// was before compression existed.
/// </summary>
public sealed class EfPayloadCompressionSettingsTests
{
    private const string Owner = "Runtime";

    [Fact]
    public void Configuring_nothing_binds_nothing()
    {
        Assert.Null(EfPayloadCompressionSettings.Resolve(Services(), Owner));
        Assert.Null(EfPayloadCompressionSettings.Resolve(Services(), "OpenTelemetry"));
    }

    /// <summary>
    /// Not merely "the codec is None": binding no extension at all is what keeps such a host's contexts sharing one
    /// model with what they had before, rather than splitting the cache for a setting that changes no stored byte.
    /// </summary>
    [Fact]
    public void A_context_in_a_host_that_configures_nothing_carries_no_payload_extension()
    {
        var builder = new DbContextOptionsBuilder();

        new EfModuleBinding(Owner, "__history", null).Apply(builder, Services(), "Sqlite", "Filename=:memory:", null);

        Assert.Null(builder.Options.FindExtension<EfPayloadCompressionOptionsExtension>());
    }

    [Fact]
    public void The_host_wide_key_reaches_every_module()
    {
        var services = Services((EfPayloadCompressionSettings.ConfigurationKey, "GZip"));

        Assert.Equal(EfPayloadCompression.GZip, EfPayloadCompressionSettings.Resolve(services, Owner)!.Codec);
        Assert.Equal(EfPayloadCompression.GZip, EfPayloadCompressionSettings.Resolve(services, "OpenTelemetry")!.Codec);
    }

    [Fact]
    public void A_module_key_wins_over_the_host_wide_one()
    {
        var services = Services(
            (EfPayloadCompressionSettings.ConfigurationKey, "GZip"),
            (EfPayloadCompressionSettings.ModuleConfigurationKey("WorkflowsDesign"), "None"));

        Assert.Equal(EfPayloadCompression.GZip, EfPayloadCompressionSettings.Resolve(services, Owner)!.Codec);
        Assert.Null(EfPayloadCompressionSettings.Resolve(services, "WorkflowsDesign"));
    }

    /// <summary>Diagnostics is the plausible first user; design is the least plausible. One key, one module.</summary>
    [Fact]
    public void A_module_key_reaches_only_that_module()
    {
        var services = Services((EfPayloadCompressionSettings.ModuleConfigurationKey("OpenTelemetry"), "GZip"));

        Assert.Equal(EfPayloadCompression.GZip, EfPayloadCompressionSettings.Resolve(services, "OpenTelemetry")!.Codec);
        Assert.Null(EfPayloadCompressionSettings.Resolve(services, Owner));
    }

    [Fact]
    public void A_configured_module_binds_the_extension_onto_its_context()
    {
        var builder = new DbContextOptionsBuilder();
        var services = Services((EfPayloadCompressionSettings.ModuleConfigurationKey(Owner), "GZip"));

        new EfModuleBinding(Owner, "__history", null).Apply(builder, services, "Sqlite", "Filename=:memory:", null);

        var extension = builder.Options.FindExtension<EfPayloadCompressionOptionsExtension>();
        Assert.NotNull(extension);
        Assert.Equal(EfPayloadCompression.GZip, extension.Options.Codec);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("GZIP")]
    [InlineData("  GZip  ")]
    public void The_codec_name_is_case_and_whitespace_insensitive(string configured) =>
        Assert.Equal(EfPayloadCompression.GZip, EfPayloadCompressionSettings.Parse(Owner, configured));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_codec_is_no_compression(string? configured) =>
        Assert.Equal(EfPayloadCompression.None, EfPayloadCompressionSettings.Parse(Owner, configured));

    /// <summary>
    /// A typo that silently fell back to no compression would look exactly like the setting working. The operator
    /// would have no way to tell, so it fails at startup and names what it would accept.
    /// </summary>
    [Theory]
    [InlineData("gzp")]
    [InlineData("zstd")]
    [InlineData("brotli")]
    [InlineData("true")]
    public void An_unknown_codec_is_refused_and_the_message_says_what_is_accepted(string configured)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => EfPayloadCompressionSettings.Parse(Owner, configured));

        Assert.Contains(configured, failure.Message, StringComparison.Ordinal);
        Assert.Contains(Owner, failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(EfPayloadCompression.GZip), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_threshold_is_read_from_configuration()
    {
        var services = Services(
            (EfPayloadCompressionSettings.ConfigurationKey, "GZip"),
            (EfPayloadCompressionSettings.MinimumLengthConfigurationKey, "0"));

        Assert.Equal(0, EfPayloadCompressionSettings.Resolve(services, Owner)!.MinimumLength);
    }

    [Fact]
    public void The_threshold_defaults_when_it_is_not_configured() =>
        Assert.Equal(
            EfPayloadCompressionOptions.DefaultMinimumLength,
            EfPayloadCompressionSettings.Resolve(Services((EfPayloadCompressionSettings.ConfigurationKey, "GZip")), Owner)!.MinimumLength);

    [Theory]
    [InlineData("-1")]
    [InlineData("lots")]
    [InlineData("1.5")]
    public void An_unusable_threshold_is_refused(string configured)
    {
        var services = Services(
            (EfPayloadCompressionSettings.ConfigurationKey, "GZip"),
            (EfPayloadCompressionSettings.MinimumLengthConfigurationKey, configured));

        Assert.Throws<InvalidOperationException>(() => EfPayloadCompressionSettings.Resolve(services, Owner));
    }

    /// <summary>
    /// The threshold alone changes no stored byte, so it must not bind an extension and split the model cache.
    /// </summary>
    [Fact]
    public void A_threshold_without_a_codec_binds_nothing() =>
        Assert.Null(EfPayloadCompressionSettings.Resolve(
            Services((EfPayloadCompressionSettings.MinimumLengthConfigurationKey, "0")), Owner));

    /// <summary>
    /// The whole chain in one test: a configuration key, through <see cref="EfModuleBinding.Apply"/>, onto the
    /// options, into the converter, out as the bytes in the column. Each link is covered on its own elsewhere, and
    /// a chain of individually-green links can still be disconnected.
    /// </summary>
    [Fact]
    public void A_configured_codec_reaches_the_bytes_in_the_column()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
        connection.Open();
        var value = "{\"note\":\"" + new string('x', 4_096) + "\"}";

        using (var configured = Context(connection, Services(
                   (EfPayloadCompressionSettings.ModuleConfigurationKey(Owner), "GZip"),
                   (EfPayloadCompressionSettings.MinimumLengthConfigurationKey, "0"))))
        {
            configured.Database.EnsureCreated();
            configured.Add(new Row { Id = "configured", ContentJson = value });
            configured.SaveChanges();
        }

        using (var unconfigured = Context(connection, Services()))
        {
            unconfigured.Add(new Row { Id = "unconfigured", ContentJson = value });
            unconfigured.SaveChanges();
        }

        Assert.True(EfPayloadCodec.IsFrame(ReadRaw(connection, "configured")), "the configured codec never reached the column");
        Assert.False(EfPayloadCodec.IsFrame(ReadRaw(connection, "unconfigured")));
        // And both are still the same document to every reader.
        using var reader = Context(connection, Services());
        Assert.Equal(value, reader.Set<Row>().Single(row => row.Id == "configured").ContentJson);
        Assert.Equal(value, reader.Set<Row>().Single(row => row.Id == "unconfigured").ContentJson);
    }

    private static PayloadContext Context(Microsoft.Data.Sqlite.SqliteConnection connection, IServiceProvider services)
    {
        var builder = new DbContextOptionsBuilder<PayloadContext>();
        new EfModuleBinding(Owner, "__history", null).Apply(builder, services, "Sqlite", "ignored", null);
        // Apply resolves the connection string; this fixture needs the shared in-memory handle instead.
        builder.UseSqlite(connection);
        return new(builder.Options);
    }

    private static string? ReadRaw(Microsoft.Data.Sqlite.SqliteConnection connection, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ContentJson FROM Rows WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        var value = command.ExecuteScalar();
        return value == DBNull.Value ? null : (string?)value;
    }

    private sealed class PayloadContext(DbContextOptions<PayloadContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Row>(entity =>
            {
                entity.ToTable("Rows");
                entity.HasKey(row => row.Id);
            });
            modelBuilder.UseElsaPayloadColumns(this, nameof(Row.ContentJson));
        }
    }

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string? ContentJson { get; set; }
    }

    private static IServiceProvider Services(params (string Key, string Value)[] settings) =>
        new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
                .Build())
            .BuildServiceProvider();
}
