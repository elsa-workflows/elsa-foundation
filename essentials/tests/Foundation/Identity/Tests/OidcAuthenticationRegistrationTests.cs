using CShells.Lifecycle;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Oidc;
using Elsa.Foundation.Identity.Oidc.Extensions;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

/// <summary>
/// Covers the W4 (MS-13) OIDC authentication registration behaviour: an Elsa server secured by
/// default must reject an unauthenticated call with 401, not fault with a 500. The interactive
/// OpenID Connect handler is a remote-authentication request handler that eagerly validates its
/// options (ClientId is required) for every request, so registering it while unconfigured turns
/// every request into a 500 before authorization runs. It is therefore only registered when a
/// ClientId is configured; the JWT bearer scheme is always registered and made the default
/// challenge scheme so unauthenticated calls are challenged (401).
/// </summary>
public sealed class OidcAuthenticationRegistrationTests
{
    [Fact]
    public async Task Legacy_distinct_named_bearer_registrations_preserve_schemes_without_duplicate_bridge_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationIdentityOidc(options => options.JwtBearerScheme = "legacy-first");
        services.AddFoundationIdentityOidc(options => options.JwtBearerScheme = "legacy-second");
        using var provider = services.BuildServiceProvider();
        var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        Assert.Contains(schemes, scheme => scheme.Name == "legacy-first");
        Assert.Contains(schemes, scheme => scheme.Name == "legacy-second");
        Assert.Equal("legacy-second", provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultAuthenticateScheme);
        Assert.Null(provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("legacy-first").EventsType);
        Assert.Null(provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("legacy-second").EventsType);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(OidcBearerRegistration));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(OidcBearerActivationGuard));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(OidcBearerNormalizationEvents));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Repeated_registration_cannot_cross_the_captured_normalization_mode(bool first, bool second)
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityOidc(options => options.NormalizeBearerClaims = first);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddFoundationIdentityOidc(options => options.NormalizeBearerClaims = second));
        Assert.Equal(OidcBearerOptionsValidator.ConfigurationInvalid, exception.Message);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(OidcBearerRegistration));
    }

    [Fact]
    public async Task Registers_the_jwt_bearer_scheme_even_when_no_client_id_is_configured()
    {
        using var provider = BuildProvider();

        var schemes = await provider.GetService<IAuthenticationSchemeProvider>()!.GetAllSchemesAsync();

        Assert.Contains(schemes, s => s.Name == new OidcAuthenticationOptions().JwtBearerScheme);
    }

    [Fact]
    public async Task Does_not_register_the_interactive_openid_connect_scheme_when_no_client_id_is_configured()
    {
        using var provider = BuildProvider();

        var schemes = await provider.GetService<IAuthenticationSchemeProvider>()!.GetAllSchemesAsync();

        Assert.DoesNotContain(schemes, s => s.Name == new OidcAuthenticationOptions().AuthenticationScheme);
    }

    [Fact]
    public async Task Registers_the_interactive_openid_connect_scheme_when_a_client_id_is_configured()
    {
        using var provider = BuildProvider(options =>
        {
            options.ClientId = "elsa-client";
            options.Authority = "https://localhost/";
            options.RequireHttpsMetadata = false;
        });

        var schemes = await provider.GetService<IAuthenticationSchemeProvider>()!.GetAllSchemesAsync();

        Assert.Contains(schemes, s => s.Name == new OidcAuthenticationOptions().AuthenticationScheme);
    }

    [Fact]
    public void Normalization_is_opt_in_and_audience_is_absent_by_default()
    {
        using var provider = BuildProvider();

        var options = provider.GetRequiredService<IOptions<OidcAuthenticationOptions>>().Value;

        Assert.False(options.NormalizeBearerClaims);
        Assert.Null(options.Audience);
        Assert.Equal("oidc", options.ProviderId);
        Assert.Null(options.TenantId);
    }

    [Fact]
    public async Task A_direct_feature_opt_in_registers_and_resolves_its_guarded_service_graph()
    {
        const string tenant = "registration-tenant-a";
        const string providerId = "registration-provider-a";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceCore(tenant);
        services.RemoveAll<IClaimMappingStore>();
        services.AddSingleton<IClaimMappingStore, RegistrationTestClaimMappingStore>();

        new OidcAuthenticationFeature
        {
            IsDefault = false,
            Authority = "https://issuer.example.test",
            Audience = "worker-api",
            ProviderId = providerId,
            TenantId = tenant,
            RequireHttpsMetadata = false,
            NormalizeBearerClaims = true
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scopedProvider = scope.ServiceProvider;
        var oidc = scopedProvider.GetRequiredService<IOptions<OidcAuthenticationOptions>>().Value;
        var bearer = scopedProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(oidc.JwtBearerScheme);
        var registration = provider.GetRequiredService<OidcBearerRegistration>();
        var validator = provider.GetRequiredService<OidcBearerOptionsValidator>();
        var guard = provider.GetRequiredService<OidcBearerActivationGuard>();
        var events = scopedProvider.GetRequiredService<OidcBearerNormalizationEvents>();
        var mappings = scopedProvider.GetRequiredService<IClaimMappingStore>();
        var normalizer = scopedProvider.GetRequiredService<IClaimsNormalizer>();
        var access = scopedProvider.GetRequiredService<IPersistenceAccessContextAccessor>();
        var identityOptions = scopedProvider.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value;

        Assert.True(oidc.NormalizeBearerClaims);
        Assert.Equal(providerId, oidc.ProviderId);
        Assert.Equal(tenant, oidc.TenantId);
        Assert.Equal(new PersistenceScope(tenant), access.Current.RequireScope());
        Assert.Equal(tenant, registration.TenantId);
        Assert.Equal(typeof(OidcBearerNormalizationEvents), bearer.EventsType);
        Assert.IsType<OidcBearerNormalizationEvents>(events);
        Assert.IsType<RegistrationTestClaimMappingStore>(mappings);
        Assert.IsType<DefaultClaimsNormalizer>(normalizer);
        Assert.IsType<OidcBearerOptionsValidator>(validator);
        Assert.IsType<OidcBearerActivationGuard>(guard);
        Assert.Contains(provider.GetServices<IHostedService>(), service => ReferenceEquals(service, guard));
        Assert.Contains(provider.GetServices<IShellInitializer>(), initializer => ReferenceEquals(initializer, guard));
        Assert.Contains(provider.GetServices<IValidateOptions<OidcAuthenticationOptions>>(), item => ReferenceEquals(item, validator));
        Assert.Contains(provider.GetServices<IValidateOptions<JwtBearerOptions>>(), item => ReferenceEquals(item, validator));
        Assert.Contains(provider.GetServices<IPostConfigureOptions<JwtBearerOptions>>(), item => ReferenceEquals(item, validator));
        Assert.Contains(OidcBearerNormalizationEvents.NormalizedAuthenticationType, identityOptions.NormalizedAuthenticationTypes);
        Assert.DoesNotContain(oidc.JwtBearerScheme, identityOptions.NormalizedAuthenticationTypes);
        Assert.DoesNotContain("AuthenticationTypes.Federation", identityOptions.NormalizedAuthenticationTypes);
        Assert.Equal(OwnershipMode.FoundationOwned, identityOptions.OwnershipMode);
        Assert.Equal(ProviderCapabilities.FoundationReference, identityOptions.ProviderCapabilities);
        Assert.Equal(PermissionPropagationMode.ImmediateServerSide, identityOptions.PermissionPropagation);

        await guard.InitializeAsync(CancellationToken.None);
    }

    [Fact]
    public void Legacy_bearer_audience_uses_client_id_when_audience_is_absent()
    {
        using var provider = BuildProvider(options => options.ClientId = "legacy-audience");

        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(new OidcAuthenticationOptions().JwtBearerScheme);

        Assert.Equal("legacy-audience", bearer.Audience);
    }

    [Fact]
    public async Task An_explicit_audience_does_not_register_interactive_oidc_without_a_client_id()
    {
        using var provider = BuildProvider(options =>
        {
            options.Authority = "https://issuer.example/";
            options.Audience = "worker-api";
        });

        var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(new OidcAuthenticationOptions().JwtBearerScheme);

        Assert.Equal("worker-api", bearer.Audience);
        Assert.DoesNotContain(schemes, scheme => scheme.Name == new OidcAuthenticationOptions().AuthenticationScheme);
    }

    [Fact]
    public void Makes_the_jwt_bearer_scheme_the_default_challenge_scheme_when_oidc_is_default()
    {
        using var provider = BuildProvider();

        var authenticationOptions = provider.GetService<IOptions<AuthenticationOptions>>()!.Value;

        var jwtScheme = new OidcAuthenticationOptions().JwtBearerScheme;
        Assert.Equal(jwtScheme, authenticationOptions.DefaultChallengeScheme);
        Assert.Equal(jwtScheme, authenticationOptions.DefaultAuthenticateScheme);
    }

    [Fact]
    public void Preserves_authentication_defaults_selected_by_the_host()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "host-scheme";
            options.DefaultAuthenticateScheme = "host-authenticate";
            options.DefaultChallengeScheme = "host-challenge";
        });
        services.AddFoundationIdentityOidc(options => options.IsDefault = true);
        using var provider = services.BuildServiceProvider();

        var authenticationOptions = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal("host-scheme", authenticationOptions.DefaultScheme);
        Assert.Equal("host-authenticate", authenticationOptions.DefaultAuthenticateScheme);
        Assert.Equal("host-challenge", authenticationOptions.DefaultChallengeScheme);
    }

    [Fact]
    public async Task Describe_advertises_no_challenge_and_is_never_default_when_unconfigured()
    {
        // No ClientId → the interactive handler is not registered, so challenging the provider's scheme would
        // 500. The descriptor must therefore surface no challenge (making /challenge/oidc a clean 404) and
        // must not present as the default interactive provider even when IsDefault is set.
        var module = new OidcAuthenticationProviderModule(Options.Create(new OidcAuthenticationOptions { IsDefault = true }));

        var descriptor = await module.DescribeAsync();

        Assert.Null(descriptor.Challenge);
        Assert.False(descriptor.IsDefault);
    }

    [Fact]
    public async Task Describe_advertises_the_challenge_and_honours_default_when_a_client_id_is_configured()
    {
        var module = new OidcAuthenticationProviderModule(
            Options.Create(new OidcAuthenticationOptions { ClientId = "elsa-client", IsDefault = true }));

        var descriptor = await module.DescribeAsync();

        Assert.NotNull(descriptor.Challenge);
        Assert.Equal(new OidcAuthenticationOptions().AuthenticationScheme, descriptor.Challenge!.Scheme);
        Assert.True(descriptor.IsDefault);
    }

    [Fact]
    public async Task External_oidc_metadata_keeps_the_token_refresh_permission_boundary()
    {
        var module = new OidcAuthenticationProviderModule(Options.Create(new OidcAuthenticationOptions()));

        var descriptor = await module.DescribeAsync();

        Assert.Equal(PermissionPropagationMode.TokenRefreshBoundary, module.Capabilities.PermissionPropagation);
        Assert.Equal(PermissionPropagationMode.TokenRefreshBoundary, descriptor.Capabilities.PermissionPropagation);
    }

    private static ServiceProvider BuildProvider(Action<OidcAuthenticationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationIdentityOidc(configure);
        return services.BuildServiceProvider();
    }

    private sealed class RegistrationTestClaimMappingStore : IClaimMappingStore
    {
        public ValueTask<IReadOnlyList<ClaimMappingRule>> ListForProviderAsync(
            string tenantId,
            string provider,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ClaimMappingRule>>([]);

        public ValueTask SaveAsync(ClaimMappingRule rule, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
