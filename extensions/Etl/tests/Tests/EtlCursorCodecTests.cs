using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Api;

namespace Elsa.Etl.Tests;

public sealed class EtlCursorCodecTests
{
    private const string SigningKey = "unit-test-cursor-signing-key-32-bytes-minimum";

    [Fact]
    public void Encode_and_decode_round_trip_every_cursor_field_without_exposing_the_key()
    {
        var state = State();
        var codec = new EtlCursorCodec(SigningKey);

        var token = codec.Encode(state);

        Assert.True(token.Length <= 4096);
        Assert.DoesNotContain(SigningKey, token, StringComparison.Ordinal);
        Assert.Equal(state, codec.Decode(token));
    }

    [Fact]
    public void Constructor_requires_at_least_32_utf8_signing_key_bytes()
    {
        Assert.Throws<ArgumentException>(() => new EtlCursorCodec(new string('k', 31)));
        Assert.NotNull(new EtlCursorCodec(new string('é', 16)));
    }

    [Fact]
    public void Decode_rejects_payload_and_signature_tampering_and_wrong_keys()
    {
        var token = new EtlCursorCodec(SigningKey).Encode(State());
        var separator = token.IndexOf('.');
        var payloadTampered = (token[0] == 'A' ? "B" : "A") + token[1..];
        var signatureTampered = token[..(separator + 1)] + (token[separator + 1] == 'A' ? "B" : "A") + token[(separator + 2)..];

        Assert.Throws<FormatException>(() => new EtlCursorCodec(SigningKey).Decode(payloadTampered));
        Assert.Throws<FormatException>(() => new EtlCursorCodec(SigningKey).Decode(signatureTampered));
        Assert.Throws<FormatException>(() => new EtlCursorCodec(new string('x', 32)).Decode(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("abc")]
    [InlineData(".abc")]
    [InlineData("abc.")]
    [InlineData("abc.def.ghi")]
    [InlineData("abc.def=")]
    [InlineData("abc.def+")]
    public void Decode_rejects_malformed_token_envelopes(string token)
    {
        Assert.Throws<FormatException>(() => new EtlCursorCodec(SigningKey).Decode(token));
    }

    [Fact]
    public void Decode_rejects_tokens_over_the_length_limit()
    {
        Assert.Throws<FormatException>(() => new EtlCursorCodec(SigningKey).Decode(new string('a', 4097)));
    }

    [Fact]
    public void Decode_rejects_unknown_fields_noncanonical_json_and_invalid_states_even_with_valid_signatures()
    {
        var codec = new EtlCursorCodec(SigningKey);
        var canonicalJson = Encoding.UTF8.GetString(DecodePayload(codec.Encode(State())));
        var unknownMember = canonicalJson.Insert(canonicalJson.Length - 1, ",\"extra\":true");
        var noncanonicalJson = canonicalJson.Replace("\"format\":1", "\"format\" :1", StringComparison.Ordinal);
        const string invalidFormatJson = "{\"format\":2}";

        Assert.Throws<FormatException>(() => codec.Decode(Sign(unknownMember, SigningKey)));
        Assert.Throws<FormatException>(() => codec.Decode(Sign(noncanonicalJson, SigningKey)));
        Assert.Throws<FormatException>(() => codec.Decode(Sign(invalidFormatJson, SigningKey)));
    }

    [Fact]
    public void Encode_rejects_unknown_kinds_invalid_hashes_and_out_of_bound_values()
    {
        var codec = new EtlCursorCodec(SigningKey);
        var state = State();

        Assert.Throws<ArgumentException>(() => codec.Encode(state with { Kind = "unknown" }));
        Assert.Throws<ArgumentException>(() => codec.Encode(state with { ScopeKey = new string('A', 64) }));
        Assert.Throws<ArgumentException>(() => codec.Encode(state with { Limit = 1001 }));
        Assert.Throws<ArgumentException>(() => codec.Encode(state with { LastVersion = -1 }));
        Assert.Throws<ArgumentException>(() => codec.Encode(state with { LastAlias = new string('x', 513) }));
    }

    [Fact]
    public void Authoring_cursor_payload_remains_unchanged_when_runtime_boundary_is_absent()
    {
        var codec = new EtlCursorCodec(SigningKey);
        var json = Encoding.UTF8.GetString(DecodePayload(codec.Encode(State())));
        Assert.DoesNotContain("lastOrdinal", json);
        Assert.Equal(State(), codec.Decode(Sign(json, SigningKey)));
        var runtime = State() with { Kind = "partitions", LastOrdinal = 3 };
        Assert.Equal(runtime, codec.Decode(codec.Encode(runtime)));
        Assert.Throws<ArgumentException>(() => codec.Encode(runtime with { LastOrdinal = -1 }));
    }

    private static EtlCursorState State() => new(
        1,
        "versions",
        new string('a', 64),
        "orders",
        25,
        "page-after-id",
        3,
        7,
        "source",
        "reader");

    private static byte[] DecodePayload(string token)
    {
        var separator = token.IndexOf('.');
        var payload = token[..separator].Replace('-', '+').Replace('_', '/');
        payload += new string('=', (4 - payload.Length % 4) % 4);
        return Convert.FromBase64String(payload);
    }

    private static string Sign(string json, string signingKey)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), payload);
        return $"{EncodeBase64Url(payload)}.{EncodeBase64Url(signature)}";
    }

    private static string EncodeBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
