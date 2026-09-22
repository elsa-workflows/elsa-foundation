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
    string? ShellFeatureName = null)
{
    public StudioModuleManifest(
        string id,
        string displayName,
        string version,
        string entry,
        IReadOnlyCollection<string> styles,
        string requiredHostVersion,
        string requiredSdkVersion,
        IReadOnlyCollection<string> capabilities)
        : this(id, displayName, version, entry, styles, requiredHostVersion, requiredSdkVersion, capabilities, null)
    {
    }
}
