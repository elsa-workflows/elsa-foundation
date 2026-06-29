namespace Elsa.Serialization.Core;

/// <summary>
/// A central repository of well known types.
/// </summary>
public interface IWellKnownTypeRegistry
{
    /// <summary>
    /// Registers a type under an alias. Registration is fail-fast.
    /// </summary>
    /// <remarks>
    /// The alias is a frozen contract: rename the underlying CLR type freely, but never rename the alias —
    /// persisted definitions resolve by alias, so changing it breaks every stored reference.
    /// </remarks>
    /// <param name="type">The CLR type to register.</param>
    /// <param name="alias">
    /// The stable alias. Bare (non-dotted) aliases are reserved for framework primitives; module-contributed
    /// types must use a dotted alias.
    /// </param>
    /// <exception cref="Exceptions.DuplicateTypeAliasException">
    /// Thrown when the alias is already registered, or when <paramref name="type"/> is already registered under a
    /// different alias. (Re-registering the identical (type, alias) pair is an idempotent no-op.)
    /// </exception>
    /// <exception cref="Exceptions.ReservedAliasNamespaceException">
    /// Thrown when <paramref name="alias"/> is a bare (non-dotted) alias that is not in the framework-reserved
    /// primitive set.
    /// </exception>
    void RegisterType(Type type, string alias);

    /// <summary>
    /// Attempts to get an alias for the specified type.
    /// </summary>
    bool TryGetAlias(Type type, out string alias);

    /// <summary>
    /// Attempts to get the type associated with the specified alias.
    /// </summary>
    bool TryGetType(string alias, out Type type);

    /// <summary>
    /// Returns all registered types.
    /// </summary>
    IEnumerable<Type> ListTypes();

    /// <summary>
    /// Returns the alias for the specified type. If no alias was found, the assembly qualified type name is returned instead.
    /// </summary>
    string GetAliasOrDefault(Type type);

    /// <summary>
    /// Returns the type associated with the specified alias. If no type was found, the alias is interpreted as a type name/
    /// </summary>
    Type GetTypeOrDefault(string alias);

    /// <summary>
    /// Attempt to return a type with the specified alias.
    /// </summary>
    bool TryGetTypeOrDefault(string alias, out Type type);
}