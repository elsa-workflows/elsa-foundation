namespace Elsa.DesignPersistence.Benchmarks.Forms;

/// <summary>
/// The three Groundwork physical storage forms compared for selection evidence (Spec 093 data-model
/// Decision 3, quickstart §6). Canonical JSON is authoritative in every form; the forms differ only
/// in whether query-bearing fields are materialized as native indexed columns.
/// </summary>
public enum PhysicalForm
{
    /// <summary>Shared documents table across all kinds; query fields stay inside JSON (retains scans).</summary>
    SharedLinked,

    /// <summary>One document table per kind; query fields stay inside JSON (no projected columns).</summary>
    DedicatedDocument,

    /// <summary>Physical entity tables with native projected columns and indexes.</summary>
    PhysicalEntity
}

public static class PhysicalForms
{
    public static PhysicalForm Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        "shared" or "shared-linked" or "sharedlinked" => PhysicalForm.SharedLinked,
        "dedicated" or "dedicated-document" or "dedicateddocument" => PhysicalForm.DedicatedDocument,
        "entity" or "physical-entity" or "physicalentity" => PhysicalForm.PhysicalEntity,
        _ => throw new ArgumentException($"Unknown physical form '{value}'. Use shared, dedicated, or entity.")
    };

    public static string Key(this PhysicalForm form) => form switch
    {
        PhysicalForm.SharedLinked => "shared",
        PhysicalForm.DedicatedDocument => "dedicated",
        PhysicalForm.PhysicalEntity => "entity",
        _ => throw new ArgumentOutOfRangeException(nameof(form))
    };
}
