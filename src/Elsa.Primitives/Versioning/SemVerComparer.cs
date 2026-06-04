namespace Elsa.Primitives.Versioning;

/// <summary>
/// A reusable <see cref="IComparer{T}"/> over <see cref="SemVer"/> that applies SemVer 2.0.0
/// precedence. Provided as a strategy so callers (sorting, ordering, "latest version" selection)
/// can depend on the comparer rather than re-implementing precedence.
/// </summary>
public sealed class SemVerComparer : IComparer<SemVer>
{
    public static SemVerComparer Instance { get; } = new();

    public int Compare(SemVer x, SemVer y) => x.CompareTo(y);
}
