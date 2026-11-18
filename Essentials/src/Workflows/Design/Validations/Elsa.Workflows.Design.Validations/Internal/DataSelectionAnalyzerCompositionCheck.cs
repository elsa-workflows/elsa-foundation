using CShells.Lifecycle;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Validations.Internal;

internal sealed record DataSelectionComposition(IServiceCollection Services);

/// <summary>
/// Rejects a missing, duplicate or non-scoped data selection authority, and a missing, duplicate or non-singleton
/// write-order analyzer, after every feature has registered.
/// </summary>
internal sealed class DataSelectionAnalyzerCompositionCheck(DataSelectionComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var registrations = composition.Services.Where(descriptor =>
            descriptor.ServiceType == typeof(IActivityDataSelectionAnalyzer) && !descriptor.IsKeyedService).ToArray();
        if (registrations.Length != 1 || registrations[0].Lifetime != ServiceLifetime.Scoped)
            throw new InvalidOperationException("Exactly one scoped IActivityDataSelectionAnalyzer must be composed.");
        // The write-order analyzer (spec 266) is a replacement contract consumed by the singleton graph provider:
        // exactly one singleton, so a second registration never wins silently.
        var writeOrder = composition.Services.Where(descriptor =>
            descriptor.ServiceType == typeof(IUnorderedVariableWriteAnalyzer) && !descriptor.IsKeyedService).ToArray();
        if (writeOrder.Length != 1 || writeOrder[0].Lifetime != ServiceLifetime.Singleton)
            throw new InvalidOperationException("Exactly one singleton IUnorderedVariableWriteAnalyzer must be composed.");
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
