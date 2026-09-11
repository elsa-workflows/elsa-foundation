using System.Globalization;
using System.Text;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Groundwork.Kernel;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

public sealed class SecretsSearchKeysTests
{
    [Fact]
    public void Projection_identity_pins_the_phase_1_dotnet_10_mapping_and_records_groundwork_v1()
    {
        Assert.Equal("16.0.0+phase1-dotnet10-delta", SecretsSearchKeys.UnicodeVersion);
        Assert.Equal(
            "elsa-secrets-unicode-ordinal-ignore-case-v1-bcbcc4bf0951b182137ed0f42681f30bafda7777f500c42203cf58bb7e4eaaa1",
            SecretsSearchKeys.UnicodeOrdinalIgnoreCaseAlgorithmId);
        Assert.EndsWith(
            "3206f759667cb9cc764ec243dfb3d322a39970184efab619e80163c36d86818f",
            PortableStringComparison.UnicodeOrdinalIgnoreCaseAlgorithmId,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("payments")]
    [InlineData("München")]
    [InlineData("straße")]
    [InlineData("\u0131")]
    [InlineData("\u03C2\u03C3")]
    [InlineData("\U00010428\U0001044F")]
    [InlineData("\uA7CF\uA7D3\uA7D5")]
    public void Projection_matches_groundwork_unicode_v1_for_representative_existing_values(string value)
    {
        var expected = DecodeGroundworkComparisonKey(
            PortableStringComparison.CreateUnicodeOrdinalIgnoreCase(value));

        Assert.Equal(expected, SecretsSearchKeys.LookupKey(value));
        Assert.Equal(expected, SecretsSearchKeys.SearchKey(value));
    }

    [Fact]
    public void Projection_is_byte_compatible_with_phase_1_runtime_keys_for_every_unicode_scalar()
    {
        var mismatches = new List<string>();
        for (var scalar = 0; scalar <= 0x10FFFF; scalar++)
        {
            if (scalar is >= 0xD800 and <= 0xDFFF)
                continue;

            var value = char.ConvertFromUtf32(scalar);
            var runtime = value.ToUpperInvariant();
            var pinned = SecretsSearchKeys.LookupKey(value);
            if (!string.Equals(runtime, pinned, StringComparison.Ordinal))
                mismatches.Add($"U+{scalar:X}: runtime={CodePoints(runtime)}, pinned={CodePoints(pinned)}");
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void Groundwork_compatibility_boundary_is_exact_and_versioned()
    {
        var differences = new List<int>();
        for (var scalar = 0; scalar <= 0x10FFFF; scalar++)
        {
            if (scalar is >= 0xD800 and <= 0xDFFF)
                continue;

            var value = char.ConvertFromUtf32(scalar);
            var groundwork = DecodeGroundworkComparisonKey(
                PortableStringComparison.CreateUnicodeOrdinalIgnoreCase(value));
            if (!string.Equals(groundwork, SecretsSearchKeys.LookupKey(value), StringComparison.Ordinal))
                differences.Add(scalar);
        }

        int[] expected = [0x017F, ..Enumerable.Range(0x16EBB, 25)];
        Assert.Equal(expected, differences);
    }

    [Fact]
    public void Projection_rejects_ill_formed_utf16_instead_of_persisting_an_unstable_key()
    {
        var exception = Assert.Throws<ArgumentException>(() => SecretsSearchKeys.LookupKey("\uD800"));
        Assert.Contains("well-formed UTF-16", exception.Message, StringComparison.Ordinal);
    }

    private static string DecodeGroundworkComparisonKey(string key)
    {
        Assert.True(key.Length % 6 == 0);
        var result = new StringBuilder(key.Length / 3);
        for (var index = 0; index < key.Length; index += 6)
        {
            var scalar = int.Parse(key.AsSpan(index, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            result.Append(char.ConvertFromUtf32(scalar));
        }

        return result.ToString();
    }

    private static string CodePoints(string value) => string.Join(
        "+",
        value.EnumerateRunes().Select(rune => $"U+{rune.Value:X}"));
}
