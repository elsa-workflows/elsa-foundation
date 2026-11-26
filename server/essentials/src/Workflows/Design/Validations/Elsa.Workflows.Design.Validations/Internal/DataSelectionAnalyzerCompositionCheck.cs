using CShells.Lifecycle;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Validations.Internal;

internal sealed record DataSelectionComposition(IServiceCollection Services);

/// <summary>Rejects a missing, duplicate or non-scoped authority after every feature has registered.</summary>
internal sealed class DataSelectionAnalyzerCompositionCheck(DataSelectionComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var registrations = composition.Services.Where(descriptor =>
            descriptor.ServiceType == typeof(IActivityDataSelectionAnalyzer) && !descriptor.IsKeyedService).ToArray();
        if (registrations.Length != 1 || registrations[0].Lifetime != ServiceLifetime.Scoped)
            throw new InvalidOperationException("Exactly one scoped IActivityDataSelectionAnalyzer must be composed.");
        var previewers = composition.Services.Where(descriptor =>
            descriptor.ServiceType == typeof(IActivityBindingConversionPreviewer) && !descriptor.IsKeyedService).ToArray();
        if (previewers.Length > 1 || previewers.Any(descriptor => descriptor.Lifetime != ServiceLifetime.Scoped))
            throw new InvalidOperationException("At most one scoped IActivityBindingConversionPreviewer can be composed. Use services.Replace(...) to replace the Publishing implementation.");
        return Task.CompletedTask;
    }

    internal static void Register(IServiceCollection services)
    {
        var existing = services.Where(descriptor => descriptor.ServiceType == typeof(DataSelectionComposition)).ToArray();
        if (existing.Length == 1 && existing[0].ImplementationInstance is DataSelectionComposition current &&
            ReferenceEquals(current.Services, services)) return;
        // A copied shell retains initializer descriptors, but must inspect its own final registrations.
        foreach (var descriptor in existing) services.Remove(descriptor);
        services.AddSingleton(new DataSelectionComposition(services));
        if (existing.Length == 0)
            services.AddShellInitializer<DataSelectionAnalyzerCompositionCheck>(LifecyclePhase.Prepare, 0);
    }
}
