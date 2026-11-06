using CShells.Lifecycle;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Validations.Internal;

internal sealed record ActivityDataSelectionAnalyzerComposition(IServiceCollection Services);

/// <summary>Rejects a missing, duplicate or non-scoped authority after every feature has registered.</summary>
internal sealed class ActivityDataSelectionAnalyzerCompositionCheck(ActivityDataSelectionAnalyzerComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var registrations = composition.Services.Where(descriptor =>
            descriptor.ServiceType == typeof(IActivityDataSelectionAnalyzer) && !descriptor.IsKeyedService).ToArray();
        if (registrations.Length != 1 || registrations[0].Lifetime != ServiceLifetime.Scoped)
            throw new InvalidOperationException("Exactly one scoped IActivityDataSelectionAnalyzer must be composed.");
        return Task.CompletedTask;
    }

    internal static void Register(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ActivityDataSelectionAnalyzerComposition))) return;
        services.AddSingleton(new ActivityDataSelectionAnalyzerComposition(services));
        services.AddShellInitializer<ActivityDataSelectionAnalyzerCompositionCheck>(LifecyclePhase.Prepare, 0);
    }
}
