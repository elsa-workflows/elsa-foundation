using CShells.Features;
using CShells.Lifecycle;
using Elsa.Events.Core.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Events;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Management.Query;
using Elsa.Studio.Dashboard.Management.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Dashboard.Management;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Studio")]
[ShellFeature(name: "StudioDashboardManagement", DisplayName = "Studio Dashboard Management",
    Description = "Provides validated native dashboard queries and saved definitions.", DependsOn = ["Events"])]
public class DashboardManagementFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DashboardQueryLimits>();
        services.TryAddSingleton<DashboardReadGate>();
        services.TryAddScoped<IDashboardQueryService, DashboardQueryService>();
        services.TryAddEventHandler<DashboardDatasetsCollecting, CollectDashboardDatasets>();
        services.TryAddEventHandler<DashboardQueryValidating, ValidateDashboardQuery>();
        services.TryAddEventHandler<DashboardQueryExecuting, ExecuteDashboardQuery>();
        services.TryAddEventHandler<DashboardQueryDrilling, DrillDashboardQuery>();
        // Replace only our known fail-closed fallback; preserve an explicitly configured host validator.
        foreach (var fallback in services.Where(item => item.ServiceType == typeof(IDashboardDefinitionValidator) &&
                     item.ImplementationType == typeof(UnavailableDashboardDefinitionValidator)).ToArray())
            services.Remove(fallback);
        services.TryAddScoped<IDashboardDefinitionValidator, DashboardDefinitionValidator>();
        services.AddDashboardResources();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShellInitializer>(new DashboardManagementCompositionGuard(services)));
    }
}
