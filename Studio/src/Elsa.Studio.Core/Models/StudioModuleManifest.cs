namespace Elsa.Studio.Core.Models;

public sealed record StudioModuleManifest(
    string Id,
    string DisplayName,
    string Version,
    string Entry,
    IReadOnlyCollection<string> Styles,
    string RequiredHostVersion,
    string RequiredSdkVersion,
    IReadOnlyCollection<string> Capabilities,
    IReadOnlyCollection<string> RequiredBackendCapabilities);
