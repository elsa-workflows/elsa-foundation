namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// The set of admitted physical Groundwork stores a host declares, keyed by target name.
/// <para>
/// One instance is placed in the service collection at composition time and resolved as a singleton at
/// runtime, so provider registrations, lane bindings, and the readiness guard all agree on which targets
/// exist. Declaring the same target twice with the same provider and connection is idempotent, which
/// keeps repeated feature composition safe; declaring it twice with a <em>different</em> provider or
/// connection is a composition error rather than a silently discarded configuration.
/// </para>
/// </summary>
public sealed class GroundworkTargetRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, GroundworkTargetDeclaration> declarations = new(StringComparer.Ordinal);

    /// <summary>The declared target names in deterministic ordinal order.</summary>
    public IReadOnlyList<string> TargetNames
    {
        get
        {
            lock (gate)
                return declarations.Keys.Order(StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>Whether <paramref name="name"/> resolves to a declared target.</summary>
    public bool IsDeclared(string? name)
    {
        var target = GroundworkTargetNames.Normalize(name);
        lock (gate)
            return declarations.ContainsKey(target);
    }

    /// <summary>Returns the declaration for <paramref name="name"/>, or <c>null</c> when it is undeclared.</summary>
    public GroundworkTargetDeclaration? Find(string? name)
    {
        var target = GroundworkTargetNames.Normalize(name);
        lock (gate)
            return declarations.GetValueOrDefault(target);
    }

    /// <summary>
    /// Records <paramref name="declaration"/>. Returns <see cref="GroundworkTargetDeclarationResult.Declared"/>
    /// the first time a name is seen and <see cref="GroundworkTargetDeclarationResult.AlreadyDeclared"/> for an
    /// exact repeat, so a provider registration can early-return without re-registering its services.
    /// </summary>
    /// <exception cref="GroundworkTargetConflictException">
    /// The same target name was already declared against a different provider leaf or a different connection.
    /// </exception>
    public GroundworkTargetDeclarationResult Declare(GroundworkTargetDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        lock (gate)
        {
            if (!declarations.TryGetValue(declaration.Name, out var existing))
            {
                declarations.Add(declaration.Name, declaration);
                return GroundworkTargetDeclarationResult.Declared;
            }

            if (existing.AddressesSameStoreAs(declaration))
                return GroundworkTargetDeclarationResult.AlreadyDeclared;

            throw new GroundworkTargetConflictException(
                $"Groundwork target '{declaration.Name}' was declared twice against different stores: " +
                $"first as {existing.Describe()}, then as {declaration.Describe()}. " +
                "Give each physical store its own target name, or make both declarations address the same store.");
        }
    }

    /// <summary>Ensures <paramref name="name"/> is declared, failing with the list of names that are.</summary>
    /// <exception cref="GroundworkTargetConflictException">The target is not declared.</exception>
    public GroundworkTargetDeclaration Require(string? name)
    {
        var target = GroundworkTargetNames.Normalize(name);
        var declaration = Find(target);
        if (declaration is not null)
            return declaration;

        var declared = TargetNames;
        var known = declared.Count == 0
            ? "no targets are declared"
            : $"declared targets are {string.Join(", ", declared.Select(candidate => $"'{candidate}'"))}";
        throw new GroundworkTargetConflictException(
            $"Groundwork target '{target}' is not declared, so nothing can bind to it; {known}. " +
            "Declare the target on a provider feature before binding a persistence lane to it.");
    }
}

/// <summary>The outcome of declaring a Groundwork target.</summary>
public enum GroundworkTargetDeclarationResult
{
    /// <summary>The target name was not previously declared and is now registered.</summary>
    Declared,

    /// <summary>The target was already declared against the same provider and connection.</summary>
    AlreadyDeclared
}
