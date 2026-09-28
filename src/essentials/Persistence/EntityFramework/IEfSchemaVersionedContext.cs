namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// A context whose tables all belong to one schema family, stamped and checked by
/// <see cref="EfSchemaVersionMaterializationInterceptor"/>, apart from the finalization tables every module context maps
/// (<see cref="EfSchemaFinalization"/>), which belong to their own.
/// </summary>
public interface IEfSchemaVersionedContext
{
    /// <summary>The schema family every row of this context belongs to, as <see cref="EfSchemaVersionSkewException"/> names it.</summary>
    string SchemaFamily { get; }

    /// <summary>The version this build stamps on every row it writes and reads without skew.</summary>
    string SchemaVersion { get; }
}
