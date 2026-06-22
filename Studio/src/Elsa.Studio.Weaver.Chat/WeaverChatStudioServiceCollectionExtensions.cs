using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.Weaver.Chat.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Weaver.Chat;

public static class WeaverChatStudioServiceCollectionExtensions
{
    public static IServiceCollection AddWeaverChatStudio(this IServiceCollection services)
    {
        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeWeaverChatStudioModule>();
    }
}
