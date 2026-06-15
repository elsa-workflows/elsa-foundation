using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers the Groundwork-backed runtime store bridges. A provider feature is responsible for
/// registering the concrete <see cref="Groundwork.Documents.Store.IDocumentStore"/> these bridges
/// consume; this method only swaps the runtime store contracts over to the bridge implementations.
/// </summary>
public static class GroundworkRuntimeStoreRegistration
{
    public static IServiceCollection AddGroundworkRuntimeStores(this IServiceCollection services)
    {
        // Replace the in-memory defaults registered by the runtime API feature. RemoveAll guarantees
        // the bridge wins regardless of feature composition order.
        services.RemoveAll<IBookmarkStateStore>();
        services.AddSingleton<IBookmarkStateStore, GroundworkBookmarkStateStore>();
        services.RemoveAll<IWorkflowExecutableStore>();
        services.AddSingleton<IWorkflowExecutableStore, GroundworkWorkflowExecutableStore>();
        return services;
    }
}
