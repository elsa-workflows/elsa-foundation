using System.Text.Json;
using Elsa.Modularity.Planning.Json;

namespace Elsa.Modularity.Planning.Tests;

public sealed class SelectionDocumentTests
{
    [Fact]
    public void Catalog_and_authored_documents_round_trip_with_pins()
    {
        var group = PlannerFixture.Definition("group", "runtime-base", ["B", "A"]);
        var catalog = PlannerFixture.Catalog(groups: [group]);
        var parsedCatalog = SelectionJsonReader.ParseCatalog(PlannerFixture.Json(catalog));
        Assert.Equal(catalog.Digest, parsedCatalog.Digest);
        Assert.Equal(group.Digest, Assert.Single(parsedCatalog.Groups).Digest);

        var authored = PlannerFixture.Authored(catalog, groups: [PlannerFixture.Ref(group)], accepted: ["A", "B"]);
        var parsedAuthored = SelectionJsonReader.ParseComposition(PlannerFixture.Json(authored));
        Assert.Equal(catalog.Digest, parsedAuthored.Catalog.Digest);
        Assert.Equal(group.Digest, Assert.Single(parsedAuthored.Groups).Digest);
        Assert.Equal(new[] { "A", "B" }, parsedAuthored.Accepted.FeatureIds.ToArray());
    }

    [Fact]
    public void Formatting_and_property_order_do_not_change_catalog_digest()
    {
        var group = PlannerFixture.Definition("group", "g", ["B", "A"]);
        var catalog = PlannerFixture.Catalog(groups: [group]);
        var compact = PlannerFixture.Json(catalog);
        using var parsed = JsonDocument.Parse(compact);
        var indented = JsonSerializer.Serialize(parsed.RootElement, new JsonSerializerOptions { WriteIndented = true });
        Assert.Equal(SelectionJsonReader.ParseCatalog(compact).Digest, SelectionJsonReader.ParseCatalog(indented).Digest);
    }

    [Fact]
    public void Workspace_profile_is_a_flat_validated_snapshot()
    {
        var definition = PlannerFixture.Definition("profile", "custom-worker", ["A", "B"]);
        var json = PlannerFixture.Json(definition);
        var workspaceJson = "{\"schemaVersion\":\"1\"," + json[1..];
        var parsed = SelectionJsonReader.ParseWorkspaceProfile(workspaceJson);
        Assert.Equal(definition.Digest, parsed.Definition.Digest);
    }

    [Theory]
    [InlineData("duplicate-json-key")]
    [InlineData("digest-mismatch")]
    [InlineData("schema-unsupported")]
    [InlineData("duplicate-member")]
    [InlineData("invalid-unicode")]
    [InlineData("unknown-catalog-field")]
    public void Invalid_catalog_inputs_refuse_with_specific_codes(string expectedCode)
    {
        var group = PlannerFixture.Definition("group", "g", ["A"]);
        var catalog = PlannerFixture.Catalog(groups: [group]);
        var json = PlannerFixture.Json(catalog);
        json = expectedCode switch
        {
            "duplicate-json-key" => json.Replace("\"id\":\"foundation-fixtures\"", "\"id\":\"foundation-fixtures\",\"id\":\"again\"", StringComparison.Ordinal),
            "digest-mismatch" => json.Replace(catalog.Digest, new string('0', 64), StringComparison.Ordinal),
            "schema-unsupported" => json.Replace("\"schemaVersion\":\"1\"", "\"schemaVersion\":\"2\"", StringComparison.Ordinal),
            "duplicate-member" => json.Replace("\"members\":[\"A\"]", "\"members\":[\"A\",\"A\"]", StringComparison.Ordinal),
            "invalid-unicode" => json.Replace("\"title\":\"g\"", "\"title\":\"\\uD800\"", StringComparison.Ordinal),
            "unknown-catalog-field" => json.Replace("\"publisher\":\"elsa-foundation\"", "\"publisher\":\"elsa-foundation\",\"surprise\":\"x\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(expectedCode, Assert.Throws<SelectionDocumentException>(() => SelectionJsonReader.ParseCatalog(json)).Code);
    }

    [Fact]
    public void Unknown_settings_and_resources_are_preserved_but_not_promoted_to_selection_fields()
    {
        var catalog = PlannerFixture.Catalog();
        var authored = PlannerFixture.Authored(catalog);
        var json = PlannerFixture.Json(authored).Replace("\"settings\":null", "\"settings\":{\"future\":{\"secret\":\"SENSITIVE_SENTINEL\",\"zero\":0}}", StringComparison.Ordinal)
            .Replace("\"resources\":null", "\"resources\":{\"unknown\":false}", StringComparison.Ordinal);
        var parsed = SelectionJsonReader.ParseComposition(json);
        Assert.Contains("SENSITIVE_SENTINEL", parsed.Settings!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.False, parsed.Resources!.Value.GetProperty("unknown").ValueKind);
    }

    [Fact]
    public void Duplicate_definitions_and_reviewed_explanations_refuse_import()
    {
        var group = PlannerFixture.Definition("group", "g", ["A"], explanations: [new("A", "B", "optional", "reviewed")]);
        var catalog = PlannerFixture.Catalog(groups: [group]);
        var duplicateDefinition = catalog with { Groups = [group, group] };
        var duplicateExplanation = group with { DependencyExplanations = [group.DependencyExplanations[0], group.DependencyExplanations[0]] };
        var duplicateExplanationCatalog = catalog with { Groups = [duplicateExplanation] };

        Assert.Equal("duplicate-definition", Assert.Throws<SelectionDocumentException>(() => SelectionJsonReader.ParseCatalog(PlannerFixture.Json(duplicateDefinition))).Code);
        Assert.Equal("duplicate-explanation", Assert.Throws<SelectionDocumentException>(() => SelectionJsonReader.ParseCatalog(PlannerFixture.Json(duplicateExplanationCatalog))).Code);
    }

    [Fact]
    public void Duplicate_authored_ids_refuse_but_add_and_remove_overlap_is_valid()
    {
        var catalog = PlannerFixture.Catalog();
        var authored = PlannerFixture.Authored(catalog, add: ["A"], remove: ["A"]);
        Assert.NotNull(SelectionJsonReader.ParseComposition(PlannerFixture.Json(authored)));
        var duplicate = authored with { Add = ["A", "A"] };
        Assert.Equal("duplicate-authored-selection", Assert.Throws<SelectionDocumentException>(() => SelectionJsonReader.ParseComposition(PlannerFixture.Json(duplicate))).Code);
    }
}
