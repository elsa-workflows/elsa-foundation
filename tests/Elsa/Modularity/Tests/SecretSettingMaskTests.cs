using System.Text.Json;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Services;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class SecretSettingMaskTests
{
    private const string Placeholder = SecretSettingMask.Placeholder;

    private static readonly FeatureSettingDescriptor[] Settings = [Setting("Key", secret: true), Setting("Mode", secret: false)];

    [Theory]
    [InlineData("\"material\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("""{"k1":"material"}""")]
    [InlineData("""["material"]""")]
    public void MaskReplacesASetSecretOfAnyJsonTypeWithThePlaceholder(string value)
    {
        var masked = SecretSettingMask.MaskConfiguration(Json($$"""{"Key":{{value}}}"""), Settings);

        Assert.Equal(Placeholder, masked.GetProperty("Key").GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void MaskLeavesAnUnsetSecretAsItIs(string value)
    {
        var masked = SecretSettingMask.MaskConfiguration(Json($$"""{"Key":{{value}}}"""), Settings);

        Assert.Equal(Json(value).GetRawText(), masked.GetProperty("Key").GetRawText());
    }

    [Fact]
    public void MaskMatchesNamesCaseInsensitivelyAndShowsOnlyDeclaredNonSecretSettings()
    {
        var masked = SecretSettingMask.MaskConfiguration(Json("""{"key":"material","mode":"fast","Undeclared":"material"}"""), Settings);

        Assert.Equal(Placeholder, masked.GetProperty("key").GetString());
        Assert.Equal("fast", masked.GetProperty("mode").GetString());
        Assert.Equal(Placeholder, masked.GetProperty("Undeclared").GetString());
    }

    [Fact]
    public void MaskShowsEveryValueWhenEveryKeyIsADeclaredNonSecretSetting()
    {
        var masked = SecretSettingMask.MaskConfiguration(Json("""{"Key":"material"}"""), [Setting("Key", secret: false)]);

        Assert.Equal("material", masked.GetProperty("Key").GetString());
    }

    [Fact]
    public void MaskHidesEveryValueOfAFeatureThatDeclaresNoSettings()
    {
        var masked = SecretSettingMask.MaskConfiguration(Json("""{"ApiToken":"material","Enabled":true}"""), []);

        Assert.Equal(Placeholder, masked.GetProperty("ApiToken").GetString());
        Assert.Equal(Placeholder, masked.GetProperty("Enabled").GetString());
    }

    [Fact]
    public void MaskHidesColonPathKeysEvenUnderADeclaredNonSecretSetting()
    {
        var masked = SecretSettingMask.MaskConfiguration(Json("""{"Key:primary":"material","Mode:sub":"material"}"""), Settings);

        Assert.Equal(Placeholder, masked.GetProperty("Key:primary").GetString());
        Assert.Equal(Placeholder, masked.GetProperty("Mode:sub").GetString());
    }

    [Fact]
    public void MaskKeepsANameHiddenWhenAnotherSourceDeclaresItPlain()
    {
        var masked = SecretSettingMask.MaskConfiguration(Json("""{"Key":"material"}"""), [Setting("Key", secret: false), Setting("key", secret: true)]);

        Assert.Equal(Placeholder, masked.GetProperty("Key").GetString());
    }

    [Fact]
    public void MaskReturnsNonObjectConfigurationUnchanged()
    {
        var masked = SecretSettingMask.MaskConfiguration(Json("\"material\""), Settings);

        Assert.Equal("material", masked.GetString());
    }

    [Fact]
    public void MaskDefaultsMasksOnlyNonEmptySecretDefaults()
    {
        var masked = SecretSettingMask.MaskDefaults(
        [
            Setting("Set", secret: true, Json("\"material\"")),
            Setting("Empty", secret: true, Json("\"\"")),
            Setting("Absent", secret: true),
            Setting("Undefined", secret: true, default(JsonElement)),
            Setting("Plain", secret: false, Json("\"fast\""))
        ]);

        Assert.Equal(Placeholder, DefaultOf(masked, "Set")?.GetString());
        Assert.Equal("", DefaultOf(masked, "Empty")?.GetString());
        Assert.Null(DefaultOf(masked, "Absent"));
        Assert.Equal(JsonValueKind.Undefined, DefaultOf(masked, "Undefined")?.ValueKind);
        Assert.Equal("fast", DefaultOf(masked, "Plain")?.GetString());
    }

    [Fact]
    public void RestorePutsTheStoredValueBackForAPlaceholderMatchingCaseInsensitively()
    {
        var restored = SecretSettingMask.Restore(
            Json($$"""{"Key":"{{Placeholder}}","Mode":"slow"}"""),
            Json("""{"key":{"k1":"material"},"Mode":"fast"}"""),
            Settings);

        Assert.Equal("material", restored.GetProperty("Key").GetProperty("k1").GetString());
        Assert.Equal("slow", restored.GetProperty("Mode").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"not-an-object\"")]
    [InlineData("""{"Mode":"fast"}""")]
    public void RestoreDropsAPlaceholderThatHasNoStoredValue(string? stored)
    {
        var restored = SecretSettingMask.Restore(
            Json($$"""{"Key":"{{Placeholder}}","Mode":"slow"}"""),
            stored is null ? null : Json(stored),
            Settings);

        Assert.False(restored.TryGetProperty("Key", out _));
        Assert.Equal("slow", restored.GetProperty("Mode").GetString());
    }

    [Theory]
    [InlineData("\"rotated\"")]
    [InlineData("""{"k1":"rotated"}""")]
    [InlineData("null")]
    public void RestoreKeepsASecretValueThatIsNotThePlaceholder(string value)
    {
        var restored = SecretSettingMask.Restore(Json($$"""{"Key":{{value}}}"""), Json("""{"Key":"material"}"""), Settings);

        Assert.Equal(Json(value).GetRawText(), restored.GetProperty("Key").GetRawText());
    }

    [Fact]
    public void RestorePutsBackAnUndeclaredHiddenValue()
    {
        var restored = SecretSettingMask.Restore(Json($$"""{"ApiToken":"{{Placeholder}}"}"""), Json("""{"ApiToken":"material"}"""), []);

        Assert.Equal("material", restored.GetProperty("ApiToken").GetString());
    }

    [Fact]
    public void RestoreLeavesThePlaceholderTextInANonSecretSetting()
    {
        var restored = SecretSettingMask.Restore(Json($$"""{"Mode":"{{Placeholder}}"}"""), Json("""{"Mode":"fast"}"""), Settings);

        Assert.Equal(Placeholder, restored.GetProperty("Mode").GetString());
    }

    [Fact]
    public void RestoreReturnsNonObjectRequestsUnchanged()
    {
        var restored = SecretSettingMask.Restore(Json("null"), Json("""{"Key":"material"}"""), Settings);

        Assert.Equal(JsonValueKind.Null, restored.ValueKind);
    }

    private static JsonElement? DefaultOf(IReadOnlyList<FeatureSettingDescriptor> settings, string name) =>
        Assert.Single(settings, x => x.Name == name).DefaultValue;

    private static FeatureSettingDescriptor Setting(string name, bool secret, JsonElement? defaultValue = null) =>
        new(name, name, null, null, null, null, "string", false, defaultValue, secret, false, false, false, false, null, null, []);

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();
}
