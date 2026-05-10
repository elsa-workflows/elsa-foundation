using Elsa.Common.Extensions;
using Elsa.Serialization.Core;

namespace Elsa.Serialization.Services
{
    /// <inheritdoc />
    public sealed class WellKnownTypeRegistry : IWellKnownTypeRegistry
    {
        private readonly Dictionary<string, Type> _aliasTypeDictionary = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Type, string> _typeAliasDictionary = [];

        public WellKnownTypeRegistry(IDictionary<string, Type> aliasTypeDictionary)
        {
            foreach (var entry in aliasTypeDictionary)
                RegisterType(entry.Value, entry.Key);
        }

        public WellKnownTypeRegistry()
        {
        }

        /// <inheritdoc />
        public void RegisterType(Type type, string alias)
        {
            _typeAliasDictionary[type] = alias;
            _aliasTypeDictionary[alias] = type;

            if (type.IsPrimitive || type.IsValueType && Nullable.GetUnderlyingType(type) == null)
            {
                var nullableType = typeof(Nullable<>).MakeGenericType(type);
                var nullableAlias = alias + "?";
                _typeAliasDictionary[nullableType] = nullableAlias;
                _aliasTypeDictionary[nullableAlias] = nullableType;
            }
        }

        /// <inheritdoc />
        public bool TryGetAlias(Type type, out string alias) => _typeAliasDictionary.TryGetValue(type, out alias!);

        /// <inheritdoc />
        public bool TryGetType(string alias, out Type type) => _aliasTypeDictionary.TryGetValue(alias, out type!);

        /// <inheritdoc />
        public IEnumerable<Type> ListTypes() => _typeAliasDictionary.Keys;

        public string GetAliasOrDefault(Type type)
        {
            return TryGetAlias(type, out var alias)
                ? alias
                : type.GetSimpleAssemblyQualifiedName();
        }

        /// <inheritdoc />
        public Type GetTypeOrDefault(string alias)
        {
            return TryGetType(alias, out var type) ? type : Type.GetType(alias) ?? typeof(object);
        }

        /// <inheritdoc />
        public bool TryGetTypeOrDefault(string alias, out Type type)
        {
            if (TryGetType(alias, out type))
                return true;

            var t = Type.GetType(alias);

            if (t == null)
                return false;

            type = t;
            return true;
        }
    }
}
