using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Core.Extensions;

/// <summary>
/// Records the single provider that owns workflow test-scope lifecycle, admission, and cleanup.
/// Provider features use this guard before replacing the three storage contracts so composition
/// order cannot silently combine unrelated implementations.
/// </summary>
public sealed record WorkflowTestScopeProviderRegistration(Type ProviderType, bool IsInMemoryDefault);

public static class TestScopeProviderRegistrationExtensions
{
    /// <summary>
    /// Claims the workflow test-scope provider slot. A durable provider may replace the in-memory
    /// default, but two different non-default providers are rejected in either registration order.
    /// Re-registering the same provider is idempotent.
    /// </summary>
    public static IServiceCollection ClaimWorkflowTestScopeProvider(
        this IServiceCollection services,
        Type providerType,
        bool isInMemoryDefault = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(providerType);

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(WorkflowTestScopeProviderRegistration))
            .Select(descriptor => descriptor.ImplementationInstance as WorkflowTestScopeProviderRegistration
                ?? throw new InvalidOperationException(
                    "Workflow test-scope provider registrations must use the guarded instance registration."))
            .ToArray();
        if (registrations.Length > 1)
            throw Conflict(registrations.Select(registration => registration.ProviderType).Append(providerType));

        var existing = registrations.SingleOrDefault();
        if (existing is null)
        {
            services.AddSingleton(new WorkflowTestScopeProviderRegistration(providerType, isInMemoryDefault));
            return services;
        }

        if (existing.ProviderType == providerType)
            return services;

        if (isInMemoryDefault)
            return services;

        if (!existing.IsInMemoryDefault)
            throw Conflict([existing.ProviderType, providerType]);

        services.RemoveAll<WorkflowTestScopeProviderRegistration>();
        services.AddSingleton(new WorkflowTestScopeProviderRegistration(providerType, false));
        return services;
    }

    private static InvalidOperationException Conflict(IEnumerable<Type> providerTypes) => new(
        $"Workflow test-scope storage is a single-provider replacement contract; conflicting providers were registered: {string.Join(", ", providerTypes.Select(type => type.FullName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}.");
}
