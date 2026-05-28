namespace Elsa.Activities.Design.Reconciliation.Json.Options;

/// <summary>
/// Options for the JSON-file reconciliation source. The reader reads
/// <see cref="FilePath"/>; every contributed activity row is tagged with
/// <see cref="SourceId"/> as the source-instance identifier (so multiple JSON sources
/// configured in the same host stay distinguishable in audit logs and reconciliation-state
/// queries).
/// </summary>
public sealed class JsonReconciliationOptions
{
    /// <summary>
    /// Filesystem path to the JSON catalog file.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Source-instance identifier written as <c>SourceId</c> on every <c>ActivityDefinition</c>
    /// this source contributes. Defaults to <c>"JsonFile"</c>. Override when multiple JSON
    /// sources coexist (e.g. <c>"PrimaryCatalog"</c>, <c>"OverridesCatalog"</c>) so audit
    /// queries can localise which file produced a given row.
    /// </summary>
    public string SourceId { get; set; } = "JsonFile";
}
