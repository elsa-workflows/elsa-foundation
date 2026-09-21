using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the opt-in EF Core workflow activation-slot authority.</summary>
public static class RuntimeWorkflowActivationAuthorityEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeWorkflowActivationAuthorityEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var serviceSnapshot = services.ToArray();
        var registrationSnapshots = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IRuntimePersistenceRegistrationState>()
            .Select(state => state.CaptureSnapshot())
            .ToArray();
        try
        {
            RuntimeEfContractBackendRegistration.EnsureSharedContext(services, "Runtime workflow activation-authority EF persistence");
            var existing = WorkflowActivationAuthorityBackend.Find(services);
            if (existing?.Name == WorkflowActivationAuthorityBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContract(services);
                return services;
            }

            if (existing is not null)
            {
                existing.EnsureOwnsRegisteredContract(services);
                throw new InvalidOperationException("Runtime activation-authority EF persistence refuses to replace a selected non-EF backend.");
            }

            var contracts = services.Where(x => x.ServiceType == typeof(IWorkflowActivationAuthority)).ToArray();
            if (existing is null && contracts.Any(x => !IsRuntimeDefault(x)))
                throw new InvalidOperationException("An explicit workflow activation-authority registration is already present; EF persistence refuses to replace it implicitly.");
            if (contracts.Length > 1)
                throw new InvalidOperationException("Runtime activation-authority EF persistence requires one selected authority contract.");

            foreach (var descriptor in contracts)
                services.Remove(descriptor);

            services.AddScoped<EfWorkflowActivationAuthority>();
            var concrete = services.Last();
            var contract = ServiceDescriptor.Scoped<IWorkflowActivationAuthority>(provider =>
                provider.GetRequiredService<EfWorkflowActivationAuthority>());
            services.Add(contract);
            WorkflowActivationAuthorityBackend.Register(services, new(
                WorkflowActivationAuthorityBackend.EntityFramework,
                contract,
                concrete));
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in serviceSnapshot)
                services.Add(descriptor);
            foreach (var snapshot in registrationSnapshots)
                snapshot.Rollback();
            throw;
        }
    }

    private static bool IsRuntimeDefault(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType == typeof(InMemoryWorkflowActivationAuthority);
}
