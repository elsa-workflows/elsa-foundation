namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

public static class EfSchemaFinalizationGatesExtensions
{
    /// <summary>
    /// The gate this copy of <c>Elsa.Persistence.EntityFramework</c> admitted for <paramref name="contextType"/> or the
    /// nearest of its base types, or null before the module was admitted. The registry is shared with every module a
    /// container holds, whichever copy of this assembly each was loaded with (#2143), but a gate is registered under its
    /// own module's context type, which only that module's copy can name.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The gate registered for the context is not one this copy built, so its write check cannot read what the gate
    /// holds. That is refused rather than read as "no gate yet", which would let a write through unchecked.
    /// </exception>
    public static EfSchemaModuleGate? FindModuleGate(this EfSchemaFinalizationGates gates, Type contextType)
    {
        ArgumentNullException.ThrowIfNull(gates);
        return gates.FindForContext(contextType) switch
        {
            null => null,
            EfSchemaModuleGate gate => gate,
            var other => throw new InvalidOperationException(
                $"The finalization gate registered for {contextType.FullName} is a {other.GetType().AssemblyQualifiedName}, " +
                $"not the {typeof(EfSchemaModuleGate).AssemblyQualifiedName} this module's write check reads. A context's gate " +
                "is registered by its own module's migrator, so the two copies of Elsa.Persistence.EntityFramework must not " +
                "share a context type.")
        };
    }
}
