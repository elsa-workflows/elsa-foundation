using Elsa3.Activities.Design.Import.Contracts;

namespace Elsa3.Mapping.Services;

/// <summary>
/// The converter's current revision (spec 235 R-8, #3921). Imported Elsa 3 versions are numbered <c>{n}.{R}.0</c> and,
/// above revision 0, identified by their source version and this number, so a host that already imported with an
/// earlier revision gets the re-converted versions beside the earlier ones on its next import instead of a store
/// rebuild; the earlier versions stay as they are, published or not.
/// </summary>
/// <remarks>
/// Bump <see cref="CurrentRevision"/> by one in the same change that makes the converter emit different Elsa 4 content
/// for the same Elsa 3 input (a new rewrite, a changed mapping, a changed default). Do not bump it for a change that
/// leaves every converted output byte-identical. Older Elsa 3 versions of a reusable workflow whose <c>{m}.{R}.0</c> would
/// fall below a newer version already published are refused by the activity version bump rule and skipped; the versions
/// imported at the earlier revision keep running.
/// </remarks>
public sealed class Elsa3ConverterRevision : IElsa3ConverterRevision
{
    /// <summary>The revision of this converter. 0 is the numbering every import before revisions existed used.</summary>
    public const int CurrentRevision = 0;

    /// <inheritdoc />
    public int Current => CurrentRevision;
}
