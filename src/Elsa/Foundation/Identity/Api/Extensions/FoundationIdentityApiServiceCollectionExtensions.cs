using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.Api.Extensions;

public static class FoundationIdentityApiServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationIdentityApi(this IServiceCollection services)
    {
        services.AddFoundationIdentityAbstractions();
        services.TryAddScoped<IAuthSessionService, ClaimsAuthSessionService>();
        services.AddOptions<FoundationIdentityApiOptions>();

        return services;
    }
}
