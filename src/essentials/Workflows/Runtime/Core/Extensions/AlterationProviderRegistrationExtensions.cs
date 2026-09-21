using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Core.Extensions;

/// <summary>Records the one provider that owns the alteration plan/job contracts.</summary>
public sealed record WorkflowAlterationProviderRegistration(Type ProviderType, bool IsInMemoryDefault);

public static class AlterationProviderRegistrationExtensions
{
    public static IServiceCollection ClaimWorkflowAlterationProvider(this IServiceCollection services, Type providerType, bool isInMemoryDefault = false)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(providerType);
        var registrations = services.Where(x => x.ServiceType == typeof(WorkflowAlterationProviderRegistration)).Select(x => x.ImplementationInstance as WorkflowAlterationProviderRegistration ?? throw new InvalidOperationException("Workflow alteration provider registrations must use the guarded instance registration.")).ToArray();
        if (registrations.Length > 1) throw Conflict(registrations.Select(x => x.ProviderType).Append(providerType));
        var existing = registrations.SingleOrDefault();
        if (existing is null) { services.AddSingleton(new WorkflowAlterationProviderRegistration(providerType, isInMemoryDefault)); return services; }
        if (existing.ProviderType == providerType || isInMemoryDefault) return services;
        if (!existing.IsInMemoryDefault) throw Conflict([existing.ProviderType, providerType]);
        services.RemoveAll<WorkflowAlterationProviderRegistration>(); services.AddSingleton(new WorkflowAlterationProviderRegistration(providerType, false)); return services;
    }
    private static InvalidOperationException Conflict(IEnumerable<Type> providers) => new($"Workflow alteration storage is a single-provider replacement contract; conflicting providers were registered: {string.Join(", ", providers.Select(x => x.FullName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}.");
}
