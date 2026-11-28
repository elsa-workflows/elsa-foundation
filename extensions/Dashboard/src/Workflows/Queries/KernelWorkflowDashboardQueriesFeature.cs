using CShells.Features;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Dashboard.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Dashboard.Queries;

public static class WorkflowDashboardQueryPermissions
{
    public const string Read = "workflows/dashboard-queries:read";
}

/// <summary>Contributes saved workflow questions backed by the retained Actor Foundation runs.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Dashboard")]
[ShellFeature(name: "KernelWorkflowDashboardQueries", DisplayName = "Kernel Workflow Dashboard Queries",
    Description = "Contributes authorized retained Actor Foundation workflow data to native saved questions.",
    DependsOn = ["StudioDashboardApi", "WorkflowsRuntimeKernelRetainedApi"])]
public sealed class KernelWorkflowDashboardQueriesFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionContributor, WorkflowDashboardQueryPermissionExtension>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDashboardDatasetExtension, WorkflowDashboardQuerySource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDashboardQueryValidationExtension, WorkflowDashboardQuerySource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDashboardQueryExecutionExtension, WorkflowDashboardQuerySource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDashboardQueryDrillExtension, WorkflowDashboardQuerySource>());
    }
}

internal sealed class WorkflowDashboardQueryPermissionExtension : IPermissionContributor
{
    public string OwnerId => "Elsa.Workflows.Dashboard.Queries";
    public IEnumerable<Permission> Contribute() =>
    [new(WorkflowDashboardQueryPermissions.Read, "Read workflow question data", "Workflows",
        "Discover and compute workflow question aggregates; each source run and requested organization still requires current read authority.")];
}
