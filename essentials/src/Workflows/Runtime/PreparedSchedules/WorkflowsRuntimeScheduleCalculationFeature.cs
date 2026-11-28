using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>Calculates the next time for an authored interval or Cron schedule.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Triggers")]
[ShellFeature(
    name: "WorkflowsRuntimeScheduleCalculation",
    DisplayName = "Workflow Schedule Calculation",
    Description = "Calculates authored interval and Cron occurrences for scheduling Test Runs.")]
public sealed class WorkflowsRuntimeScheduleCalculationFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRecurringScheduleCalculator, RecurringScheduleCalculator>();
    }
}
