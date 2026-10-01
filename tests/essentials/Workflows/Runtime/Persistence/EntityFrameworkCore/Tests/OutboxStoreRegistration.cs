using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

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
}
