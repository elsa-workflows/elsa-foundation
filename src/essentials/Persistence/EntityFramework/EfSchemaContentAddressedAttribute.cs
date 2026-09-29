namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Marks an entity type whose rows are content-addressed (spec 186, FR-010a and FR-010b): its key is derived from a hash
/// of content the row stores, as a workflow executable's and an executable activity template's are (ADR 0038), so
/// rewriting a row could change or forge its identity. <paramref name="reason"/> says how the key follows from the
/// content, since that is what a later version must not quietly undo.
/// </summary>
/// <remarks>
/// <para>
/// It states what the entity is; the family that owns it names it in
/// <see cref="EfSchemaFamilyAttribute.ContentAddressed"/>, which is what the post-finalization backfill and the
/// completeness rules read. <c>EfSchemaContentAddressedDeclarationTests</c> fails the build when the two disagree for any
/// first-party stamped table, in either direction, so a new content-addressed table is declared the moment it is
/// marked, and marked the moment it is declared.
/// </para>
/// <para>
/// The backfill also never rewrites a row of a marked entity whose family forgot to name it: a family is then never
/// recorded complete over such rows below the target, which is the loud direction.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EfSchemaContentAddressedAttribute(string reason) : Attribute
{
    /// <summary>How the entity's key follows from its content.</summary>
    public string Reason { get; } = reason;
}
