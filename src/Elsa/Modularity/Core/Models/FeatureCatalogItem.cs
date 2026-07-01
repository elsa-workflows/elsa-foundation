using System.Text.Json;

namespace Elsa.Modularity.Core.Models;

public sealed record FeatureCatalogResponse(
    string Revision,
    IReadOnlyList<FeatureCatalogItem> Features);

public sealed record FeatureCatalogItem(
    string Id,
    string DisplayName,
    string? Description,
    IReadOnlyList<string> Categories,
    string SourceKind,
    string? PackageId,
    string? PackageVersion,
    bool Enabled,
    JsonElement Configuration,
    bool Advanced,
    bool Experimental,
    string? ManifestPath,
    string? ManifestHash,
    string? ReadError,
    IReadOnlyList<FeatureSettingDescriptor> Settings,
    IReadOnlyList<string> Dependencies);

public sealed class FeatureCatalogItemBuilder
{
    public string Id { get; init; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<string> Categories { get; set; } = [];
    public string SourceKind { get; set; } = FeatureSourceKinds.Shell;
    public string? PackageId { get; set; }
    public string? PackageVersion { get; set; }
    public bool Enabled { get; set; }
    public JsonElement Configuration { get; set; } = JsonDocument.Parse("{}").RootElement.Clone();
    public bool Advanced { get; set; }
    public bool Experimental { get; set; }
    public string? ManifestPath { get; set; }
    public string? ManifestHash { get; set; }
    public string? ReadError { get; set; }
    public IReadOnlyList<FeatureSettingDescriptor> Settings { get; set; } = [];
    public IReadOnlyList<string> Dependencies { get; set; } = [];

    public FeatureCatalogItem ToItem() =>
        new(
            Id,
            string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName!,
            Description,
            Categories,
            SourceKind,
            PackageId,
            PackageVersion,
            Enabled,
            Configuration.Clone(),
            Advanced,
            Experimental,
            ManifestPath,
            ManifestHash,
            ReadError,
            Settings,
            Dependencies);
}
