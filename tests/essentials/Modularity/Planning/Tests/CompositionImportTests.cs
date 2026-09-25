using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Json;

namespace Elsa.Modularity.Planning.Tests;

public sealed class CompositionImportTests
{
    private const string UnknownCanary = "COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET";
    private const string ConnectionCanary = "COMPOSITION_BRIDGE_CONNECTION_CANARY_NOT_A_SECRET";
    private const string FeatureConnectionCanary = "COMPOSITION_BRIDGE_FEATURE_CONNECTION_CANARY_NOT_A_SECRET";

    [Fact]
    public void Imports_exact_selection_and_only_reviewed_safe_values_without_leaking_local_configuration()
    {
        var result = Import();

        Assert.Equal("default", result.Preview.ShellId);
        Assert.Equal("Production", result.Preview.Environment);
        Assert.Equal(new[] { "A" }, result.Preview.EnabledFeatureIds);
        Assert.Equal(new[] { "B" }, result.Preview.DisabledFeatureIds);

        var flag = Setting(result.Preview, "A", "/Flag");
        Assert.Equal("boolean", flag.ValueKind);
        Assert.Equal("overlay", flag.SourceLayer);
        Assert.True(flag.Reviewed);
        Assert.True(flag.Portable);
        Assert.False(flag.Masked);
        Assert.Equal(JsonValueKind.True, flag.PortableValue!.Value.ValueKind);

        var limit = Setting(result.Preview, "A", "/Limit");
        Assert.Equal("number", limit.ValueKind);
        Assert.Equal("base", limit.SourceLayer);
        Assert.Equal(0, limit.PortableValue!.Value.GetInt32());

        var explicitNull = Setting(result.Preview, "A", "/Future/x");
        Assert.True(explicitNull.Present);
        Assert.Equal("null", explicitNull.ValueKind);
        Assert.True(explicitNull.Masked);
        Assert.Null(explicitNull.PortableValue);

        Assert.True(Setting(result.Preview, "A", "/Future/Canary").Masked);
        Assert.True(Setting(result.Preview, "A", "/Provider").Masked);
        Assert.True(Setting(result.Preview, "A", "/ConnectionString").Masked);

        Assert.Equal("primary", result.Preview.DefaultResource);
        Assert.Equal("shell-configuration-base", result.Preview.DefaultResourceSource);
        var binding = Assert.Single(result.Preview.Bindings);
        Assert.Equal("A", binding.FeatureId);
        Assert.Equal("primary", binding.ResourceName);

        Assert.Equal(new[] { "A" }, result.Authored.Add);
        Assert.Equal(new[] { "B" }, result.Authored.Remove);
        Assert.Null(result.Authored.Profile);
        Assert.Empty(result.Authored.Groups);
        Assert.Empty(result.Authored.Accepted.Locks);
        Assert.Equal(result.Authored.Catalog.Digest, result.Authored.Accepted.CatalogDigest);
        Assert.Equal(new[] { "A" }, result.Authored.Accepted.FeatureIds);
        Assert.Equal(new[] { "A" }, result.Plan.SelectedFeatureIds);
        Assert.Empty(result.Plan.ObservedLocks);
        Assert.Equal("unchecked", result.Plan.Persistence.Status);
        Assert.Equal(new[] { "primary" }, result.Plan.Persistence.ResourceReferences);

        var authoredJson = JsonSerializer.Serialize(result.Authored, JsonOptions);
        var parsedAuthored = SelectionJsonReader.ParseComposition(authoredJson);
        Assert.Equal(JsonValueKind.True, parsedAuthored.Settings!.Value.GetProperty("A").GetProperty("Flag").ValueKind);
        Assert.Equal(0, parsedAuthored.Settings.Value.GetProperty("A").GetProperty("Limit").GetInt32());
        Assert.Equal("primary", parsedAuthored.Resources!.Value.GetProperty("persistence").GetProperty("defaultResource").GetString());
        Assert.Equal("primary", parsedAuthored.Resources.Value.GetProperty("persistence").GetProperty("bindings").GetProperty("A").GetString());

        var redacted = JsonSerializer.Serialize(new { result.Preview, result.Plan }, JsonOptions);
        Assert.DoesNotContain(UnknownCanary, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(ConnectionCanary, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(FeatureConnectionCanary, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("PostgreSql", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("Shared", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(UnknownCanary, authoredJson, StringComparison.Ordinal);
        Assert.DoesNotContain(ConnectionCanary, authoredJson, StringComparison.Ordinal);
        Assert.DoesNotContain(FeatureConnectionCanary, authoredJson, StringComparison.Ordinal);
        Assert.DoesNotContain("PostgreSql", authoredJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Shared", authoredJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Omitting_setting_review_keeps_every_setting_local_and_masked()
    {
        var result = CompositionImporter.Import(
            BaseShells,
            ProductionShells,
            BaseAppsettings,
            ProductionAppsettings,
            "default",
            "Production",
            PlannerFixture.Catalog());

        Assert.Null(result.Authored.Settings);
        Assert.All(result.Preview.Settings, setting =>
        {
            Assert.True(setting.Masked);
            Assert.Null(setting.PortableValue);
        });
    }

    [Fact]
    public void Rejects_review_type_mismatch_before_importing_a_value()
    {
        var review = Review("string");

        var exception = Assert.Throws<CompositionImportException>(() => Import(review));

        Assert.Equal("bridge-portable-unsafe", exception.Code);
        Assert.DoesNotContain("false", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refuses_a_logical_resource_reference_without_a_selected_local_definition()
    {
        const string missingResource = """
            { "Elsa": { "Persistence": { "Resources": {} } } }
            """;

        var exception = Assert.Throws<CompositionImportException>(() => CompositionImporter.Import(
            BaseShells,
            ProductionShells,
            missingResource,
            ProductionAppsettings,
            "default",
            "Production",
            PlannerFixture.Catalog(),
            SettingReviewReader.Parse(ReviewJson)));

        Assert.Equal("bridge-mapping-unresolved", exception.Code);
        Assert.DoesNotContain("primary", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_portable_leaf_inside_a_nonempty_array_until_array_shape_can_be_preserved()
    {
        const string shells = """
            {"CShells":{"Shells":{"default":{"Features":{"A":{"Items":[7]}}}}}}
            """;
        var review = SettingReviewReader.Parse("""
            {"schemaVersion":"1","fields":[{"featureId":"A","pointer":"/Items/0","type":"number","portable":true}]}
            """);

        var exception = Assert.Throws<CompositionImportException>(() => CompositionImporter.Import(
            shells, null, "{}", null, "default", "Production", PlannerFixture.Catalog(), review));

        Assert.Equal("bridge-portable-unsafe", exception.Code);
    }

    [Fact]
    public void Never_projects_a_reviewed_value_below_a_physical_connection_tree()
    {
        const string shells = """
            {"CShells":{"Shells":{"default":{"Features":{"A":{"ConnectionStrings":{"Shared":"connection-canary"}}}}}}}
            """;
        var review = SettingReviewReader.Parse("""
            {"schemaVersion":"1","fields":[{"featureId":"A","pointer":"/ConnectionStrings/Shared","type":"string","portable":true}]}
            """);

        var result = CompositionImporter.Import(
            shells, null, "{}", null, "default", "Production", PlannerFixture.Catalog(), review);

        Assert.Null(result.Authored.Settings);
        var field = Setting(result.Preview, "A", "/ConnectionStrings/Shared");
        Assert.True(field.Masked);
        Assert.Null(field.PortableValue);
        Assert.DoesNotContain("connection-canary", JsonSerializer.Serialize(result.Preview), StringComparison.Ordinal);
    }

    private static CompositionImportResult Import(SettingReviewDocument? review = null) =>
        CompositionImporter.Import(
            BaseShells,
            ProductionShells,
            BaseAppsettings,
            ProductionAppsettings,
            "default",
            "Production",
            PlannerFixture.Catalog(),
            review ?? SettingReviewReader.Parse(ReviewJson));

    private static CompositionImportSetting Setting(CompositionImportPreview preview, string featureId, string pointer) =>
        Assert.Single(preview.Settings, setting => setting.FeatureId == featureId && setting.Pointer == pointer);

    private static SettingReviewDocument Review(string flagType) => SettingReviewReader.Parse($$"""
        {
          "schemaVersion": "1",
          "fields": [
            { "featureId": "A", "pointer": "/Flag", "type": "{{flagType}}", "portable": true },
            { "featureId": "A", "pointer": "/Limit", "type": "number", "portable": true },
            { "featureId": "A", "pointer": "/Provider", "type": "string", "portable": true },
            { "featureId": "A", "pointer": "/ConnectionString", "type": "string", "portable": true }
          ]
        }
        """);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const string ReviewJson = """
        {
          "schemaVersion": "1",
          "fields": [
            { "featureId": "A", "pointer": "/Flag", "type": "boolean", "portable": true },
            { "featureId": "A", "pointer": "/Limit", "type": "number", "portable": true },
            { "featureId": "A", "pointer": "/Provider", "type": "string", "portable": true },
            { "featureId": "A", "pointer": "/ConnectionString", "type": "string", "portable": true }
          ]
        }
        """;

    private const string BaseShells = """
        {
          "CShells": { "Shells": {
            "default": {
              "Features": {
                "A": {
                  "Flag": false,
                  "Limit": 0,
                  "Future": { "x": null, "Canary": "COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET" },
                  "Provider": "PostgreSql",
                  "ConnectionString": "COMPOSITION_BRIDGE_FEATURE_CONNECTION_CANARY_NOT_A_SECRET"
                },
                "B": false
              },
              "Configuration": { "Elsa": { "Persistence": {
                "DefaultResource": "primary",
                "Bindings": { "A": "primary" }
              } } }
            },
            "tenant-b": { "Features": { "TenantOnly": { "Keep": "unchanged" } } }
          } },
          "CustomRoot": { "Keep": true }
        }
        """;

    private const string ProductionShells = """
        {
          "CShells": { "Shells": {
            "default": { "Features": { "A": { "Flag": true } } }
          } },
          "ProductionAnnotation": { "Keep": "preserved" }
        }
        """;

    private const string BaseAppsettings = """
        {
          "ConnectionStrings": { "Shared": "COMPOSITION_BRIDGE_CONNECTION_CANARY_NOT_A_SECRET" },
          "Elsa": { "Persistence": {
            "DefaultResource": "root-default",
            "Resources": {
              "primary": { "Provider": "PostgreSql", "ConnectionName": "Shared" },
              "root-default": { "Provider": "Sqlite", "ConnectionName": "Local" }
            }
          } },
          "CustomAppRoot": { "Keep": "unchanged" }
        }
        """;

    private const string ProductionAppsettings = """
        { "EnvironmentAnnotation": { "Keep": "production-value" } }
        """;
}
