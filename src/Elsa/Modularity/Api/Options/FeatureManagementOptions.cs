namespace Elsa.Modularity.Api.Options;

public sealed class FeatureManagementOptions
{
    public string ShellsJsonPath { get; set; } = "shells.json";

    /// <summary>
    /// The key the feature catalog revision is computed with. Every instance that reads and applies the catalog against
    /// the same shells.json must share it, and it should be a random value of at least 32 bytes. When unset, a key
    /// generated once per process is used, so a revision read before a restart, or from another instance, is rejected on
    /// apply as a conflict and the client has to reload the catalog.
    /// </summary>
    public string? RevisionKey { get; set; }
}
