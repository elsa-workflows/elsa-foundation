using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Exceptions;

namespace Elsa.Serialization.SystemText.Services;

/// <inheritdoc />
public sealed class WellKnownTypeRegistry : IWellKnownTypeRegistry
{
    /// <summary>
    /// The canonical set of bare (non-dotted) aliases the framework owns. These are the BCL primitive
    /// aliases; module-contributed types must use dotted aliases (see <see cref="ReservedAliasNamespaceException"/>).
    /// Single-sourced from <see cref="TypeAliasConvention.ReservedBareAliases"/> so the reserved set and the
    /// alias convention cannot drift.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedBareAliases = TypeAliasConvention.ReservedBareAliases;

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
        // Reserved-namespace guard (FR-011/FR-012/FR-013): a bare (non-dotted) alias that is not a framework
        // primitive is rejected. Dotted aliases are always allowed (subject to the duplicate check below).
        // Applied only to the incoming alias, never to the derived nullable.
        if (!alias.Contains('.') && !ReservedBareAliases.Contains(alias))
            throw new ReservedAliasNamespaceException(alias);

        // Fail-fast on duplicate registration (FR-010/FR-013/SC-004): a repeat alias, or the same CLR type
        // mapped to a different alias, would silently resolve to the wrong type. Refuse instead of overwriting.
        if (_aliasTypeDictionary.TryGetValue(alias, out var existingType))
        {
            if (existingType == type)
                return; // Idempotent: identical (type, alias) re-registration is a no-op.
            throw new DuplicateTypeAliasException(alias);
        }

        if (_typeAliasDictionary.TryGetValue(type, out var existingAlias) && !string.Equals(existingAlias, alias, StringComparison.OrdinalIgnoreCase))
            throw new DuplicateTypeAliasException(alias, $"The type '{type}' is already registered under the alias '{existingAlias}'; it cannot also be registered as '{alias}'.");

        _typeAliasDictionary[type] = alias;
        _aliasTypeDictionary[alias] = type;

        RegisterDerivedNullable(type, alias);
    }

    /// <summary>
    /// Auto-registers the nullable companion (e.g. <c>int</c> → <c>int?</c> / "Int32?") of a value type. This is an
    /// internal derivation applied AFTER the incoming alias is accepted, so it bypasses both the reserved check
    /// and the duplicate throw: it is collision-safe (only adds when absent) and idempotent.
    /// </summary>
    private void RegisterDerivedNullable(Type type, string alias)
    {
        if (!type.IsPrimitive && !(type.IsValueType && Nullable.GetUnderlyingType(type) == null))
            return;

        var nullableType = typeof(Nullable<>).MakeGenericType(type);
        var nullableAlias = alias + "?";

        // Only add when absent — never throw, never re-key an existing nullable registration.
        if (!_aliasTypeDictionary.ContainsKey(nullableAlias))
            _aliasTypeDictionary[nullableAlias] = nullableType;
        if (!_typeAliasDictionary.ContainsKey(nullableType))
            _typeAliasDictionary[nullableType] = nullableAlias;
    }

    /// <inheritdoc />
    public bool TryGetAlias(Type type, out string alias) => _typeAliasDictionary.TryGetValue(type, out alias!);

    /// <inheritdoc />
    public bool TryGetType(string alias, out Type type) => _aliasTypeDictionary.TryGetValue(alias, out type!);

    /// <inheritdoc />
    public IEnumerable<Type> ListTypes() => _typeAliasDictionary.Keys;

    /// <inheritdoc />
    /// <remarks>
    /// FR-004 / FR-004a: NEVER emits an assembly-qualified name. Returns the registered alias if present,
    /// else the deterministic <see cref="TypeAliasConvention.CanonicalAlias"/> (a bare primitive alias or the
    /// dotted <c>FullName</c>). This is a PURE computation — it does NOT mutate the dictionaries (registration
    /// stays startup-only), so an unregistered type still serializes alias-only without runtime mutation.
    /// </remarks>
    public string GetAliasOrDefault(Type type)
    {
        return TryGetAlias(type, out var alias)
            ? alias
            : TypeAliasConvention.CanonicalAlias(type);
    }

    /// <inheritdoc />
    /// <remarks>FR-004a: registry lookup only — no <c>Type.GetType</c> fallback. Unknown → <c>object</c>.</remarks>
    public Type GetTypeOrDefault(string alias)
    {
        return TryGetType(alias, out var type) ? type : typeof(object);
    }

    /// <inheritdoc />
    /// <remarks>FR-004a: registry lookup only — no <c>Type.GetType</c> fallback. Unknown → false.</remarks>
    public bool TryGetTypeOrDefault(string alias, out Type type)
    {
        return TryGetType(alias, out type);
    }
}
