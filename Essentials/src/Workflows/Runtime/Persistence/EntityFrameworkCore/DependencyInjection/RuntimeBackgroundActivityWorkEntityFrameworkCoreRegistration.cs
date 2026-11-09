using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the shared-context EF worker ledger for durable background activity execution.</summary>
public static class RuntimeBackgroundActivityWorkEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeBackgroundActivityWorkEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var snapshot = services.ToArray();
        try
        {
            RuntimeEfContractBackendRegistration.EnsureSharedContext(services, "Runtime background activity work EF persistence");

            var existing = RuntimeBackgroundActivityWorkEfRegistration.Find(services);
            if (existing is not null)
            {
                existing.EnsureOwnsRegisteredContract(services);
                return services;
            }

            var contracts = services.Where(descriptor => descriptor.ServiceType == typeof(IBackgroundActivityWorkStore)).ToArray();
            if (contracts.Length > 1 || contracts.Any(descriptor => descriptor.ImplementationType != typeof(InMemoryBackgroundActivityWorkStore)))
                throw new InvalidOperationException("Runtime background activity work EF persistence refuses to replace an explicit or multiply registered store.");

            foreach (var descriptor in contracts)
                services.Remove(descriptor);

            services.AddScoped<EfBackgroundActivityWorkStore>();
            var concrete = services.Last();
            var contract = ServiceDescriptor.Scoped<IBackgroundActivityWorkStore>(provider =>
                provider.GetRequiredService<EfBackgroundActivityWorkStore>());
            services.Add(contract);
            RuntimeBackgroundActivityWorkEfRegistration.Register(services, new(contract, concrete));
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }
}

internal sealed class RuntimeBackgroundActivityWorkEfRegistration(
    ServiceDescriptor contract,
    ServiceDescriptor concrete)
{
    public static RuntimeBackgroundActivityWorkEfRegistration? Find(IServiceCollection services) => services
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<RuntimeBackgroundActivityWorkEfRegistration>()
        .SingleOrDefault();

    public void EnsureOwnsRegisteredContract(IServiceCollection services)
    {
        var contracts = services.Where(descriptor => descriptor.ServiceType == typeof(IBackgroundActivityWorkStore)).ToArray();
        if (contracts.Length != 1 || !ReferenceEquals(contracts[0], contract) || !services.Contains(concrete))
            throw new InvalidOperationException("The EF background activity work registration no longer exclusively owns its store contract.");
    }

    public static void Register(IServiceCollection services, RuntimeBackgroundActivityWorkEfRegistration registration) =>
        services.AddSingleton(registration);
}
