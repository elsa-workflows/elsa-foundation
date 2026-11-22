namespace Elsa3.Activities.Design.Import.Contracts;

/// <summary>
/// The revision of the Elsa 3 converter whose output the import stores (spec 235 R-8, #3921). An imported Elsa 3
/// version n is numbered <c>{n}.{R}.0</c> and, above revision 0, identified by its source version and the revision, so
/// a converter upgrade re-imports the same Elsa 3 input as a newer Elsa 4 version beside the earlier one instead of
/// colliding with it; nothing an earlier revision wrote is changed or unpublished. The Elsa3Mapping feature
/// implements it with the converter's current revision; without an implementation the import runs at revision 0, the
/// numbering and identities of every import before revisions existed.
/// </summary>
public interface IElsa3ConverterRevision
{
    /// <summary>The current converter revision, 0 or higher.</summary>
    int Current { get; }
}
