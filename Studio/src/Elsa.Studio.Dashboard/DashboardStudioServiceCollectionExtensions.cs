using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.Dashboard.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Dashboard;

public static class DashboardStudioServiceCollectionExtensions
{
    public static IServiceCollection AddDashboardStudio(this IServiceCollection services) =>
        services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeDashboardStudioModule>();
}
