using Elsa.Foundation.Identity.Api.ApiKeys;
using Elsa.Foundation.Identity.Api.Applications;
using Elsa.Foundation.Identity.Api.Endpoints.Applications;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeEndpoints;

namespace Elsa.Foundation.Identity.Api.Extensions;

public static class FoundationIdentityApplicationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers application management and the <see cref="ApiKeyAuthenticationDefaults.AuthenticationScheme"/> scheme,
    /// whose principals are trusted normalized principals. The OpenIddict scheme selector routes
    /// <c>Authorization: ApiKey</c> requests to it; the host supplies the application, credential and role stores.
    /// </summary>
    public static IServiceCollection AddFoundationIdentityApplications(
        this IServiceCollection services,
        Action<ApiKeyAuthenticationOptions>? configure = null)
    {
        services.AddFoundationIdentityAbstractions();
        services.AddNormalizedAuthenticationType(ApiKeyAuthenticationDefaults.AuthenticationScheme);
        services.TryAddScoped<ApplicationManager>();
        services.TryAddKeyedSingleton<IEndpointExceptionTranslator, ApplicationExceptionTranslator>(FoundationIdentityApplicationsApi.OwnerId);

        // AddScheme refuses a second registration of the same scheme when the options are built, so a repeated call
        // only adds the configuration.
        if (services.All(descriptor => descriptor.ServiceType != typeof(ApiKeyAuthenticationHandler)))
            services.AddAuthentication().AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.AuthenticationScheme, "Elsa application API key", configureOptions: null);

        if (configure is not null)
            services.Configure(ApiKeyAuthenticationDefaults.AuthenticationScheme, configure);

        return services;
    }
}
