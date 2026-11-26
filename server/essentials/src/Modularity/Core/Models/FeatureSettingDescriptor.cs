using System.Text.Json;

namespace Elsa.Modularity.Core.Models;

public sealed record FeatureSettingDescriptor(
    string Name,
    string DisplayName,
    string? Description,
    string? Category,
    string? Group,
    string? ClrType,
    string? JsonType,
    bool Required,
    JsonElement? DefaultValue,
    bool Secret,
    bool Sensitive,
    bool RestartRequired,
    bool Advanced,
    bool Experimental,
    string? UIHint,
    string? OptionsProvider,
    IReadOnlyList<FeatureSettingOptionDescriptor> Options);

public sealed record FeatureSettingOptionDescriptor(
    string Label,
    JsonElement Value,
    string? Description);
