using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal static class OutboxStoreRegistration
{
    /// <summary>
    /// Wraps the effective <see cref="IRuntimePostCommitOutboxStore"/> registration, keeping its lifetime. The processor
    /// reaches the claim capabilities by casting the store it resolves, so a decorator must implement those it forwards.
    /// </summary>
    public static void Decorate(
        IServiceCollection services,
        Func<IRuntimePostCommitOutboxStore, IRuntimePostCommitOutboxStore> decorate)
    {
        var descriptor = services.Last(candidate => candidate.ServiceType == typeof(IRuntimePostCommitOutboxStore));
        var factory = descriptor.ImplementationFactory
            ?? throw new InvalidOperationException("The outbox store registration is expected to be a factory.");
        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(
            typeof(IRuntimePostCommitOutboxStore),
            provider => decorate((IRuntimePostCommitOutboxStore)factory(provider)),
            descriptor.Lifetime));
    }

    /// <summary>
    /// <see cref="Decorate"/>, and resolves the claim and lookup contracts to the decorated store as well. The drain
    /// orchestrator takes those two from the container rather than casting the store (#2225), so a decorator it must see
    /// is registered here.
    /// </summary>
    public static void DecorateWithClaimsAndLookup(
        IServiceCollection services,
        Func<IRuntimePostCommitOutboxStore, IRuntimePostCommitOutboxStore> decorate)
    {
        Decorate(services, decorate);
        var lifetime = services.Last(candidate => candidate.ServiceType == typeof(IRuntimePostCommitOutboxStore)).Lifetime;
        services.Replace(ServiceDescriptor.Describe(
            typeof(IRuntimePostCommitOutboxClaimStore),
            provider => provider.GetRequiredService<IRuntimePostCommitOutboxStore>(),
            lifetime));
        services.Replace(ServiceDescriptor.Describe(
            typeof(IPostCommitOutboxLookupStore),
            provider => provider.GetRequiredService<IRuntimePostCommitOutboxStore>(),
            lifetime));
    }
}
