using Elsa.Studio.Attention.Handlers;
using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Microsoft.Extensions.DependencyInjection;
namespace Elsa.Studio.Attention;
public static class AttentionStudioServiceCollectionExtensions { public static IServiceCollection AddAttentionStudio(this IServiceCollection services) => services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeAttentionStudioModule>(); }
