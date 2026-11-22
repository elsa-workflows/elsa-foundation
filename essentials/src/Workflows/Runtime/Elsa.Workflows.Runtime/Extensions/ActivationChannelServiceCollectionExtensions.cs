using Elsa.Workflows.Runtime.Core.Options;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Extensions;

internal static class ActivationChannelServiceCollectionExtensions
{
    internal static IServiceCollection AddActivationChannelOptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ActivationChannelOptions>, ActivationChannelOptionsValidator>());
        services.AddOptions<ActivationChannelOptions>().ValidateOnStart();
        return services;
    }
}
