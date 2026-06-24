using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.Secrets.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Secrets;

public static class SecretsStudioServiceCollectionExtensions
{
    public static IServiceCollection AddSecretsStudio(this IServiceCollection services)
    {
        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeSecretsStudioModule>();
    }
}
