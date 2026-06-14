using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.Samples.Dashboard.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Samples.Dashboard;

public static class DashboardStudioSampleServiceCollectionExtensions
{
    public static IServiceCollection AddDashboardStudioSample(this IServiceCollection services)
    {
        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeDashboardStudioModule>();
    }
}

