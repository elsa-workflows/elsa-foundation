using System.Collections.Concurrent;

namespace Elsa.Persistence.Schema.SchemaFinalization;

/// <summary>
/// The finalization gates of one container: a shell's, or a plain host's. A module's migrator registers its gate here
/// once it has admitted the module, and the write check (<c>EfSchemaWriteGateInterceptor</c>) finds it here from any
/// context of the module the container builds, so every write checks against the state the gate keeps. It is also where
/// a consumer reads the gate's status (spec 181, FR-022).
/// </summary>
/// <remarks>
/// <para>
/// Registered per container, never by instance on the host container: every shell binds its modules to its own
/// databases, so a gate belongs to the container its module was activated in.
/// </para>
/// <para>
/// It lives in <c>Elsa.Persistence.Schema</c>, which every host shares, so a container has one registry whichever copy
/// of <c>Elsa.Persistence.EntityFramework</c> each of its modules was loaded with, and the dormancy check's source reads
/// every module's gate through it (#2143). A gate is registered under its module's own context type, which only that
/// module's copy of the persistence assembly can name, so the gate found for a context is always one its own write
/// check can use.
/// </para>
/// </remarks>
public sealed class EfSchemaFinalizationGates
{
    private readonly ConcurrentDictionary<string, IEfSchemaModuleGate> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Type, IEfSchemaModuleGate> _byContext = new();

    /// <summary>The gates registered so far, one per activated module.</summary>
    public IReadOnlyCollection<IEfSchemaModuleGate> All => _gates.Values.ToArray();

    /// <summary>
    /// Registers <paramref name="gate"/> for its module and for <paramref name="contextType"/>, the module's base
    /// context, replacing a gate registered for either before.
    /// </summary>
    public void Register(Type contextType, IEfSchemaModuleGate gate)
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
    public IEfSchemaModuleGate? FindForContext(Type contextType)
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
    public IEfSchemaModuleGate? Find(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        return _gates.GetValueOrDefault(module);
    }
}
