namespace Elsa.Serialization.SystemText;

/// <summary>
/// The single definition of the deterministic serializer's canonical ordering (spec 086 FR-001): ordinal by
/// serialized name/key. Every site that emits a sorted member/entry sequence — the dictionary converters, the
/// polymorphic converters, and the member-order resolver modifier — routes through here, so the comparer and
/// tie-break live in one place. A divergence between sites would silently break byte-identity, which only a
/// single golden-digest test guards.
/// </summary>
internal static class CanonicalOrdering
{
    /// <summary>Orders <paramref name="source"/> by <paramref name="name"/> using <see cref="StringComparer.Ordinal"/>.</summary>
    public static IOrderedEnumerable<T> InCanonicalOrder<T>(this IEnumerable<T> source, Func<T, string> name)
        => source.OrderBy(name, StringComparer.Ordinal);
}
