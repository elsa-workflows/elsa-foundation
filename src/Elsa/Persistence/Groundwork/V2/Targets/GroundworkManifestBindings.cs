namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// Records which Groundwork target each storage-manifest source belongs to.
/// <para>
/// A manifest source declares one lane's durable schema. Binding it to a target is what makes the
/// target admit that lane's storage units and nothing else, so a design-only node stops carrying
/// runtime tables. A source with no recorded binding belongs to
/// <see cref="GroundworkTargetNames.Default"/>, which keeps a single-database host composing exactly
/// as it did before targets existed.
/// </para>
/// </summary>
public sealed class GroundworkManifestBindings
{
    private readonly Lock gate = new();
    private readonly Dictionary<Type, string> bindings = [];

    /// <summary>Sources a host placed by name, so a later explicit binding cannot silently move them.</summary>
    private readonly HashSet<Type> explicitBindings = [];

    /// <summary>
    /// Binds <paramref name="manifestSourceType"/> to <paramref name="targetName"/>.
    /// <para>
    /// An absent or blank name is an <em>unspecified</em> binding: it settles on
    /// <see cref="GroundworkTargetNames.Default"/> only when nothing has claimed the source yet, and
    /// otherwise leaves an existing binding alone. That is what lets an aggregate preset call a lane
    /// registration without a target while the host still places that lane somewhere specific, in either
    /// composition order. A named target is an assertion: re-binding to the same name is idempotent, and
    /// re-binding to a different one is a composition error.
    /// </para>
    /// </summary>
    /// <exception cref="GroundworkTargetConflictException">
    /// The source was already bound to a different named target. One lane's schema cannot live in two
    /// databases.
    /// </exception>
    public void Bind(Type manifestSourceType, string? targetName)
    {
        ArgumentNullException.ThrowIfNull(manifestSourceType);
        var isExplicit = !string.IsNullOrWhiteSpace(targetName);
        var target = GroundworkTargetNames.Normalize(targetName);
        lock (gate)
        {
            if (!bindings.TryGetValue(manifestSourceType, out var existing))
            {
                bindings.Add(manifestSourceType, target);
                if (isExplicit)
                    explicitBindings.Add(manifestSourceType);
                return;
            }

            if (string.Equals(existing, target, StringComparison.Ordinal))
            {
                if (isExplicit)
                    explicitBindings.Add(manifestSourceType);
                return;
            }

            // An unspecified binding never displaces, and never contradicts, a host's explicit placement.
            if (!isExplicit)
                return;

            // The reverse composition order: the lane settled on the default before the host named its
            // target. Only an unspecified binding can be displaced, so two named targets still conflict.
            if (!explicitBindings.Contains(manifestSourceType))
            {
                bindings[manifestSourceType] = target;
                explicitBindings.Add(manifestSourceType);
                return;
            }

            throw new GroundworkTargetConflictException(
                $"Groundwork manifest source '{manifestSourceType.FullName}' is bound to both target " +
                $"'{existing}' and target '{target}'. A persistence lane's storage units belong to exactly " +
                "one physical store; bind the lane once, to the target that should hold it.");
        }
    }

    /// <summary>The target owning <paramref name="manifestSourceType"/>, defaulting when unbound.</summary>
    public string TargetFor(Type manifestSourceType)
    {
        ArgumentNullException.ThrowIfNull(manifestSourceType);
        lock (gate)
            return bindings.GetValueOrDefault(manifestSourceType, GroundworkTargetNames.Default);
    }

    /// <summary>The distinct targets that own at least one bound manifest source, in ordinal order.</summary>
    public IReadOnlyList<string> BoundTargets
    {
        get
        {
            lock (gate)
                return bindings.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
    }
}
