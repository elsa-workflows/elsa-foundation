namespace Elsa.Expressions.JavaScript.Core.Contracts
{

    /// <summary>
    /// A central registry of type aliases.
    /// </summary>
    public interface IJavaScriptTypeAliasRegistry
    {
        /// <summary>
        /// Attempts to get an alias for the specified type.
        /// </summary>
        bool TryGetAlias(Type type, out string alias);

        public string GetAliasOrDefault(Type type) => TryGetAlias(type, out var result)
            ? result
            : "any";
    }
}
