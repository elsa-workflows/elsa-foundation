namespace Elsa.Activities.Design.Reconciliation.Clr.Options;

/// <summary>
/// Options for the CLR assembly-scanning reconciliation source. The source scans
/// <see cref="FolderPath"/> for assemblies, discovers <c>IActivity</c> implementations by
/// metadata, and contributes one catalog row per activity.
/// </summary>
public sealed class ClrReconciliationOptions
{
    /// <summary>The folder scanned for activity-bearing assemblies.</summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// The source identity recorded on every contributed row. When unset it defaults to the
    /// normalised <see cref="FolderPath"/> (R3), so two distinct folders are distinct sources.
    /// </summary>
    public string? SourceId { get; set; }

    /// <summary>The effective folder path. Empty configuration means the running app base directory.</summary>
    public string ResolvedFolderPath => string.IsNullOrWhiteSpace(FolderPath)
        ? AppContext.BaseDirectory
        : Path.GetFullPath(FolderPath);

    /// <summary>The effective source id: the explicit value, else the normalised folder path.</summary>
    public string ResolvedSourceId => string.IsNullOrWhiteSpace(SourceId)
        ? ResolvedFolderPath
        : SourceId;
}
