using Elsa.Weaver.Api.Authorization;
using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Api.Capabilities.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Api.Extensions;

public static class WeaverApiServiceCollectionExtensions
{
    public static IServiceCollection AddWeaverApi(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IWeaverPrincipalSource, HttpWeaverPrincipalSource>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverContextProvider, HostSupportContextSource>());
        services.AddApiCapabilitySource<WeaverEffectsApiCapabilitySource>();
        services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer, HttpWeaverToolAuthorizer>());
        services.AddPermissionContributor<WeaverPermissionContributor>();
        return services;
    }
}
