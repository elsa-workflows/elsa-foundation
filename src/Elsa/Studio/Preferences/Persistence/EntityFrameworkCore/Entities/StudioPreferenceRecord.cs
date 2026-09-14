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
    public int SchemaVersion { get; set; }
    public string ValueJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; }
}
