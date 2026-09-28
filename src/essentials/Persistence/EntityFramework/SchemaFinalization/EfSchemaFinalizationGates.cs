using System.Collections.Concurrent;

namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// The finalization gates of one container: a shell's, or a plain host's. A module's migrator registers its gate here
/// once it has admitted the module, and <see cref="EfSchemaWriteGateInterceptor"/> finds it here from any context of
/// the module the container builds, so every write checks against the state the gate keeps. It is also where a
/// consumer reads the gate's status (spec 181, FR-022).
/// </summary>
/// <remarks>
/// Registered per container, never by instance on the host container: every shell binds its modules to its own
/// databases, so a gate belongs to the container its module was activated in.
/// </remarks>
public sealed class EfSchemaFinalizationGates
{
    private readonly ConcurrentDictionary<string, EfSchemaModuleGate> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Type, EfSchemaModuleGate> _byContext = new();

    /// <summary>The gates registered so far, one per activated module.</summary>
    public IReadOnlyCollection<EfSchemaModuleGate> All => _gates.Values.ToArray();

    /// <summary>
    /// Registers <paramref name="gate"/> for its module and for <paramref name="contextType"/>, the module's base
    /// context, replacing a gate registered for either before.
    /// </summary>
    public void Register(Type contextType, EfSchemaModuleGate gate)
    {
        ArgumentNullException.ThrowIfNull(contextType);
        ArgumentNullException.ThrowIfNull(gate);
        _gates[gate.Module] = gate;
        _byContext[contextType] = gate;
    }

    /// <summary>
    /// The gate registered for <paramref name="contextType"/> or the nearest of its base types, which is how a
    /// provider-derived context finds the gate its module's base context was admitted under.
    /// </summary>
    public EfSchemaModuleGate? FindForContext(Type contextType)
    {
        ArgumentNullException.ThrowIfNull(contextType);
        for (var candidate = contextType; candidate is not null && candidate != typeof(object); candidate = candidate.BaseType)
        {
            if (_byContext.TryGetValue(candidate, out var gate))
                return gate;
        }

        return null;
    }

    /// <summary>The gate of <paramref name="module"/>, or null when the module has not been admitted in this container.</summary>
    public EfSchemaModuleGate? Find(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        return _gates.GetValueOrDefault(module);
    }
}
