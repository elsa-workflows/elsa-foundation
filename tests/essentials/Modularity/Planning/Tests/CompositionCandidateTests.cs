using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Tests;

namespace Elsa.Modularity.Planning.Tests;

public sealed class CompositionCandidateTests
{
    [Fact]
    public void Patches_a_reviewed_existing_field_and_preserves_every_supported_source_file()
    {
        var (snapshot, catalog, authored, review) = Fixture();
        authored = WithSettings(authored, """{"A":{"Flag":true,"Limit":1}}""");

        var candidate = CompositionCandidateBuilder.Build(snapshot, catalog, authored, review);

        Assert.Equal(snapshot.FileNames.Order(StringComparer.Ordinal), candidate.Files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(1, JsonDocument.Parse(candidate.Files["shells.json"]).RootElement
            .GetProperty("CShells").GetProperty("Shells").GetProperty("default").GetProperty("Features").GetProperty("A").GetProperty("Limit").GetInt32());
        Assert.Equal("COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET", JsonDocument.Parse(candidate.Files["shells.json"]).RootElement
            .GetProperty("CShells").GetProperty("Shells").GetProperty("default").GetProperty("Features").GetProperty("A").GetProperty("Future").GetProperty("Canary").GetString());
        Assert.Equal("production annotation", Text(candidate, "shells.Production.json", "ProductionAnnotation", "Keep"));
        Assert.Equal("staging annotation", Text(candidate, "shells.Staging.json", "StagingAnnotation", "Keep"));
        Assert.Equal("app root", Text(candidate, "appsettings.json", "CustomAppRoot", "Keep"));
        Assert.Equal("staging settings", Text(candidate, "appsettings.Staging.json", "StagingAnnotation", "Keep"));
        Assert.Equal("COMPOSITION_BRIDGE_CONNECTION_CANARY_NOT_A_SECRET", Text(candidate, "appsettings.json", "ConnectionStrings", "Shared"));
        Assert.DoesNotContain("COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET", JsonSerializer.Serialize(candidate.Changes));
        Assert.DoesNotContain("COMPOSITION_BRIDGE_CONNECTION_CANARY_NOT_A_SECRET", JsonSerializer.Serialize(candidate.Changes));
        Assert.Contains(candidate.Changes, change => change.FeatureId == "A" && change.Pointer == "/Limit" && change.SourceLayer == "base");
    }

    [Fact]
    public void Patches_mixed_case_selected_overlay_path_without_changing_its_spelling()
    {
        var (snapshot, catalog, authored, review) = Fixture(mixedCaseOverlay: true);
        authored = WithSettings(authored, """{"A":{"Flag":false,"Limit":0}}""");

        var candidate = CompositionCandidateBuilder.Build(snapshot, catalog, authored, review);
        using var overlay = JsonDocument.Parse(candidate.Files["shells.Production.json"]);
        var item = overlay.RootElement.GetProperty("CShells").GetProperty("Shells").GetProperty("default")
            .GetProperty("Features").GetProperty("A").GetProperty("fLaG");
        Assert.False(item.GetBoolean());
        Assert.Contains("Features", overlay.RootElement.GetProperty("CShells").GetProperty("Shells").GetProperty("default").EnumerateObject().Select(x => x.Name));
    }

    [Fact]
    public void Patches_existing_logical_default_and_feature_binding_references()
    {
        var (snapshot, catalog, authored, review) = Fixture();
        authored = WithResources(authored, """{"persistence":{"defaultResource":"secondary","bindings":{"A":"secondary"}}}""");

        var candidate = CompositionCandidateBuilder.Build(snapshot, catalog, authored, review);
        using var baseDocument = JsonDocument.Parse(candidate.Files["shells.json"]);
        var persistence = baseDocument.RootElement.GetProperty("CShells").GetProperty("Shells").GetProperty("default")
            .GetProperty("Configuration").GetProperty("Elsa").GetProperty("Persistence");
        Assert.Equal("secondary", persistence.GetProperty("DefaultResource").GetString());
        Assert.Equal("secondary", persistence.GetProperty("Bindings").GetProperty("A").GetString());
        Assert.Contains(candidate.Changes, change => change.Pointer == "/persistence/defaultResource" && change.ValueType == "resource-name" && change.Changed);
        Assert.DoesNotContain("secondary", JsonSerializer.Serialize(candidate.Changes));
    }

    [Fact]
    public void Maps_existing_array_indexes_but_refuses_an_unmapped_array_index()
    {
        var (snapshot, catalog, authored, _) = Fixture(arraySettings: true);
        var review = SettingReviewReader.Parse("""
            {"schemaVersion":"1","fields":[{"featureId":"A","pointer":"/Items/0","type":"number","portable":true},{"featureId":"A","pointer":"/Items/1","type":"number","portable":true}]}
            """);
        authored = WithSettings(authored, """{"A":{"Items":[8]}}""");
        var candidate = CompositionCandidateBuilder.Build(snapshot, catalog, authored, review);
        using var document = JsonDocument.Parse(candidate.Files["shells.json"]);
        Assert.Equal(8, document.RootElement.GetProperty("CShells").GetProperty("Shells").GetProperty("default")
            .GetProperty("Features").GetProperty("A").GetProperty("Items")[0].GetInt32());

        authored = WithSettings(authored, """{"A":{"Items":[8,9]}}""");
        var exception = Assert.Throws<CompositionImportException>(() => CompositionCandidateBuilder.Build(snapshot, catalog, authored, review));
        Assert.Equal("bridge-mapping-unresolved", exception.Code);
    }

    [Fact]
    public void Refuses_selection_drift_wrong_type_unreviewed_or_new_setting_paths_and_missing_resources()
    {
        var (snapshot, catalog, authored, review) = Fixture();
        var drift = authored with { Accepted = authored.Accepted with { FeatureIds = [] } };
        AssertCode("bridge-selection-drift", () => CompositionCandidateBuilder.Build(snapshot, catalog, drift, review));
        var changedSelection = PlannerFixture.Authored(catalog, add: ["A", "C"], remove: ["B"], accepted: ["A", "C"],
            settings: authored.Settings, resources: authored.Resources);
        AssertCode("bridge-selection-drift", () => CompositionCandidateBuilder.Build(snapshot, catalog, changedSelection, review));
        AssertCode("bridge-catalog-mismatch", () => CompositionCandidateBuilder.Build(snapshot, catalog,
            authored with { Catalog = authored.Catalog with { Version = "2" } }, review));

        AssertCode("bridge-portable-unsafe", () => CompositionCandidateBuilder.Build(snapshot, catalog,
            WithSettings(authored, """{"A":{"Limit":"secret-looking"}}"""), review));
        var wrongSourceTypeReview = SettingReviewReader.Parse("""
            {"schemaVersion":"1","fields":[{"featureId":"A","pointer":"/Limit","type":"string","portable":true}]}
            """);
        AssertCode("bridge-portable-unsafe", () => CompositionCandidateBuilder.Build(snapshot, catalog,
            WithSettings(authored, """{"A":{"Limit":"1"}}"""), wrongSourceTypeReview));
        AssertCode("bridge-portable-unsafe", () => CompositionCandidateBuilder.Build(snapshot, catalog,
            WithSettings(authored, """{"A":{"Future":{"Canary":"hidden"}}}"""), review));

        var newPathReview = SettingReviewReader.Parse("""
            {"schemaVersion":"1","fields":[{"featureId":"A","pointer":"/NeverThere","type":"string","portable":true}]}
            """);
        AssertCode("bridge-mapping-unresolved", () => CompositionCandidateBuilder.Build(snapshot, catalog,
            WithSettings(authored, """{"A":{"NeverThere":"hidden"}}"""), newPathReview));

        var missingResource = WithResources(authored, """{"persistence":{"defaultResource":"missing"}}""");
        var resourceError = Assert.Throws<CompositionImportException>(() => CompositionCandidateBuilder.Build(snapshot, catalog, missingResource, review));
        Assert.Equal("bridge-mapping-unresolved", resourceError.Code);
        Assert.DoesNotContain("missing", resourceError.Message, StringComparison.Ordinal);
        AssertCode("bridge-portable-unsafe", () => CompositionCandidateBuilder.Build(snapshot, catalog,
            WithResources(authored, """{"persistence":{"defaultResource":"primary"},"vendor":{"value":"unknown"}}"""), review));
        AssertCode("bridge-portable-unsafe", () => CompositionCandidateBuilder.Build(snapshot, catalog,
            WithResources(authored, """{"persistence":{"bindings":{"A":"primary","a":"primary"}}}"""), review));

        var connectionReview = SettingReviewReader.Parse("""
            {"schemaVersion":"1","fields":[{"featureId":"A","pointer":"/ConnectionString","type":"string","portable":true}]}
            """);
        AssertCode("bridge-portable-unsafe", () => CompositionCandidateBuilder.Build(snapshot, catalog,
            WithSettings(authored, """{"A":{"ConnectionString":"do-not-disclose"}}"""), connectionReview));
    }

    private static (SourceSnapshot Snapshot, SelectionCatalog Catalog, AuthoredComposition Authored, SettingReviewDocument Review) Fixture(
        bool mixedCaseOverlay = false,
        bool arraySettings = false)
    {
        const string baseShells = """
            {"CShells":{"Shells":{"default":{"Features":{"A":{"Flag":false,"Limit":0,"Future":{"Canary":"COMPOSITION_BRIDGE_UNKNOWN_CANARY_NOT_A_SECRET"},"Items":[7]},"B":false},"Configuration":{"Elsa":{"Persistence":{"DefaultResource":"primary","Bindings":{"A":"primary"}}}}}}},"CustomRoot":{"Keep":true}}
            """;
        var production = mixedCaseOverlay
            ? """{"CShells":{"Shells":{"default":{"Features":{"A":{"fLaG":true}}}}},"ProductionAnnotation":{"Keep":"production annotation"}}"""
            : """{"CShells":{"Shells":{"default":{"Features":{"A":{"Flag":true}}}}},"ProductionAnnotation":{"Keep":"production annotation"}}""";
        _ = CshellsSourceReader.Read(baseShells, production, "default");
        const string appsettings = """
            {"ConnectionStrings":{"Shared":"COMPOSITION_BRIDGE_CONNECTION_CANARY_NOT_A_SECRET"},"Elsa":{"Persistence":{"Resources":{"primary":{"Provider":"Sqlite"},"secondary":{"Provider":"Sqlite"}}}},"CustomAppRoot":{"Keep":"app root"}}
            """;
        const string productionAppsettings = "{}";
        const string stagingShells = """
            {"StagingAnnotation":{"Keep":"staging annotation"}}
            """;
        const string stagingAppsettings = """
            {"StagingAnnotation":{"Keep":"staging settings"}}
            """;
        var catalog = PlannerFixture.Catalog();
        var settings = arraySettings ? """{"A":{"Items":[7]}}""" : """{"A":{"Flag":true,"Limit":0}}""";
        var authored = PlannerFixture.Authored(catalog, add: ["A"], remove: ["B"], accepted: ["A"], settings: Element(settings),
            resources: Element("""{"persistence":{"defaultResource":"primary","bindings":{"A":"primary"}}}"""));
        var reviewJson = arraySettings
            ? """{"schemaVersion":"1","fields":[{"featureId":"A","pointer":"/Items/0","type":"number","portable":true}]}"""
            : """{"schemaVersion":"1","fields":[{"featureId":"A","pointer":"/Flag","type":"boolean","portable":true},{"featureId":"A","pointer":"/Limit","type":"number","portable":true}]}""";
        var selection = new SourceSelection("default", "Production", "shells.Production.json", "appsettings.Production.json");
        var snapshot = SourceSnapshot.Freeze(selection,
        [
            Pair("shells.json", baseShells), Pair("shells.Production.json", production), Pair("shells.Staging.json", stagingShells),
            Pair("appsettings.json", appsettings), Pair("appsettings.Production.json", productionAppsettings), Pair("appsettings.Staging.json", stagingAppsettings)
        ]);
        return (snapshot, catalog, authored, SettingReviewReader.Parse(reviewJson));
    }

    private static AuthoredComposition WithSettings(AuthoredComposition authored, string json) => authored with { Settings = Element(json) };
    private static AuthoredComposition WithResources(AuthoredComposition authored, string json) => authored with { Resources = Element(json) };
    private static JsonElement Element(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
    private static KeyValuePair<string, byte[]> Pair(string name, string content) => new(name, System.Text.Encoding.UTF8.GetBytes(content));
    private static string Text(CompositionCandidate candidate, string filename, params string[] path)
    {
        using var document = JsonDocument.Parse(candidate.Files[filename]);
        var value = document.RootElement;
        foreach (var segment in path) value = value.GetProperty(segment);
        return value.GetString()!;
    }
    private static void AssertCode(string code, Action action) => Assert.Equal(code, Assert.Throws<CompositionImportException>(action).Code);
}
