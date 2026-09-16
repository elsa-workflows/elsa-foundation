using Elsa.Foundation.Identity.Authentication;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Ownership;
using System.Net;
using System.Text.Encodings.Web;
using CShells.AspNetCore.Features;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

public sealed class FoundationIdentityAbstractionsFeatureTests
{
    [Fact]
    public void RegistersContractDefaults()
    {
        var services = new ServiceCollection();

        new FoundationIdentityAbstractionsFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAuthenticationProviderResolver>());
        Assert.NotNull(provider.GetRequiredService<IPermissionCatalog>());
        Assert.NotNull(provider.GetRequiredService<IPermissionEvaluator>());
        Assert.NotNull(provider.GetRequiredService<IClaimsNormalizer>());
        Assert.NotNull(provider.GetRequiredService<IOwnershipModeProvider>());
        Assert.NotNull(provider.GetRequiredService<IEffectiveCapabilitiesResolver>());
        Assert.IsType<RequirePermissionPolicyProvider>(provider.GetRequiredService<IAuthorizationPolicyProvider>());
    }

    [Fact]
    public void InstallsAuthenticationAndAuthorizationMarkerServices()
    {
        // AddFoundationIdentityAbstractions calls AddAuthorizationCore, which registers the policy engine but
        // not the marker service UseAuthorization asserts on. Without these the shell fails to compose its
        // pipeline and answers 503 to everything, so assert the markers specifically.
        var services = new ServiceCollection();
        services.AddLogging();

        new FoundationIdentityAbstractionsFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAuthenticationSchemeProvider>());
        Assert.NotNull(provider.GetRequiredService<IAuthorizationHandlerProvider>());
        Assert.NotNull(provider.GetRequiredService<IAuthorizationService>());
    }

    [Fact]
    public void IsAMiddlewareShellFeature()
    {
        // The host installs no auth middleware: Elsa.Foundation.Host composes no feature, so this feature
        // carries it into the shell pipeline instead.
        Assert.IsAssignableFrom<IMiddlewareShellFeature>(new FoundationIdentityAbstractionsFeature());
    }

    [Fact]
    public async Task UseMiddlewareSatisfiesEndpointAuthorizationMetadata()
    {
        // Regression guard for "Endpoint ... contains authorization metadata, but a middleware was not found
        // that supports authorization" — the 500 every authorized endpoint returned on Elsa.Foundation.Host.
        // A raw WebHost is used rather than WebApplication because WebApplication auto-inserts the auth
        // middleware when the services are present, which would make this pass with or without the feature.
        using var host = await StartHostAsync(installAuthMiddleware: true);

        var response = await host.GetTestClient().GetAsync("/protected");

        // Reached authorization and was challenged: no scheme is configured in this test, so 401 rather than 500.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WithoutTheMiddlewareAnAuthorizedEndpointFails()
    {
        // Negative control: proves the test above detects the defect rather than passing for another reason.
        // This is exactly what Elsa.Foundation.Host did before the feature installed the middleware.
        using var host = await StartHostAsync(installAuthMiddleware: false);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.GetTestClient().GetAsync("/protected"));

        Assert.Contains("contains authorization metadata", failure.Message);
    }

    private static Task<IHost> StartHostAsync(bool installAuthMiddleware)
    {
        var feature = new FoundationIdentityAbstractionsFeature();

        return new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    feature.ConfigureServices(services);

                    // A real deployment gets its scheme from a provider feature (OpenIddict, OIDC,
                    // AspNetCoreIdentity). This stands in for one so a challenge yields 401 rather than
                    // throwing for want of a default scheme; the feature under test supplies the middleware.
                    services.AddAuthentication(TestScheme)
                        .AddScheme<AuthenticationSchemeOptions, AnonymousSchemeHandler>(TestScheme, configureOptions: null);
                })
                .Configure(app =>
                {
                    app.UseRouting();

                    if (installAuthMiddleware)
                        feature.UseMiddleware(app, environment: null);

                    app.UseEndpoints(endpoints => endpoints
                        .MapGet("/protected", () => Results.Ok())
                        .RequireAuthorization());
                }))
            .StartAsync();
    }

    [Fact]
    public async Task ProviderManagerComposesEnabledProviderModules()
    {
        var manager = new DefaultAuthenticationProviderResolver(
        [
            new TestProviderModule(new("external", "External", "external-oidc", ProviderCapabilities.ExternalOidcDefault, Enabled: true)),
            new TestProviderModule(new("disabled", "Disabled", "external-oidc", ProviderCapabilities.ExternalOidcDefault, Enabled: false)),
            new TestProviderModule(new("builtin", "Built-in", "openiddict", ProviderCapabilities.FoundationReference, Enabled: true, IsDefault: true))
        ]);

        var providers = await manager.ListAsync();
        var found = await manager.FindAsync("builtin");

        Assert.Collection(
            providers,
            x => Assert.Equal("builtin", x.Id),
            x => Assert.Equal("external", x.Id));
        Assert.Equal("builtin", found?.Id);
    }

    [Fact]
    public async Task ProviderManagerRequiresExplicitGlobalFallbackForTenantLookup()
    {
        var manager = new DefaultAuthenticationProviderResolver(
        [
            new TestProviderModule(new("entra", "Global Entra", "external-oidc", ProviderCapabilities.ExternalOidcDefault)),
            new TestProviderModule(new("entra", "Tenant Entra", "external-oidc", ProviderCapabilities.ExternalOidcDefault, TenantId: "tenant-a"))
        ]);

        var tenantProvider = await manager.FindAsync("entra", "tenant-a");
        var missingWithoutFallback = await manager.FindAsync("entra", "tenant-b");
        var missingWithFallback = await manager.FindAsync("entra", "tenant-b", allowGlobalFallback: true);

        Assert.Equal("Tenant Entra", tenantProvider?.DisplayName);
        Assert.Null(missingWithoutFallback);
        Assert.Equal("Global Entra", missingWithFallback?.DisplayName);
    }

    private const string TestScheme = "Test";

    /// <summary>Authenticates nobody, so an authorized endpoint is challenged rather than allowed.</summary>
    private sealed class AnonymousSchemeHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class TestProviderModule(AuthenticationProviderDescriptor descriptor) : IAuthenticationProviderModule
    {
        public string ProviderId => descriptor.Id;

        public string DisplayName => descriptor.DisplayName;

        public string Kind => descriptor.Kind;

        public ProviderCapabilities Capabilities => descriptor.Capabilities;

        public ValueTask<AuthenticationProviderDescriptor> DescribeAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(descriptor);
    }
}
