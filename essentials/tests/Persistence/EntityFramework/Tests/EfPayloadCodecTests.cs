using System.Text;
using Elsa.Persistence.EntityFramework;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Pins the stored form of a payload column. Losslessness is the property that actually hurts if it breaks, so it
/// is asserted per case rather than in one round-trip test, and the cases include the ones a UTF-8 codec would
/// silently corrupt.
/// </summary>
public sealed class EfPayloadCodecTests
{
    private const int NoMinimum = 0;

    /// <summary>
    /// Values that survive xUnit's theory-argument serialization. Lone surrogates do NOT, so they are built inside
    /// <see cref="Surrogates_survive_as_the_same_code_units"/> instead; passing one through here yields a string
    /// xUnit has already replaced with U+FFFD, and the round trip then "passes" without ever seeing a surrogate.
    /// </summary>
    public static TheoryData<string> Values() => new()
    {
        "",
        "{}",
        "{\"a\":1}",
        "null",
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
        new string('x', 10_000),
        "embedded \0 nul",
        "strict-encoder characters: \u00e9 < & + \" \\",
        "\uFEFF leading byte order mark"
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void Plaintext_round_trips_when_nothing_is_encoded(string value) =>
        Assert.Equal(value, EfPayloadCodec.Decode(EfPayloadCodec.Encode(value, EfPayloadCompression.None, NoMinimum)));

    [Theory]
    [MemberData(nameof(Values))]
    public void Every_value_round_trips_through_gzip(string value)
    {
        var stored = EfPayloadCodec.Encode(value, EfPayloadCompression.GZip, NoMinimum);

        Assert.Equal(value, EfPayloadCodec.Decode(stored));
        // Short values are deliberately stored as plaintext, so only the compressible one proves the frame path.
        if (value.Length >= 10_000)
            Assert.True(EfPayloadCodec.IsFrame(stored));
    }

    /// <summary>
    /// The cases a UTF-8 codec silently corrupts, and the reason the codec stores UTF-16 code units.
    /// <para>
    /// Built here rather than passed as theory data on purpose: xUnit serializes theory arguments, and a lone
    /// surrogate does not survive that. An earlier draft passed against inputs xUnit had already replaced with
    /// U+FFFD. The first assertion below keeps that from recurring: it proves the input still holds the surrogate.
    /// </para>
    /// <para>
    /// Each value is padded so the frame is genuinely shorter than the plaintext. Without the padding
    /// <see cref="EfPayloadCodec.Encode"/> correctly returns the plaintext untouched, the codec never runs, and the
    /// round trip is a no-op that passes against any codec at all — which a UTF-8 mutation confirmed it did.
    /// The <see cref="EfPayloadCodec.IsFrame"/> assertion is what proves the encoding was exercised.
    /// </para>
    /// <para>
    /// Equality alone would also be too weak, because a codec that replaced every lone surrogate with U+FFFD maps
    /// distinct inputs to equal outputs of the same length. The code units are compared instead.
    /// </para>
    /// </summary>
    [Fact]
    public void Surrogates_survive_as_the_same_code_units_through_a_real_frame()
    {
        string[] awkward =
        [
            "\uD800",
            "\uDC00",
            "lone \uD800 high",
            "lone \uDFFF low",
            "\uD800\uD800",
            "\uD83D\uDE00",
            "line separator" + (char)0x2028 + "and paragraph separator" + (char)0x2029,
            "embedded \0 nul",
            "\uFEFF byte order mark"
        ];

        foreach (var value in awkward.Select(awkwardValue => awkwardValue + new string('x', 4_096)))
        {
            Assert.DoesNotContain('\uFFFD', value);

            var stored = EfPayloadCodec.Encode(value, EfPayloadCompression.GZip, NoMinimum);

            Assert.True(EfPayloadCodec.IsFrame(stored), "the value was not encoded, so this proves nothing");
            var decoded = EfPayloadCodec.Decode(stored)!;
            Assert.Equal(value.Length, decoded.Length);
            Assert.Equal(value.Select(character => (int)character), decoded.Select(character => (int)character));
            Assert.DoesNotContain('\uFFFD', decoded);
        }
    }

    /// <summary>
    /// The same values with no padding. They are short, so the encoder returns them untouched — which is correct,
    /// and is exactly why the test above has to pad.
    /// </summary>
    [Fact]
    public void A_short_awkward_value_is_stored_as_itself()
    {
        var stored = EfPayloadCodec.Encode("\uD800", EfPayloadCompression.GZip, NoMinimum);

        Assert.False(EfPayloadCodec.IsFrame(stored));
        Assert.Equal("\uD800", stored);
    }

    [Fact]
    public void Null_stays_null_rather_than_becoming_a_frame_or_an_empty_string()
    {
        Assert.Null(EfPayloadCodec.Encode(null, EfPayloadCompression.GZip, NoMinimum));
        Assert.Null(EfPayloadCodec.Decode(null));
    }

    /// <summary>Null and empty are different rows; neither may be written as the other.</summary>
    [Fact]
    public void Empty_is_distinct_from_null()
    {
        Assert.Equal("", EfPayloadCodec.Decode(EfPayloadCodec.Encode("", EfPayloadCompression.GZip, NoMinimum)));
        Assert.NotNull(EfPayloadCodec.Encode("", EfPayloadCompression.GZip, NoMinimum));
    }

    /// <summary>
    /// The row every existing database is full of. Nothing marks it, so it has to be recognised by the absence of
    /// the marker rather than by anything written beside it.
    /// </summary>
    [Fact]
    public void A_value_written_before_this_shipped_reads_as_itself()
    {
        Assert.Equal("{\"already\":\"here\"}", EfPayloadCodec.Decode("{\"already\":\"here\"}"));
        Assert.False(EfPayloadCodec.IsFrame("{\"already\":\"here\"}"));
    }

    [Fact]
    public void A_gzip_frame_is_marked_and_a_plaintext_value_is_not()
    {
        var framed = EfPayloadCodec.Encode(new string('x', 10_000), EfPayloadCompression.GZip, NoMinimum)!;

        Assert.True(EfPayloadCodec.IsFrame(framed));
        Assert.StartsWith("elsaz1.gz.", framed, StringComparison.Ordinal);
        Assert.False(EfPayloadCodec.IsFrame(EfPayloadCodec.Encode("{}", EfPayloadCompression.None, NoMinimum)));
    }

    /// <summary>
    /// Base64 expands by four thirds, so a frame is not always smaller. Choosing per value means the encoding can
    /// never make a row larger, whatever the threshold is set to.
    /// </summary>
    [Fact]
    public void A_frame_that_would_not_be_shorter_is_written_as_plaintext()
    {
        var incompressible = "{\"a\":1}";

        var stored = EfPayloadCodec.Encode(incompressible, EfPayloadCompression.GZip, NoMinimum);

        Assert.Equal(incompressible, stored);
        Assert.False(EfPayloadCodec.IsFrame(stored));
    }

    [Fact]
    public void A_value_shorter_than_the_minimum_is_not_attempted()
    {
        var compressible = new string('x', 10_000);

        Assert.False(EfPayloadCodec.IsFrame(EfPayloadCodec.Encode(compressible, EfPayloadCompression.GZip, 10_001)));
        Assert.True(EfPayloadCodec.IsFrame(EfPayloadCodec.Encode(compressible, EfPayloadCompression.GZip, 10_000)));
    }

    /// <summary>
    /// Payload columns are not all JSON documents: a trigger-binding projection row stores a hex fingerprint. So
    /// "a document can never begin with the marker" is not available as an argument, and the escape has to exist.
    /// </summary>
    [Theory]
    [InlineData("elsaz1.")]
    [InlineData("elsaz1.gz.not-really")]
    [InlineData("elsaz1.something a user typed")]
    public void A_plaintext_that_looks_like_a_frame_is_escaped_rather_than_stored_raw(string value)
    {
        foreach (var codec in new[] { EfPayloadCompression.None, EfPayloadCompression.GZip })
        {
            var stored = EfPayloadCodec.Encode(value, codec, NoMinimum)!;

            Assert.StartsWith("elsaz1.raw.", stored, StringComparison.Ordinal);
            Assert.Equal(value, EfPayloadCodec.Decode(stored));
        }
    }

    /// <summary>
    /// Elsa 3's equivalent path catches the failure, logs a warning and substitutes a default workflow state. That
    /// turns a corrupt row into a silently empty one, which is worse than an error.
    /// </summary>
    [Theory]
    [InlineData("elsaz1.")]
    [InlineData("elsaz1.gz")]
    [InlineData("elsaz1.gz.!!!not base64!!!")]
    [InlineData("elsaz1.gz.bm90IGd6aXA=")]
    public void A_frame_this_build_cannot_read_fails_closed(string stored) =>
        Assert.Throws<InvalidDataException>(() => EfPayloadCodec.Decode(stored));

    /// <summary>
    /// The forward-compatibility direction: a database written by a newer build. The message has to name the codec,
    /// because the fix is to deploy that build rather than to treat the row as corrupt.
    /// </summary>
    [Fact]
    public void A_frame_naming_an_unknown_codec_names_it_in_the_failure()
    {
        var stored = "elsaz1.zstd." + Convert.ToBase64String(Encoding.UTF8.GetBytes("whatever"));

        var failure = Assert.Throws<InvalidDataException>(() => EfPayloadCodec.Decode(stored));

        Assert.Contains("zstd", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A frame carries its own codec, so a host that turns compression off keeps reading what it already wrote.
    /// That is what makes the setting reversible without a rewrite pass.
    /// </summary>
    [Fact]
    public void A_frame_stays_readable_after_the_codec_is_turned_off()
    {
        var value = new string('x', 10_000);
        var framed = EfPayloadCodec.Encode(value, EfPayloadCompression.GZip, NoMinimum);

        Assert.Equal(value, EfPayloadCodec.Decode(framed));
        Assert.Equal(value, EfPayloadCodec.Decode(EfPayloadCodec.Encode(value, EfPayloadCompression.None, NoMinimum)));
    }
}
