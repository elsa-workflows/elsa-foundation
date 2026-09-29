namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// A context whose tables all belong to one schema family, stamped and checked by
/// <see cref="EfSchemaVersionMaterializationInterceptor"/>, apart from the finalization tables every module context maps
/// (<see cref="EfSchemaFinalization"/>), which belong to their own.
/// </summary>
/// <remarks>
/// EF materializes such a context's domain types directly, running its JSON value converters before any code could
/// upcast the content, so the interceptor reads only the family's current version. The family therefore declares no
/// upcasters, which <c>EfSchemaFamilyChainDeclarationGuardTests</c> enforces: to give it a chain, its content must first move
/// to store code that can upcast before it deserializes.
/// </remarks>
public interface IEfSchemaVersionedContext
{
    /// <summary>The chain of the schema family every row of this context belongs to: its module class's <c>Chain</c>.</summary>
    EfSchemaChain SchemaChain { get; }
}
