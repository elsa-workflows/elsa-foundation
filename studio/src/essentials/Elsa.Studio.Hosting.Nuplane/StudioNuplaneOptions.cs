namespace Elsa.Studio.Hosting.Nuplane;

/// <summary>Application-owned configuration and upload-feed selection for optional Studio administration.</summary>
public sealed class StudioNuplaneOptions
{
    /// <summary>Optional management JSON, resolved against ContentRoot. Defaults to nuplane-management.json.</summary>
    public string ManagementFile { get; set; } = "nuplane-management.json";

    /// <summary>Directory feed used for uploads. Unset selects the sole directory feed or the registered packages fallback.</summary>
    public string? DirectoryFeedName { get; set; }
}
