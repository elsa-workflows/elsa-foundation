namespace Elsa3.Mapping.Services;

/// <summary>
/// A name an Elsa 3 script could read from an enclosing scope: a container's variable, or a loop's per-pass variable with
/// the Elsa 4 loop variable that holds its value (<paramref name="LoopVariable"/>).
/// </summary>
internal readonly record struct Elsa3ScopeName(string Name, string ScopeNodeId, string? LoopVariable);

/// <summary>
/// Which Elsa 3 loop owns a by-name read of a per-pass variable (#5873; spec 1243 FR-013, PR #5841, specifies the same rule
/// for ParallelForEach). Elsa 3 looks a name up nearest scope first (the scopes, in Elsa 3's lookup order, come from the
/// converter), so the nearest scope that declares the name owns the read: when that is a loop, the read is the loop's
/// per-pass value, ahead of any workflow variable or variable of a scope further out; when it is a container nested inside
/// the loop's body, that variable takes precedence and the read keeps the handling of any other variable.
/// </summary>
internal static class Elsa3LoopPassVariables
{
    /// <summary>The loop whose per-pass variable a read of a name that <paramref name="matches"/> reaches; null when none does.</summary>
    public static Elsa3ScopeName? Owner(IReadOnlyList<Elsa3ScopeName> scopes, Func<string, bool> matches)
    {
        foreach (var scope in scopes)
        {
            if (matches(scope.Name))
                return scope.LoopVariable is null ? null : scope;
        }

        return null;
    }

    /// <summary>The loop whose per-pass variable a read of <paramref name="name"/> reaches (Elsa 3 matches it ordinally).</summary>
    public static Elsa3ScopeName? Owner(IReadOnlyList<Elsa3ScopeName> scopes, string name) =>
        Owner(scopes, candidate => StringComparer.Ordinal.Equals(candidate, name));

    /// <summary>
    /// The loop whose per-pass variable a generated accessor or setter (<c>get{Name}</c>, <c>set{Name}</c>) that
    /// <paramref name="matches"/> reaches; null when none does. Elsa 3 registered one per distinct variable name in scope,
    /// nearest first, ending with the workflow's own (<paramref name="fartherNames"/>), and a later registration of the same
    /// function replaced an earlier one: names that differ in letter case only share an accessor, and the farthest wins.
    /// </summary>
    public static Elsa3ScopeName? AccessorOwner(IReadOnlyList<Elsa3ScopeName> scopes, IEnumerable<string> fartherNames, Func<string, bool> matches)
    {
        Elsa3ScopeName? owner = null;
        var registered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in scopes)
        {
            if (registered.Add(scope.Name) && matches(scope.Name))
                owner = scope;
        }

        foreach (var name in fartherNames)
        {
            if (registered.Add(name) && matches(name))
                owner = null;
        }

        return owner is { LoopVariable: not null } ? owner : null;
    }

    /// <summary>The per-pass variables a computed name could reach from a node with these enclosing scopes.</summary>
    public static IReadOnlyList<Elsa3ScopeName> Reachable(IReadOnlyList<Elsa3ScopeName> scopes) =>
        scopes.Where(scope => scope.LoopVariable is not null && Owner(scopes, scope.Name) == scope).ToArray();
}
