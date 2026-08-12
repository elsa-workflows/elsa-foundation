using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Permanent deletion is only available to a host composing a publication check (#1283), and the conformance
/// fixtures compose persistence rather than the publishing vertical. This stands the check in so the delete
/// contracts exercise the design-persistence behavior they are about; the refusal itself is pinned per
/// deployment shape, not here.
/// </summary>
public sealed class DesignPersistencePublicationDeletionGuard : IWorkflowDefinitionPublicationDeletionGuard
{
    public Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public static class DesignPersistencePublicationDeletionGuardExtensions
{
    public static IServiceCollection AddDesignPersistencePublicationDeletionGuard(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IWorkflowDefinitionPermanentDeletionGuard,
            DesignPersistencePublicationDeletionGuard>());
        return services;
    }
}
