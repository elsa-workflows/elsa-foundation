using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Tests;

internal static class PlannerFixture
{
    public static SelectionDefinition Definition(
        string kind,
        string id,
        IEnumerable<string> members,
        string version = "1",
        string rationale = "Reviewed planning fixture",
        IEnumerable<DependencyExplanation>? explanations = null,
        string? title = null,
        string description = "Planning fixture only")
    {
        var draft = new SelectionDefinition(kind, id, version, new string('0', 64), members.ToImmutableArray(), rationale, title ?? id, description, (explanations ?? []).ToImmutableArray());
        return draft with { Digest = SelectionDigest.ComputeDefinitionDigest(draft) };
    }

    public static SelectionCatalog Catalog(
        IEnumerable<SelectionDefinition>? profiles = null,
        IEnumerable<SelectionDefinition>? groups = null,
        string version = "1")
    {
        var draft = new SelectionCatalog("1", "foundation-fixtures", version, "elsa-foundation", new string('0', 64), (profiles ?? []).ToImmutableArray(), (groups ?? []).ToImmutableArray());
        return draft with { Digest = SelectionDigest.ComputeCatalogDigest(draft) };
    }

    public static DefinitionReference Ref(SelectionDefinition definition, string origin = "foundation") =>
        new(origin, definition.Kind, definition.Id, definition.Version, definition.Digest);

    public static AuthoredComposition Authored(
        SelectionCatalog catalog,
        DefinitionReference? profile = null,
        IEnumerable<DefinitionReference>? groups = null,
        IEnumerable<string>? add = null,
        IEnumerable<string>? remove = null,
        IEnumerable<string>? accepted = null,
        JsonElement? settings = null,
        JsonElement? resources = null) =>
        new("1", new CatalogPin(catalog.Id, catalog.Version, catalog.Digest), profile,
            (groups ?? []).ToImmutableArray(), (add ?? []).ToImmutableArray(), (remove ?? []).ToImmutableArray(),
            new AcceptedSelection(catalog.Digest, (accepted ?? []).ToImmutableArray(), []), settings, resources);

    public static string Json<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    public static HostInventory Inventory(params InventoryFeature[] features) =>
        new("inventory-1", "test-host", new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero), "test-snapshot", features.ToImmutableArray());

    public static InventoryFeature Bundled(string id, IEnumerable<string>? runtimeDependencies = null, IEnumerable<InventoryDependency>? manifestDependencies = null) =>
        new(id, "loaded", runtimeDependencies?.ToImmutableArray() ?? ImmutableArray<string>.Empty,
            manifestDependencies?.ToImmutableArray(), "read", null, true, "compatible", "runtime-descriptor");

    public static InventoryFeature Packaged(string id, string availability = "installed", string manifestReadStatus = "read", string compatibility = "compatible", IEnumerable<InventoryDependency>? manifestDependencies = null) =>
        new(id, availability, null, manifestDependencies?.ToImmutableArray(), manifestReadStatus,
            new InventoryPackage("Elsa.Test." + id, "1.0.0", new string('a', 64)), false, compatibility, "installed-manifest");
}
