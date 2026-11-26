namespace Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// Relational projection of one Studio preference document. The explicit scope columns are retained
/// beside the canonical identity hash so a hash collision cannot expose another subject's preference.
/// </summary>
public sealed class StudioPreferenceRecord
{
    public string Id { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string StudioHostId { get; set; } = "";
    public string Namespace { get; set; } = "";
    /// <summary>The preference namespace's own schema version, as the client wrote the value.</summary>
    public int PreferenceSchemaVersion { get; set; }

    /// <summary>The persisted-schema version of this row: <see cref="StudioPreferencesEfModule.SchemaVersion"/>.</summary>
    public string SchemaVersion { get; set; } = null!;
    public string ValueJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; }
}
