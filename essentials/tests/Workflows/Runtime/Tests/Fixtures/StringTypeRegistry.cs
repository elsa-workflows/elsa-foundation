using Elsa.Serialization.Core;

namespace Elsa.Workflows.Runtime.Tests.Fixtures;

/// <summary>A type registry that knows only the <c>String</c> alias, for materializing text-valued inputs.</summary>
internal sealed class StringTypeRegistry : IWellKnownTypeRegistry
{
    public void RegisterType(Type type, string alias) => throw new NotSupportedException();

    public bool TryGetAlias(Type type, out string alias)
    {
        alias = "String";
        return type == typeof(string);
    }

    public bool TryGetType(string alias, out Type type) => TryGetTypeOrDefault(alias, out type);
    public IEnumerable<Type> ListTypes() => [typeof(string)];
    public string GetAliasOrDefault(Type type) => type == typeof(string) ? "String" : type.FullName!;
    public Type GetTypeOrDefault(string alias) => TryGetTypeOrDefault(alias, out var type) ? type : typeof(object);

    public bool TryGetTypeOrDefault(string alias, out Type type)
    {
        type = typeof(string);
        return StringComparer.Ordinal.Equals(alias, "String");
    }
}
