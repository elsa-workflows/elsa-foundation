namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// One EF module's finalization gate in one container (spec 181), as a reader that references no EF Core sees it: what
/// the gate last observed of each family it guards, and a way to have it read again. The shared dormancy check's source
/// reads a container's gates through this (spec 182, FR-003), so it finds the gate of a module that Nuplane loaded with
/// a private copy of <c>Elsa.Persistence.EntityFramework</c> as surely as one compiled into the host (#2143).
/// </summary>
/// <remarks>
/// Implemented by <c>EfSchemaModuleGate</c>, which is the only implementation: a gate is registered by the module's own
/// migrator once it has admitted the module, and what it answers is exactly what the gate holds every write to.
/// </remarks>
public interface IEfSchemaModuleGate
{
    /// <summary>The EF module this gate guards.</summary>
    string Module { get; }

    /// <summary>
    /// <paramref name="family"/>'s status as this host last observed it, with no I/O: the record as last read, the
    /// version this host writes and whether it refuses writes, and when it read the record. <see langword="null"/> when
    /// this gate does not guard the family.
    /// </summary>
    EfSchemaFamilyStatus? Observe(string family);

    /// <summary>Every family this gate guards, as <see cref="Observe(string)"/> describes each, in declaration order.</summary>
    IReadOnlyList<EfSchemaFamilyStatus> Observe();

    /// <summary>
    /// Reads the module's records again and adopts what they say, unless the last read was less than
    /// <paramref name="maxAge"/> ago (spec 182, FR-014). Concurrent callers share one read. Returns whether it read.
    /// </summary>
    Task<bool> RefreshIfOlderThanAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);

    /// <summary>Every family's status (spec 181, FR-022), read now, with the counted members that cannot read each pending version.</summary>
    Task<IReadOnlyList<EfSchemaFamilyStatus>> ReadStatusAsync(CancellationToken cancellationToken = default);
}
