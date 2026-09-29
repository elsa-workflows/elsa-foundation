using Elsa.Persistence.Schema;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Declares a document column of one of a schema family's tables as an <em>integrity</em> column rather than content
/// (spec 180, FR-008): a projection or integrity datum that is compared as the bytes it was stored with, never upcast
/// and never deserialized into a current type, so a read that uses it needs no chain and a write that derives it needs
/// no stamp of its own beyond the row's.
/// </summary>
/// <remarks>
/// Declared beside the family's <see cref="EfSchemaFamilyAttribute"/>, as <see cref="EfSchemaContentAttribute"/> is,
/// and read onto <see cref="EfSchemaFamilyDescriptor.IntegrityColumns"/>. <paramref name="reason"/> records why the
/// column is compared as stored bytes, since that is the decision a later version must not quietly reverse: a column
/// deserialized into a current type, or compared with a value this build serializes, is content.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class EfSchemaIntegrityAttribute(string family, Type entity, string column, string reason) : Attribute
{
    /// <summary>The family whose rows hold the column: a <see cref="EfSchemaFamilyAttribute"/> of the same assembly.</summary>
    public string Family { get; } = family;

    /// <summary>The type the family's context maps to the table.</summary>
    public Type Entity { get; } = entity;

    /// <summary>The column, by property name.</summary>
    public string Column { get; } = column;

    /// <summary>Why the column is compared as stored bytes rather than upcast as content.</summary>
    public string Reason { get; } = reason;
}
