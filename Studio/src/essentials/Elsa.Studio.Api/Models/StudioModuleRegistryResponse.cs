using System.Text.Json.Serialization;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Api.Models;

public sealed record StudioModuleRegistryResponse(
    string HostVersion,
    string SdkVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<StudioModuleRegistryItem> Modules)
{
    /// <summary>
    /// Whether this response enumerates every module discovered in its selected shell. Null means
    /// unknown, including responses from existing custom providers; an omitted row cannot then prove
    /// absence. This observation establishes neither loader success nor Weaver support or authority.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Complete { get; init; }
}

public sealed record StudioModuleRegistryItem(
    string Id,
    string DisplayName,
    string SourceKind,
    string Scope,
    string Version,
    string RequiredHostVersion,
    string RequiredSdkVersion,
    string Compatibility,
    string Status,
    StudioModuleRegistryManifest Manifest,
    IReadOnlyCollection<StudioModuleContributionSummary> Contributions,
    IReadOnlyCollection<StudioModuleDiagnostic> Diagnostics);

public sealed record StudioModuleRegistryManifest(
    string Entry,
    IReadOnlyCollection<string> Styles,
    IReadOnlyCollection<string> Capabilities);

public sealed record StudioModuleContributionSummary(
    string Type,
    string Id,
    string Label,
    string Status);
