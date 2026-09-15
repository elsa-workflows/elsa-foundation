using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Marks the synchronous all-participant EF transition while it is being assembled.</summary>
/// <remarks>
/// Individual durable providers remain fail-closed when a Groundwork checkpoint writer is selected. The aggregate
/// EF composition opens this narrow scope so its participant registrars can stage the complete replacement before
/// the service collection is returned to the caller.
/// </remarks>
internal static class RuntimeCheckpointCompositionTransition
{
    public static bool IsActive(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ImplementationInstance is IRuntimeCheckpointCompositionTransition);

    public static void EnsureGroundworkCheckpointTransitionAllowed(IServiceCollection services, string participant)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(participant);
        if (!IsActive(services) && RuntimeCheckpointCommitStoreBackend.Find(services)?.Name == RuntimeCheckpointCommitStoreBackend.Groundwork)
            throw new InvalidOperationException($"Runtime {participant} EF persistence requires the aggregate EF Runtime transition while the Groundwork checkpoint writer is selected.");
    }

}

/// <summary>Private provider-composition capability used only while an aggregate transition is executing.</summary>
internal interface IRuntimeCheckpointCompositionTransition
{
}
