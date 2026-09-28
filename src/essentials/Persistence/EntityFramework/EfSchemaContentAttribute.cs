namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Declares columns of one of a schema family's tables as the family's <em>content</em> (spec 180, FR-009 and FR-014):
/// documents stored in the format of the version the row is stamped with, which a read upcasts through the family's
/// chain before it deserializes them, and which a write that changes them stamps the row with the version it wrote them
/// in.
/// </summary>
/// <remarks>
/// <para>
/// Assembly-level and constant-argument-only, beside the family's <see cref="EfSchemaFamilyAttribute"/> in the same
/// assembly, so <see cref="EfSchemaFamilyCatalog"/> reads it as metadata onto the family's descriptor
/// (<see cref="EfSchemaFamilyDescriptor.ContentColumns"/>) without composing a shell or a container. One declaration
/// per table: <paramref name="entity"/> is the type the family's context maps to that table, and
/// <paramref name="columns"/> its property names, written with <c>nameof</c> where the property is on the type and as
/// a constant where it is a shadow property.
/// </para>
/// <para>
/// Every document column of a stamped table - a string column whose name ends in <c>Json</c> or is <c>Content</c> or
/// <c>Payload</c>, a payload column (<see cref="EfPayloadColumns"/>), or a domain value a converter stores as a string -
/// is declared either here or with <see cref="EfSchemaIntegrityAttribute"/>, and the build fails when one is declared
/// by neither, or when a declared column is not in the model. The guards read this declaration rather than inferring
/// content from the call sites that happen to upcast it: every read of a declared content column goes through the
/// family's chain, and every write that changes one restamps the row.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class EfSchemaContentAttribute(string family, Type entity, params string[] columns) : Attribute
{
    /// <summary>The family whose rows hold the content: a <see cref="EfSchemaFamilyAttribute"/> of the same assembly.</summary>
    public string Family { get; } = family;

    /// <summary>The type the family's context maps to the table.</summary>
    public Type Entity { get; } = entity;

    /// <summary>The table's content columns, by property name.</summary>
    public IReadOnlyList<string> Columns { get; } = columns;
}
