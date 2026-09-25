using System.Text.Json;
using Elsa.Modularity.Planning.Bridge;

namespace Elsa.Modularity.Planning.Tests;

public sealed class CompositionBridgeSourceTests
{
    private const string UnknownCanary = "COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET";

    [Fact]
    public void Reads_two_shell_object_map_layers_with_exact_state_provenance_and_raw_values()
    {
        var source = CshellsSourceReader.Read(BaseShells, ProductionShells, "default");

        Assert.Equal(CshellsFeatureShape.ObjectMap, source.FeatureShape);
        Assert.Equal(new[] { "A" }, source.EnabledFeatureIds);
        Assert.Equal(new[] { "B" }, source.DisabledFeatureIds);
        Assert.Equal("primary", source.BaseConfiguration!.Value.GetProperty("Elsa").GetProperty("Persistence").GetProperty("DefaultResource").GetString());
        Assert.Equal("primary", source.BaseConfiguration.Value.GetProperty("Elsa").GetProperty("Persistence").GetProperty("Bindings").GetProperty("A").GetString());
        Assert.Equal("selected-overlay", source.OverlayConfiguration!.Value.GetProperty("CompositionFixture").GetProperty("Layer").GetString());
        AssertSetting(source, "A", "/Flag", JsonValueKind.True, CshellsSourceLayer.Overlay);
        AssertSetting(source, "A", "/EnabledSetting", JsonValueKind.False, CshellsSourceLayer.Base);
        AssertSetting(source, "A", "/Limit", JsonValueKind.Number, CshellsSourceLayer.Base);
        AssertSetting(source, "A", "/Future/x", JsonValueKind.Null, CshellsSourceLayer.Base);
        AssertSetting(source, "A", "/Future/Canary", JsonValueKind.String, CshellsSourceLayer.Base, UnknownCanary);
        AssertSetting(source, "A", "/EmptyString", JsonValueKind.String, CshellsSourceLayer.Base, "");
        AssertSetting(source, "A", "/EmptyObject", JsonValueKind.Object, CshellsSourceLayer.Base);
        AssertSetting(source, "A", "/EmptyArray", JsonValueKind.Array, CshellsSourceLayer.Base);
        Assert.Equal(0, Find(source, "A", "/Limit").Value.GetInt32());
        Assert.DoesNotContain("tenant-b", source.EnabledFeatureIds);
    }

    [Fact]
    public void Object_map_true_override_clears_disabled_state_and_has_no_stale_settings()
    {
        const string baseJson = """
            {"CShells":{"Shells":{"default":{"Features":{"A":false,"B":true}}}}}
            """;
        const string overlayJson = """
            {"CShells":{"Shells":{"default":{"Features":{"A":true,"B":false}}}}}
            """;

        var source = CshellsSourceReader.Read(baseJson, overlayJson, "default");

        Assert.Equal(new[] { "A" }, source.EnabledFeatureIds);
        Assert.Equal(new[] { "B" }, source.DisabledFeatureIds);
        Assert.Empty(source.SettingLeaves);
    }

    [Fact]
    public void Array_overlay_replaces_numeric_indexes_and_does_not_treat_enabled_as_activation_state()
    {
        const string baseJson = """
            {"CShells":{"Shells":{"default":{"Features":[{"Name":"A","Enabled":false},"B"]}}}}
            """;
        const string overlayJson = """
            {"CShells":{"Shells":{"default":{"Features":[{"Name":"C","State":false}]}}}}
            """;

        var source = CshellsSourceReader.Read(baseJson, overlayJson, "default");

        Assert.Equal(CshellsFeatureShape.Array, source.FeatureShape);
        Assert.Equal(new[] { "B", "C" }, source.EnabledFeatureIds);
        Assert.Empty(source.DisabledFeatureIds);
        AssertSetting(source, "C", "/State", JsonValueKind.False, CshellsSourceLayer.Overlay);
        Assert.DoesNotContain(source.SettingLeaves, item => item.FeatureId == "B");
    }

    [Theory]
    [InlineData("{\"CShells\":{\"Shells\":{\"default\":{\"Features\":{\"A\":{},\"a\":{}}}}}}", null, "default", "bridge-source-duplicate")]
    [InlineData("{\"CShells\":{\"Shells\":{\"default\":{\"Features\":{\"A\":{}}}}},\"CShells\":{}}", null, "default", "bridge-source-duplicate")]
    [InlineData("{\"CShells\":{\"Shells\":{\"default\":{\"Features\":{\"A\":{}}}}}}", "{\"CShells\":{\"Shells\":{\"default\":{\"Features\":[\"A\"]}}}}", "default", "bridge-source-invalid")]
    [InlineData("{\"CShells\":{\"Shells\":{\"default\":{\"Features\":{\"unsafe;id\":true}}}}}", null, "default", "bridge-source-invalid")]
    [InlineData("{\"CShells\":{\"Shells\":{\"default\":{\"Features\":[\"A\",\"B\"]}}}}", "{\"CShells\":{\"Shells\":{\"default\":{\"Features\":[\"B\"]}}}}", "default", "bridge-source-duplicate")]
    [InlineData("{\"CShells\":{\"Shells\":{\"default\":{\"Features\":{\"A\":false}}}}}", "{\"CShells\":{\"Shells\":{\"default\":{\"Features\":{\"A\":{\"Flag\":true}}}}}}", "default", "bridge-source-invalid")]
    [InlineData("{\"CShells\":{\"Shells\":{\"default\":{\"Features\":{\"A\":{\"Flag\":true}}}}}}", "{\"CShells\":{\"Shells\":{\"default\":{\"Features\":{\"A\":false}}}}}", "default", "bridge-source-invalid")]
    public void Refuses_duplicate_or_unsupported_source_layers_without_echoing_values(
        string baseJson, string? overlayJson, string shellId, string expectedCode)
    {
        var exception = Assert.Throws<CshellsSourceException>(() => CshellsSourceReader.Read(baseJson, overlayJson, shellId));

        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain(UnknownCanary, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe;id", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"CShells\":{\"Shells\":{\"default\":{\"Features\":{\"A\":{\"Flag\":\"COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET\"}}}}}}")]
    public void Invalid_json_and_malformed_selected_shell_use_safe_refusal(string json)
    {
        var exception = Assert.Throws<CshellsSourceException>(() => CshellsSourceReader.Read(json, null, "missing"));

        Assert.Equal("bridge-source-invalid", exception.Code);
        Assert.DoesNotContain(UnknownCanary, exception.Message, StringComparison.Ordinal);
    }

    private static CshellsSettingLeaf Find(CshellsSource source, string featureId, string pointer) =>
        Assert.Single(source.SettingLeaves, item => item.FeatureId == featureId && item.Pointer == pointer);

    private static void AssertSetting(
        CshellsSource source,
        string featureId,
        string pointer,
        JsonValueKind kind,
        CshellsSourceLayer layer,
        string? stringValue = null)
    {
        var leaf = Find(source, featureId, pointer);
        Assert.Equal(kind, leaf.Value.ValueKind);
        Assert.Equal(layer, leaf.SourceLayer);
        if (kind == JsonValueKind.String)
            Assert.Equal(stringValue, leaf.Value.GetString());
    }

    private const string BaseShells = """
        {
          "CShells": {
            "Shells": {
              "default": {
                "Features": {
                  "A": {
                    "Flag": false,
                    "EnabledSetting": false,
                    "Limit": 0,
                    "Future": { "x": null, "Canary": "COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET" },
                    "EmptyString": "",
                    "EmptyObject": {},
                    "EmptyArray": []
                  },
                  "B": false
                },
                "Configuration": {
                  "Elsa": { "Persistence": { "DefaultResource": "primary", "Bindings": { "A": "primary" } } }
                }
              },
              "tenant-b": { "Features": { "TenantOnly": { "Keep": "unchanged" } } }
            }
          },
          "CustomRoot": { "Keep": true }
        }
        """;

    private const string ProductionShells = """
        {
          "CShells": {
            "Shells": {
              "default": {
                "Features": { "A": { "Flag": true } },
                "Configuration": { "CompositionFixture": { "Layer": "selected-overlay" } }
              }
            }
          },
          "ProductionAnnotation": { "Keep": "preserved" }
        }
        """;
}
