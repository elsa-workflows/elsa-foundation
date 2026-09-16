using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

public sealed class SecretsSearchKeysTests
{
    [Fact]
    public void Projection_identity_pins_the_portable_mapping()
    {
        Assert.Equal("16.0.0+phase1-dotnet10-delta", SecretsSearchKeys.UnicodeVersion);
        Assert.Equal(
            "elsa-secrets-unicode-ordinal-ignore-case-v1-bcbcc4bf0951b182137ed0f42681f30bafda7777f500c42203cf58bb7e4eaaa1",
            SecretsSearchKeys.UnicodeOrdinalIgnoreCaseAlgorithmId);
    }

    // Golden projections captured from the pinned Unicode data. They replace the former
    // equality check against the deleted Groundwork comparison-key implementation, which
    // was the only available oracle for these representative values.
    [Theory]
    [InlineData("payments", "PAYMENTS")]
    [InlineData("M\u00FCnchen", "M\u00DCNCHEN")]
    [InlineData("stra\u00DFe", "STRA\u00DFE")]
    [InlineData("\u0131", "\u0131")]
    [InlineData("\u03C2\u03C3", "\u03A3\u03A3")]
    [InlineData("\U00010428\U0001044F", "\U00010400\U00010427")]
    [InlineData("\u0130stanbul", "\u0130STANBUL")]
    public void Projection_is_the_pinned_ordinal_ignore_case_mapping(string value, string expected)
    {
        Assert.Equal(expected, SecretsSearchKeys.LookupKey(value));
        Assert.Equal(expected, SecretsSearchKeys.SearchKey(value));
    }

    [Theory]
    [InlineData(0x019B, 0xA7DC)]
    [InlineData(0x0264, 0xA7CB)]
    [InlineData(0x1C8A, 0x1C89)]
    [InlineData(0xA7CD, 0xA7CC)]
    [InlineData(0xA7CF, 0xA7CE)]
    [InlineData(0xA7D3, 0xA7D2)]
    [InlineData(0xA7D5, 0xA7D4)]
    [InlineData(0xA7DB, 0xA7DA)]
    [InlineData(0x10D70, 0x10D50)]
    public void Projection_uses_pinned_unicode_data_instead_of_the_host_runtime(int scalar, int expected)
    {
        var value = char.ConvertFromUtf32(scalar);

        Assert.Equal(char.ConvertFromUtf32(expected), SecretsSearchKeys.LookupKey(value));
        Assert.Equal(char.ConvertFromUtf32(expected), SecretsSearchKeys.SearchKey(value));
    }

    [Fact]
    public void Projection_rejects_lone_high_surrogate() =>
        RejectIllFormedUtf16(new string((char)0xD800, 1));

    [Fact]
    public void Projection_rejects_lone_low_surrogate() =>
        RejectIllFormedUtf16(new string((char)0xDC00, 1));

    [Fact]
    public void Projection_rejects_high_surrogate_followed_by_non_low() =>
        RejectIllFormedUtf16(new string([(char)0xD800, 'A']));

    [Fact]
    public void Projection_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => SecretsSearchKeys.LookupKey(null!));
        Assert.Throws<ArgumentNullException>(() => SecretsSearchKeys.SearchKey(null!));
    }

    private static void RejectIllFormedUtf16(string value)
    {
        var lookup = Assert.Throws<ArgumentException>(() => SecretsSearchKeys.LookupKey(value));
        Assert.Contains("well-formed UTF-16", lookup.Message, StringComparison.Ordinal);
        Assert.Equal("value", lookup.ParamName);
        var search = Assert.Throws<ArgumentException>(() => SecretsSearchKeys.SearchKey(value));
        Assert.Contains("well-formed UTF-16", search.Message, StringComparison.Ordinal);
        Assert.Equal("value", search.ParamName);
    }

}
