namespace Elsa.Studio.Core.Models;

/// <summary>The stable Studio-host identity authority, independent of an interactive backend selection.</summary>
public sealed record StudioBackendAuthority(string? BrowserBaseUrl, string? ServerBaseUrl);

/// <summary>Effective selected-shell composition supplied before the root identity authority is frozen.</summary>
public sealed record StudioBackendAuthorityContext(
    StudioBackendAuthority Configured,
    IReadOnlyCollection<string> EnabledFeatures,
    IReadOnlyCollection<string> DisabledFeatures);
