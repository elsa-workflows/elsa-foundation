using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class OpenIddictGroundworkRegistrationTests
{
    private static readonly Type[] ManagerContracts =
    [
        typeof(IOpenIddictApplicationManager),
        typeof(IOpenIddictAuthorizationManager),
        typeof(IOpenIddictScopeManager),
        typeof(IOpenIddictTokenManager)
    ];

    private static readonly Type[] StoreContractDefinitions =
    [
        typeof(IOpenIddictApplicationStore<>),
        typeof(IOpenIddictAuthorizationStore<>),
        typeof(IOpenIddictScopeStore<>),
        typeof(IOpenIddictTokenStore<>)
    ];

    [Fact]
    public void Feature_type_is_public_non_sealed_and_discoverable()
    {
        var featureType = OpenIddictGroundworkContractFixture.RequiredType(
            OpenIddictGroundworkContractFixture.FeatureTypeName);

        Assert.True(featureType.IsPublic);
        Assert.False(featureType.IsSealed);
    }

    [Fact]
    public void Registration_replaces_all_four_store_families_with_scoped_groundwork_implementations()
    {
        var services = OpenIddictGroundworkContractFixture.CreateBaselineServices();
        OpenIddictGroundworkContractFixture.ApplyGroundworkRegistration(services);

        foreach (var contractDefinition in StoreContractDefinitions)
        {
            var descriptors = services.Where(descriptor =>
                descriptor.ServiceType.IsGenericType &&
                descriptor.ServiceType.GetGenericTypeDefinition() == contractDefinition).ToArray();
            var descriptor = Assert.Single(descriptors);

            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            Assert.Equal(
                OpenIddictGroundworkContractFixture.AssemblyName,
                descriptor.ImplementationType?.Assembly.GetName().Name);
        }
    }

    [Fact]
    public void All_four_managers_and_stores_build_and_resolve()
    {
        var services = OpenIddictGroundworkContractFixture.CreateBaselineServices();
        OpenIddictGroundworkContractFixture.ApplyGroundworkRegistration(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        Assert.All(ManagerContracts, contract =>
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(contract)));
        Assert.All(
            services.Where(descriptor =>
                descriptor.ServiceType.IsGenericType &&
                StoreContractDefinitions.Contains(descriptor.ServiceType.GetGenericTypeDefinition())),
            descriptor => Assert.NotNull(scope.ServiceProvider.GetRequiredService(descriptor.ServiceType)));
    }

    [Fact]
    public async Task Behavior_registration_preserves_server_validation_selector_and_token_service_without_a_store_provider()
    {
        var services = OpenIddictGroundworkContractFixture.CreateBaselineServices();
        var tokenService = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITokenService));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<OpenIddictServerOptions>));

        using var provider = services.BuildServiceProvider();
        Assert.Equal(typeof(OpenIddictTokenService), tokenService.ImplementationType);
        var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        Assert.Contains(schemes, scheme =>
            scheme.Name == OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        Assert.Contains(schemes, scheme =>
            scheme.Name == OpenIddictIdentityDefaults.SelectorScheme);
    }

    [Fact]
    public async Task Server_validation_selector_and_token_service_registrations_are_preserved()
    {
        var services = OpenIddictGroundworkContractFixture.CreateBaselineServices();
        var preserved = services.Where(IsPreservedBehaviorDescriptor).ToArray();

        OpenIddictGroundworkContractFixture.ApplyGroundworkRegistration(services);

        Assert.All(preserved, descriptor => Assert.Contains(descriptor, services));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<OpenIddictTokenService>(provider.GetRequiredService<ITokenService>());
        var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        Assert.Contains(schemes, scheme =>
            scheme.Name == OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        Assert.Contains(schemes, scheme =>
            scheme.Name == OpenIddictIdentityDefaults.SelectorScheme);
    }

    private static bool IsPreservedBehaviorDescriptor(ServiceDescriptor descriptor)
    {
        var serviceNamespace = descriptor.ServiceType.Namespace;
        return descriptor.ServiceType == typeof(ITokenService) ||
               descriptor.ServiceType == typeof(IAuthenticationProviderModule) ||
               serviceNamespace?.StartsWith("OpenIddict.Server", StringComparison.Ordinal) == true ||
               serviceNamespace?.StartsWith("OpenIddict.Validation", StringComparison.Ordinal) == true;
    }
}
