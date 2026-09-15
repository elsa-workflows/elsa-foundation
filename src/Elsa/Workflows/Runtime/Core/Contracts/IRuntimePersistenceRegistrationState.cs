namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Exposes an atomic snapshot for provider-owned registration state.</summary>
/// <remarks>
/// Provider bridges may keep state outside <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>
/// (for example, a storage-unit registry). The EF runtime composition uses this small neutral seam to roll that
/// state back together with service descriptors when a multi-participant transition fails.
/// </remarks>
public interface IRuntimePersistenceRegistrationState
{
    IRuntimePersistenceRegistrationSnapshot CaptureSnapshot();
}

public interface IRuntimePersistenceRegistrationSnapshot
{
    void Commit();
    void Rollback();
}
