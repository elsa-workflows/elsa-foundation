using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Modularity.Nuplane.Manifest;

internal sealed record PackageManifest(PackageIdentity? Package, IReadOnlyList<PackageFeatureManifest>? Features);

internal sealed record PackageIdentity(string? Id, string? Version);

internal sealed record PackageFeatureManifest(
    string? Id,
    string? DisplayName,
    string? Description,
    string? Category,
    IReadOnlyList<string>? Categories,
    bool Advanced,
    bool Experimental,
    IReadOnlyList<PackageFeatureSettingManifest>? Settings,
    JsonObject? Extensions);

internal sealed record PackageFeatureSettingManifest(
    string? Name,
    string? ClrType,
    string? JsonType,
    bool Required,
    JsonElement? DefaultValue,
    string? DisplayName,
    string? Description,
    string? Category,
    string? Group,
    bool Secret,
    bool Sensitive,
    bool RestartRequired,
    PackageFeatureSettingValidationManifest? Validation,
    PackageFeatureSettingUiManifest? Ui,
    JsonObject? Extensions);

internal sealed record PackageFeatureSettingValidationManifest(IReadOnlyList<JsonElement>? Enum);

internal sealed record PackageFeatureSettingUiManifest(
    string? Hint,
    bool Advanced,
    bool Experimental,
    string? OptionsProvider,
    IReadOnlyList<PackageFeatureSettingOptionManifest>? Options);

internal sealed record PackageFeatureSettingOptionManifest(
    string? Label,
    JsonElement? Value,
    string? Description);
