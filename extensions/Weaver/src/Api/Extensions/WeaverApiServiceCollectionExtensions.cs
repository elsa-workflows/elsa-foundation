using Elsa.Weaver.Api.Authorization;
using Elsa.Weaver.Core.Contracts;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Api.Extensions;

public static class WeaverApiServiceCollectionExtensions
{
    public static IServiceCollection AddWeaverApi(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer, HttpWeaverToolAuthorizer>());
        services.AddPermissionContributor<WeaverPermissionContributor>();
        return services;
    }
}
