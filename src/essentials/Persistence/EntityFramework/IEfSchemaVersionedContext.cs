namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// A context whose tables all belong to one schema family, stamped and checked by
/// <see cref="EfSchemaVersionMaterializationInterceptor"/>.
/// </summary>
public interface IEfSchemaVersionedContext
{
    /// <summary>The schema family every row of this context belongs to, as <see cref="EfSchemaVersionSkewException"/> names it.</summary>
    string SchemaFamily { get; }

    /// <summary>The version this build stamps on every row it writes and reads without skew.</summary>
    string SchemaVersion { get; }
}
