namespace Elsa.Admin.Core.Models;

public sealed record AdminModuleManifest(
    string Id,
    string DisplayName,
    string Version,
    string Entry,
    IReadOnlyCollection<string> Styles,
    string RequiredHostVersion,
    string RequiredSdkVersion,
    IReadOnlyCollection<string> Capabilities);
