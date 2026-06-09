namespace Elsa.Serialization.Core;

/// <summary>
/// A central repository of well known types.
/// </summary>
public interface IWellKnownTypeRegistry
{
    /// <summary>
    /// Register a type with an alias.
    /// </summary>
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