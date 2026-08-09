using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Contracts.Generator;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>Determinism gates for the contract writer (spec 149 FR-002 / SC-002, §2.23.2).</summary>
public sealed class DeterministicJsonTests
{
    private sealed record Sample(string Zebra, string Alpha, JsonElement Payload, double Number);

    private static Sample CreateSample() => new(
        "z",
        "a",
        JsonSerializer.SerializeToElement(new { beta = 2, alpha = new { zulu = 1, yankee = new[] { 3, 1 } } }),
        1.5);

    [Fact]
    public void Object_keys_are_ordinal_sorted_recursively_including_embedded_payloads()
    {
        var json = DeterministicJson.Serialize(CreateSample());

        var alpha = json.IndexOf("\"alpha\"", StringComparison.Ordinal);
        var number = json.IndexOf("\"number\"", StringComparison.Ordinal);
        var payload = json.IndexOf("\"payload\"", StringComparison.Ordinal);
        var zebra = json.IndexOf("\"zebra\"", StringComparison.Ordinal);
        Assert.True(alpha < number && number < payload && payload < zebra, json);

        // Inside the embedded payload element, keys are sorted too.
        var payloadPart = json[payload..];
        Assert.True(
            payloadPart.IndexOf("\"alpha\"", StringComparison.Ordinal) < payloadPart.IndexOf("\"beta\"", StringComparison.Ordinal),
            payloadPart);
        Assert.True(
            payloadPart.IndexOf("\"yankee\"", StringComparison.Ordinal) < payloadPart.IndexOf("\"zulu\"", StringComparison.Ordinal),
            payloadPart);
    }

    [Fact]
    public void Output_uses_lf_without_bom_and_ends_with_single_newline()
    {
        var bytes = DeterministicJson.SerializeToBytes(CreateSample());

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM found");
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Serialization_is_culture_invariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // decimal comma culture
            var german = DeterministicJson.Serialize(CreateSample());
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = DeterministicJson.Serialize(CreateSample());

            Assert.Equal(invariant, german);
            Assert.Contains("1.5", german, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Double_serialization_is_byte_identical()
    {
        Assert.Equal(DeterministicJson.SerializeToBytes(CreateSample()), DeterministicJson.SerializeToBytes(CreateSample()));
    }

    [Fact]
    public void Fingerprint_uses_the_sha256_prefixed_lowercase_convention()
    {
        var fingerprint = DeterministicJson.Fingerprint([1, 2, 3]);

        Assert.StartsWith("sha256:", fingerprint, StringComparison.Ordinal);
        Assert.Equal(7 + 64, fingerprint.Length);
        Assert.Equal(fingerprint, fingerprint.ToLowerInvariant());
    }

    [Fact]
    public void Enums_serialize_with_the_wire_camel_case_spelling()
    {
        var json = DeterministicJson.Serialize(new { kind = JsonValueKind.String });

        Assert.Contains("\"string\"", json, StringComparison.Ordinal);
    }
}
