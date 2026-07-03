using CShells;
using CShells.FastEndpoints.Contracts;
using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Api.FastEndpoints.Configurators;
using Elsa.Api.FastEndpoints.Options;
using Elsa.Api.FastEndpoints.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Api.FastEndpoints.Tests;

/// <summary>
/// Feature-registration tests (constitution §2.23.1) for the per-shell API security surface: the
/// security configurator must be registered exactly once per shell hosting Elsa endpoints, and the
/// <see cref="ApiSecurityFeature"/> must bind its <see cref="ApiSecurityOptions"/> from its property.
/// </summary>
public sealed class ApiSecurityRegistrationTests
{
    [Fact]
    public void FastEndpointsFeatureBase_registers_the_security_configurator_exactly_once()
    {
        var services = new ServiceCollection();

        new TestFastEndpointsFeature().ConfigureServices(services);
        using var provider = BuildProvider(services);

        var securityConfigurators = provider.GetServices<IFastEndpointsConfigurator>()
            .OfType<ApiSecurityFastEndpointsConfigurator>()
            .ToList();

        Assert.Single(securityConfigurators);
    }

    [Fact]
    public void Registering_the_feature_twice_keeps_a_single_security_configurator()
    {
        var services = new ServiceCollection();
        var feature = new TestFastEndpointsFeature();

        feature.ConfigureServices(services);
        feature.ConfigureServices(services);
        using var provider = BuildProvider(services);

        var securityConfigurators = provider.GetServices<IFastEndpointsConfigurator>()
            .OfType<ApiSecurityFastEndpointsConfigurator>()
            .ToList();

        Assert.Single(securityConfigurators);
    }

    [Fact]
    public void ApiSecurityFeature_binds_options_from_its_properties()
    {
        var services = new ServiceCollection();
        var feature = new ApiSecurityFeature(CreateContext("payments"))
        {
            AllowAnonymous = true
        };

        feature.ConfigureServices(services);
        using var provider = BuildProvider(services);

        var options = provider.GetRequiredService<IOptions<ApiSecurityOptions>>().Value;

        Assert.True(options.AllowAnonymous);
        Assert.Equal("payments", options.ShellName);
    }

    [Fact]
    public void ApiSecurityFeature_defaults_to_secure()
    {
        var services = new ServiceCollection();
        var feature = new ApiSecurityFeature(CreateContext("orders"));

        feature.ConfigureServices(services);
        using var provider = BuildProvider(services);

        var options = provider.GetRequiredService<IOptions<ApiSecurityOptions>>().Value;

        Assert.False(options.AllowAnonymous);
        Assert.Equal("orders", options.ShellName);
    }

    private static ServiceProvider BuildProvider(IServiceCollection services)
    {
        services.AddOptions();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static ShellFeatureContext CreateContext(string shellName) =>
        new(new ShellSettings { Id = new ShellId(shellName) }, []);
}
