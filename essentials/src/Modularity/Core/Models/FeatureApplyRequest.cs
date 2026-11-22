using System.Text.Json;

namespace Elsa.Modularity.Core.Models;

public sealed record FeatureApplyRequest(
    string Revision,
    IReadOnlyList<FeatureApplyItem> Features);

public sealed record FeatureApplyItem(
    string Id,
    bool Enabled,
    JsonElement Configuration);

public sealed record FeatureApplyResult(
    FeatureCatalogResponse Catalog,
    int FeatureDescriptorCount,
    int ReloadedShellCount);
