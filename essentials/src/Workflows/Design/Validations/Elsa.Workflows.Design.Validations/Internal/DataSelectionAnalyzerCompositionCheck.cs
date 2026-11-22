using Elsa.Workflows.Design.Core.Contracts;
using CShells.Lifecycle;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Validations.Internal;

internal sealed record DataSelectionComposition(IServiceCollection Services);

/// <summary>
/// Rejects a missing, duplicate or non-scoped data selection authority, a missing, duplicate or non-singleton
/// write-order analyzer, a missing, duplicate or non-scoped script variable rename planner, and two script variable read
/// analyzers for one language (spec 1222), after every feature has registered.
/// </summary>
internal sealed class DataSelectionAnalyzerCompositionCheck(DataSelectionComposition composition, IServiceProvider services) : IShellInitializer
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
        // The rename planner (spec 1222) is a replacement contract consumed by the Design API: exactly one scoped.
        var renamePlanners = composition.Services.Where(descriptor =>
            descriptor.ServiceType == typeof(IScriptVariableRenamePlanner) && !descriptor.IsKeyedService).ToArray();
        if (renamePlanners.Length != 1 || renamePlanners[0].Lifetime != ServiceLifetime.Scoped)
            throw new InvalidOperationException("Exactly one scoped IScriptVariableRenamePlanner must be composed.");
        // The read resolver refuses two analyzers of different types for one language; resolving it here makes that a
        // start-up failure rather than a failure of the first publish.
        using (var scope = services.CreateScope())
            _ = scope.ServiceProvider.GetService<Services.ScriptVariableReadResolver>();
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
