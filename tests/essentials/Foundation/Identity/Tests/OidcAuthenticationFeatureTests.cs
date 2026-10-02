using CShells.AspNetCore.Extensions;
using CShells.AspNetCore.Configuration;
using CShells.DependencyInjection;
using CShells.Features;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Oidc;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

/// <summary>
/// Composes <c>FoundationIdentityOidc</c> from shell configuration, the way a <c>shells.json</c> does (#1581). A setting
/// the feature does not declare is silently ignored, so before these settings existed an Authority or ClientId in
/// <c>shells.json</c> left the provider unconfigured: every external bearer token was refused as having an invalid
/// issuer, and the interactive OpenID Connect handler was never registered.
/// </summary>
public sealed class OidcAuthenticationFeatureTests : IAsyncDisposable
{
    private const string Authority = "https://idp.example.com/realms/elsa";
    private const string ClientId = "elsa-server";
    private const string ClientSecret = "client-secret";

    private static readonly OidcAuthenticationOptions Defaults = new();

    private static readonly Dictionary<string, string?> FullSettings = new()
    {
        ["IsDefault"] = "false",
        ["Authority"] = Authority,
        ["ClientId"] = ClientId,
        ["ClientSecret"] = ClientSecret,
        ["RequireHttpsMetadata"] = "false"
    };

    private WebApplication? _host;

    [Fact]
    public async Task Shell_settings_configure_the_provider_options()
    {
        var services = await ActivateAsync(FullSettings);

        var options = services.GetRequiredService<IOptions<OidcAuthenticationOptions>>().Value;

        Assert.Equal(Authority, options.Authority);
        Assert.Equal(ClientId, options.ClientId);
        Assert.Equal(ClientSecret, options.ClientSecret);
        Assert.False(options.RequireHttpsMetadata);
        Assert.False(options.IsDefault);
    }

    [Fact]
    public async Task Shell_settings_bind_the_independent_bearer_normalization_options()
    {
        var services = await ActivateAsync(new Dictionary<string, string?>
        {
            ["Authority"] = Authority,
            ["ClientId"] = ClientId,
            ["Audience"] = "elsa-worker",
            ["ProviderId"] = "provider-a",
            ["TenantId"] = "tenant-a",
            ["NormalizeBearerClaims"] = "false"
        });

        var options = services.GetRequiredService<IOptions<OidcAuthenticationOptions>>().Value;
        var bearer = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Defaults.JwtBearerScheme);

        Assert.Equal("elsa-worker", options.Audience);
        Assert.Equal("elsa-worker", bearer.Audience);
        Assert.Equal("provider-a", options.ProviderId);
        Assert.Equal("tenant-a", options.TenantId);
        Assert.False(options.NormalizeBearerClaims);
    }

    [Fact]
    public async Task A_client_id_setting_registers_and_configures_the_interactive_openid_connect_handler()
    {
        var services = await ActivateAsync(FullSettings);

        var schemes = await services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        var openIdConnect = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(Defaults.AuthenticationScheme);

        Assert.Contains(schemes, scheme => scheme.Name == Defaults.AuthenticationScheme);
        Assert.Equal(Authority, openIdConnect.Authority);
        Assert.Equal(ClientId, openIdConnect.ClientId);
        Assert.Equal(ClientSecret, openIdConnect.ClientSecret);
        Assert.False(openIdConnect.RequireHttpsMetadata);
    }

    [Fact]
    public async Task Authority_and_client_id_settings_configure_bearer_validation()
    {
        var services = await ActivateAsync(FullSettings);

        var jwtBearer = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Defaults.JwtBearerScheme);

        Assert.Equal(Authority, jwtBearer.Authority);
        Assert.Equal(ClientId, jwtBearer.Audience);
        Assert.False(jwtBearer.RequireHttpsMetadata);
    }

    [Fact]
    public async Task An_omitted_audience_setting_keeps_the_legacy_client_id_fallback()
    {
        var services = await ActivateAsync(new Dictionary<string, string?>
        {
            ["Authority"] = Authority,
            ["ClientId"] = ClientId
        });

        var jwtBearer = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Defaults.JwtBearerScheme);

        Assert.Equal(ClientId, jwtBearer.Audience);
    }

    [Fact]
    public async Task An_authority_without_a_client_id_validates_bearer_tokens_but_registers_no_interactive_handler()
    {
        var services = await ActivateAsync(new Dictionary<string, string?> { ["Authority"] = Authority });

        var schemes = await services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        var jwtBearer = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Defaults.JwtBearerScheme);

        Assert.DoesNotContain(schemes, scheme => scheme.Name == Defaults.AuthenticationScheme);
        Assert.Equal(Authority, jwtBearer.Authority);
        Assert.True(jwtBearer.RequireHttpsMetadata);
    }

    [Fact]
    public async Task An_audience_setting_without_a_client_id_configures_only_the_bearer_handler()
    {
        var services = await ActivateAsync(new Dictionary<string, string?>
        {
            ["Authority"] = Authority,
            ["Audience"] = "worker-api"
        });

        var schemes = await services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        var jwtBearer = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Defaults.JwtBearerScheme);

        Assert.DoesNotContain(schemes, scheme => scheme.Name == Defaults.AuthenticationScheme);
        Assert.Equal("worker-api", jwtBearer.Audience);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task A_declarative_blank_audience_is_preserved_and_does_not_fall_back(string audience)
    {
        var services = await ActivateAsync(new Dictionary<string, string?>
        {
            ["Authority"] = Authority,
            ["ClientId"] = ClientId,
            ["Audience"] = audience,
            ["NormalizeBearerClaims"] = "false"
        });
        var options = services.GetRequiredService<IOptions<OidcAuthenticationOptions>>().Value;
        var jwtBearer = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Defaults.JwtBearerScheme);

        Assert.Equal(audience, options.Audience);
        Assert.Equal(ClientId, options.ClientId);
        Assert.Equal(audience, jwtBearer.Audience);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Opt_in_with_a_declarative_blank_audience_is_refused_during_activation(string audience)
    {
        var host = await ShellActivationHost.StartFromConfigurationAsync<OidcAuthenticationFeature>(
            Environments.Development,
            "FoundationIdentityOidc",
            new Dictionary<string, string?>
            {
                ["Authority"] = Authority,
                ["ClientId"] = ClientId,
                ["Audience"] = audience,
                ["ProviderId"] = "provider-a",
                ["TenantId"] = "tenant-a",
                ["NormalizeBearerClaims"] = "true"
            });
        _host = host;

        var exception = await Record.ExceptionAsync(async () => await host.ActivateShellAsync());

        Assert.NotNull(exception);
        Assert.Contains("oidc-normalization-configuration-invalid", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Valid_declarative_opt_in_activates_with_a_matching_persistence_scope_and_mapping_store()
    {
        const string tenant = "tenant-a";
        const string provider = "provider-a";
        var host = await StartFromConfigurationWithServicesAsync<OidcAuthenticationFeature>(
            Environments.Development,
            "FoundationIdentityOidc",
            new Dictionary<string, string?>
            {
                ["IsDefault"] = "false",
                ["Authority"] = Authority,
                ["Audience"] = "elsa-worker",
                ["ProviderId"] = provider,
                ["TenantId"] = tenant,
                ["RequireHttpsMetadata"] = "false",
                ["NormalizeBearerClaims"] = "true"
            },
            services =>
            {
                services.AddPersistenceCore(tenant);
                services.RemoveAll<IClaimMappingStore>();
                services.AddSingleton<IClaimMappingStore, ActivationTestClaimMappingStore>();
            });
        _host = host;

        var shell = await host.ActivateShellAsync();
        var serviceProvider = shell.ServiceProvider;
        var options = serviceProvider.GetRequiredService<IOptions<OidcAuthenticationOptions>>().Value;
        var bearer = serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(Defaults.JwtBearerScheme);

        Assert.True(options.NormalizeBearerClaims);
        Assert.Equal("elsa-worker", options.Audience);
        Assert.Equal(provider, options.ProviderId);
        Assert.Equal(tenant, options.TenantId);
        Assert.Equal(typeof(OidcBearerNormalizationEvents), bearer.EventsType);
        Assert.IsAssignableFrom<IClaimMappingStore>(serviceProvider.GetRequiredService<IClaimMappingStore>());
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    private async Task<IServiceProvider> ActivateAsync(IReadOnlyDictionary<string, string?> settings)
    {
        _host = await ShellActivationHost.StartFromConfigurationAsync<OidcAuthenticationFeature>(
            Environments.Development,
            "FoundationIdentityOidc",
            settings);
        var shell = await _host.ActivateShellAsync();
        return shell.ServiceProvider;
    }

    private static async Task<WebApplication> StartFromConfigurationWithServicesAsync<TFeature>(
        string environment,
        string featureName,
        IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection> configureServices)
        where TFeature : class, IShellFeature
    {
        const string shellName = "identity-activation-probe";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings.Select(setting =>
            KeyValuePair.Create($"CShells:Shells:{shellName}:Features:{featureName}:{setting.Key}", setting.Value)));
        configureServices(builder.Services);
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(typeof(TFeature).Assembly)
            .WithConfigurationProvider(builder.Configuration));

        var host = builder.Build();
        host.MapShells();
        try
        {
            await host.StartAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private sealed class ActivationTestClaimMappingStore : IClaimMappingStore
    {
        public ValueTask<IReadOnlyList<ClaimMappingRule>> ListForProviderAsync(
            string tenantId,
            string provider,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ClaimMappingRule>>([]);

        public ValueTask SaveAsync(ClaimMappingRule rule, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
