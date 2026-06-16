using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.FeatureManagement.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.FeatureManagement;

public static class FeatureManagementStudioServiceCollectionExtensions
{
    public static IServiceCollection AddFeatureManagementStudio(this IServiceCollection services)
    {
        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeFeatureManagementStudioModule>();
    }
}
